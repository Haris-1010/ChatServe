using System.Text.Json;
using ChatServe.Api.Configuration;
using ChatServe.Api.Messaging;
using ChatServe.Api.WhatsApp;
using ChatServe.Api.WhatsApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Controllers;

[ApiController]
[Route("webhook")]
public sealed class WebhookController(
    IOptionsMonitor<WhatsAppOptions> options,
    IMessageQueue queue,
    IPhoneSyncQueue phoneSync,
    ILogger<WebhookController> logger) : ControllerBase
{
    /// <summary>
    /// Meta verification. Meta calls this when you click "Verify and save" in its dashboard:
    /// GET /webhook?hub.mode=subscribe&amp;hub.verify_token=XYZ&amp;hub.challenge=12345
    /// If the token matches, return the challenge as plain text.
    /// </summary>
    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var expected = options.CurrentValue.VerifyToken;

        if (mode == "subscribe" && !string.IsNullOrEmpty(expected) && verifyToken == expected && challenge is not null)
        {
            logger.LogInformation("Webhook verified by Meta");
            return Content(challenge, "text/plain");
        }

        logger.LogWarning("Webhook verification failed (mode={Mode})", mode);
        return StatusCode(StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Meta sends every new message and status update here.
    /// Return 200 OK immediately; the real work happens in the background queue.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        // The signature check needs the raw bytes, so [FromBody] isn't used.
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct);
        var rawBody = ms.ToArray();

        var opts = options.CurrentValue;
        if (opts.ValidateSignature)
        {
            var signature = Request.Headers[WebhookSignatureValidator.HeaderName].ToString();
            if (!WebhookSignatureValidator.IsValid(rawBody, signature, opts.AppSecret))
            {
                logger.LogWarning("Rejected webhook with invalid signature");
                return Unauthorized();
            }
        }

        WebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebhookPayload>(rawBody);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Invalid webhook JSON");
            return BadRequest();
        }

        if (payload is null)
            return BadRequest();

        // Customer messages, and (coexistence) replies the owner sent from the WhatsApp Business app.
        // Both go on the same queue so one customer's messages are handled in order.
        foreach (var message in WebhookParser.ExtractMessages(payload, opts.PhoneNumberId)
                     .Concat(WebhookParser.ExtractPhoneReplies(payload, opts.PhoneNumberId)))
        {
            if (!queue.TryEnqueue(message))
                logger.LogError("Message queue full, dropped {MessageId}", message.MessageId);
        }

        // Coexistence: old chats and contacts from the owner's phone (imported in the background).
        foreach (var batch in WebhookParser.ExtractPhoneSync(payload, opts.PhoneNumberId))
        {
            if (!phoneSync.TryEnqueue(batch))
                logger.LogError("Phone sync queue full, dropped a batch of {Count} messages", batch.Messages.Count);
        }

        return Ok();
    }
}
