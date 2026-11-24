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
    ILogger<WebhookController> logger) : ControllerBase
{
    /// <summary>
    /// Meta verification. Dashboard mein "Verify and save" dabane par Meta ye call karta hai:
    /// GET /webhook?hub.mode=subscribe&amp;hub.verify_token=XYZ&amp;hub.challenge=12345
    /// Token match ho to challenge plain text mein wapas karna hai.
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
    /// Meta yahan har naya message aur status update bhejta hai.
    /// Foran 200 OK dena hai, asal kaam background queue mein hota hai.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        // Signature check ke liye raw bytes chahiye, is liye [FromBody] use nahi kiya.
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

        foreach (var message in WebhookParser.ExtractMessages(payload, opts.PhoneNumberId))
        {
            if (!queue.TryEnqueue(message))
                logger.LogError("Message queue full, dropped {MessageId}", message.MessageId);
        }

        return Ok();
    }
}
