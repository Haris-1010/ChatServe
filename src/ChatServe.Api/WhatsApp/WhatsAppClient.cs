using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChatServe.Api.Configuration;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.WhatsApp;

public interface IWhatsAppClient
{
    /// <summary>Sends a text message. Returns the WhatsApp message id (wamid...).</summary>
    Task<string?> SendTextAsync(string phoneNumberId, string to, string body,
        string? replyToMessageId = null, CancellationToken ct = default);

    /// <summary>Shows the customer blue ticks and starts the "typing..." indicator.</summary>
    Task MarkAsReadAsync(string phoneNumberId, string messageId, bool showTyping = true,
        CancellationToken ct = default);
}

public sealed class WhatsAppApiException(string message, int statusCode, string? responseBody)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string? ResponseBody { get; } = responseBody;
}

/// <summary>
/// A plain HttpClient wrapper for the Graph API (no SDK).
/// POST {base}/{version}/{phone-number-id}/messages
/// </summary>
public sealed class WhatsAppClient(HttpClient http, IOptionsMonitor<WhatsAppOptions> options, ILogger<WhatsAppClient> logger)
    : IWhatsAppClient
{
    /// <summary>WhatsApp text messages are limited to 4096 characters.</summary>
    public const int MaxTextLength = 4096;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // don't escape Urdu/emoji as \uXXXX
    };

    public async Task<string?> SendTextAsync(string phoneNumberId, string to, string body,
        string? replyToMessageId = null, CancellationToken ct = default)
    {
        if (body.Length > MaxTextLength)
            body = body[..(MaxTextLength - 1)] + "…";

        var payload = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to,
            type = "text",
            context = replyToMessageId is null ? null : new { message_id = replyToMessageId },
            text = new { preview_url = false, body }
        };

        using var doc = await PostAsync(phoneNumberId, payload, ct);
        var id = doc.RootElement.TryGetProperty("messages", out var msgs) && msgs.GetArrayLength() > 0
            ? msgs[0].GetProperty("id").GetString()
            : null;

        logger.LogInformation("Reply sent to {To} (wamid {MessageId})", Mask(to), id);
        return id;
    }

    public async Task MarkAsReadAsync(string phoneNumberId, string messageId, bool showTyping = true,
        CancellationToken ct = default)
    {
        var payload = new
        {
            messaging_product = "whatsapp",
            status = "read",
            message_id = messageId,
            typing_indicator = showTyping ? new { type = "text" } : null
        };

        using var _ = await PostAsync(phoneNumberId, payload, ct);
    }

    private async Task<JsonDocument> PostAsync(string phoneNumberId, object payload, CancellationToken ct)
    {
        var opts = options.CurrentValue;
        var url = $"{opts.GraphApiBaseUrl.TrimEnd('/')}/{opts.GraphApiVersion}/{phoneNumberId}/messages";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            // StringContent: the body is buffered and a Content-Length header is sent (not chunked).
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.AccessToken);

        using var response = await http.SendAsync(request, ct);
        var content = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // Common errors: 190 = token expired/invalid, 131030 = number not in the allowed list (test mode),
            // 131047 = the 24-hour window has closed (only a template can be sent).
            logger.LogError("WhatsApp API error {Status}: {Body}", (int)response.StatusCode, content);
            throw new WhatsAppApiException($"WhatsApp API returned {(int)response.StatusCode}",
                (int)response.StatusCode, content);
        }

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
    }

    /// <summary>Keep full phone numbers out of the logs.</summary>
    internal static string Mask(string phone) =>
        phone.Length <= 4 ? "****" : new string('*', phone.Length - 4) + phone[^4..];
}
