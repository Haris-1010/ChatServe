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

    /// <summary>
    /// Sends text with buttons or a list under it. Implementations without buttons (test fakes) send the plain-text version.
    /// </summary>
    Task<string?> SendInteractiveAsync(string phoneNumberId, string to, string body, InteractiveMessage interactive,
        CancellationToken ct = default) =>
        SendTextAsync(phoneNumberId, to, interactive.AsPlainText(body), replyToMessageId: null, ct);

    /// <summary>Downloads a media file a customer sent (e.g. a voice note): first its URL from the Graph API, then the bytes.</summary>
    Task<MediaFile> DownloadMediaAsync(string mediaId, CancellationToken ct = default);

    /// <summary>Shows the customer blue ticks and starts the "typing..." indicator.</summary>
    Task MarkAsReadAsync(string phoneNumberId, string messageId, bool showTyping = true,
        CancellationToken ct = default);
}

public sealed record MediaFile(byte[] Data, string MimeType);

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

    public async Task<string?> SendInteractiveAsync(string phoneNumberId, string to, string body, InteractiveMessage interactive,
        CancellationToken ct = default)
    {
        // Interactive text is limited to 1024 characters: send a long text on its own, then the buttons with a short line.
        if (body.Length > InteractiveMessage.MaxBodyLength)
        {
            await SendTextAsync(phoneNumberId, to, body, replyToMessageId: null, ct);
            body = "👇";
        }

        try
        {
            using var doc = await PostAsync(phoneNumberId, BuildInteractive(to, body, interactive), ct);
            var id = doc.RootElement.TryGetProperty("messages", out var msgs) && msgs.GetArrayLength() > 0
                ? msgs[0].GetProperty("id").GetString()
                : null;
            logger.LogInformation("Reply with {Count} options sent to {To} (wamid {MessageId})", interactive.Options.Count, Mask(to), id);
            return id;
        }
        catch (WhatsAppApiException ex) when (ex.StatusCode == 400 && ex.ResponseBody?.Contains("131047") != true)
        {
            // Buttons refused (e.g. a title too long): the customer still gets the message, with numbered options.
            logger.LogWarning("Buttons refused by WhatsApp, sending plain text instead");
            return await SendTextAsync(phoneNumberId, to, interactive.AsPlainText(body), replyToMessageId: null, ct);
        }
    }

    internal static object BuildInteractive(string to, string body, InteractiveMessage m)
    {
        var header = string.IsNullOrWhiteSpace(m.Header) ? null : new { type = "text", text = Cut(m.Header, 60) };
        var footer = string.IsNullOrWhiteSpace(m.Footer) ? null : new { text = Cut(m.Footer, 60) };
        object action = m.IsList
            ? new
            {
                button = Cut(m.ListButton!, 20),
                sections = new[]
                {
                    new
                    {
                        title = Cut(m.Header ?? "Options", 24),
                        rows = m.Options.Take(InteractiveMessage.MaxRows).Select(o => new
                        {
                            id = Cut(o.Id, 200),
                            title = Cut(o.Title, 24),
                            description = o.Description is null ? null : Cut(o.Description, 72),
                        }).ToArray(),
                    },
                },
            }
            : new
            {
                buttons = m.Options.Take(InteractiveMessage.MaxButtons).Select(o => new
                {
                    type = "reply",
                    reply = new { id = Cut(o.Id, 256), title = Cut(o.Title, 20) },
                }).ToArray(),
            };

        return new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to,
            type = "interactive",
            interactive = new { type = m.IsList ? "list" : "button", header, body = new { text = body }, footer, action },
        };
    }

    /// <summary>Cuts by text elements, so an emoji is never split in half.</summary>
    private static string Cut(string s, int max)
    {
        if (s.Length <= max) return s;
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(s);
        var sb = new StringBuilder();
        while (e.MoveNext())
        {
            var part = (string)e.Current;
            if (sb.Length + part.Length > max) break;
            sb.Append(part);
        }
        return sb.ToString();
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

    /// <summary>Voice notes are small; refuse anything big so a strange file can't fill memory.</summary>
    private const long MaxMediaBytes = 16 * 1024 * 1024;

    public async Task<MediaFile> DownloadMediaAsync(string mediaId, CancellationToken ct = default)
    {
        var opts = options.CurrentValue;
        using var infoRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{opts.GraphApiBaseUrl.TrimEnd('/')}/{opts.GraphApiVersion}/{Uri.EscapeDataString(mediaId)}");
        infoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.AccessToken);
        using var infoResponse = await http.SendAsync(infoRequest, ct);
        var infoBody = await infoResponse.Content.ReadAsStringAsync(ct);
        if (!infoResponse.IsSuccessStatusCode)
            throw new WhatsAppApiException($"Media info returned {(int)infoResponse.StatusCode}", (int)infoResponse.StatusCode, infoBody);

        using var info = JsonDocument.Parse(infoBody);
        var url = info.RootElement.TryGetProperty("url", out var u) ? u.GetString() : null;
        var mime = info.RootElement.TryGetProperty("mime_type", out var m) ? m.GetString() : null;
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new WhatsAppApiException("Media info had no download URL", 0, infoBody);

        using var fileRequest = new HttpRequestMessage(HttpMethod.Get, uri);
        fileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opts.AccessToken);
        using var fileResponse = await http.SendAsync(fileRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!fileResponse.IsSuccessStatusCode)
            throw new WhatsAppApiException($"Media download returned {(int)fileResponse.StatusCode}", (int)fileResponse.StatusCode, null);
        if (fileResponse.Content.Headers.ContentLength > MaxMediaBytes)
            throw new WhatsAppApiException("Media file is too large", 0, null);

        var data = await fileResponse.Content.ReadAsByteArrayAsync(ct);
        if (data.Length > MaxMediaBytes)
            throw new WhatsAppApiException("Media file is too large", 0, null);
        return new MediaFile(data, mime ?? fileResponse.Content.Headers.ContentType?.MediaType ?? "audio/ogg");
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
