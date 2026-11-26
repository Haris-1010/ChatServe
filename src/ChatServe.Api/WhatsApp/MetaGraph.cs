using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ChatServe.Api.Configuration;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.WhatsApp;

/// <summary>The answer of one Graph API call: Ok, Meta's error text, and the JSON body when it worked.</summary>
public sealed record GraphResult(bool Ok, string Message, JsonElement? Body = null);

/// <summary>
/// Graph API calls for connecting a number (Embedded Signup) and coexistence setup.
/// Message sending stays in <see cref="WhatsAppClient"/>.
/// </summary>
public sealed class MetaGraph(HttpClient http, IOptionsMonitor<WhatsAppOptions> options, ILogger<MetaGraph> logger)
{
    private string Base => $"{options.CurrentValue.GraphApiBaseUrl.TrimEnd('/')}/{options.CurrentValue.GraphApiVersion}";

    /// <summary>Embedded Signup gives the browser a short-lived code; the server swaps it for the client's business token.</summary>
    public Task<GraphResult> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        var o = options.CurrentValue;
        var url = $"{Base}/oauth/access_token?client_id={Uri.EscapeDataString(o.AppId)}" +
                  $"&client_secret={Uri.EscapeDataString(o.AppSecret)}&code={Uri.EscapeDataString(code)}";
        return SendAsync(HttpMethod.Get, url, token: null, body: null, ct);
    }

    /// <summary>Webhooks for this WABA's numbers come to our app only after this.</summary>
    public Task<GraphResult> SubscribeAppAsync(string wabaId, string token, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"{Base}/{Uri.EscapeDataString(wabaId)}/subscribed_apps", token, new { }, ct);

    /// <summary>A number that lives only on the API (not coexistence) must be registered once, with a 6-digit PIN.</summary>
    public Task<GraphResult> RegisterAsync(string phoneNumberId, string pin, string token, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"{Base}/{Uri.EscapeDataString(phoneNumberId)}/register", token,
            new { messaging_product = "whatsapp", pin }, ct);

    /// <summary>
    /// Coexistence: ask Meta to send the phone's contacts ("smb_app_state_sync") or chat history ("history") to our webhook.
    /// Allowed within 24 hours of onboarding.
    /// </summary>
    public Task<GraphResult> RequestPhoneSyncAsync(string phoneNumberId, string syncType, string token, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"{Base}/{Uri.EscapeDataString(phoneNumberId)}/smb_app_data", token,
            new { messaging_product = "whatsapp", sync_type = syncType }, ct);

    public Task<GraphResult> PhoneNumbersAsync(string wabaId, string token, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, $"{Base}/{Uri.EscapeDataString(wabaId)}/phone_numbers?fields=id,display_phone_number,verified_name", token, null, ct);

    public Task<GraphResult> PhoneInfoAsync(string phoneNumberId, string token, CancellationToken ct) =>
        SendAsync(HttpMethod.Get,
            $"{Base}/{Uri.EscapeDataString(phoneNumberId)}?fields=display_phone_number,verified_name,quality_rating,platform_type,status",
            token, null, ct);

    private async Task<GraphResult> SendAsync(HttpMethod method, string url, string? token, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        try
        {
            using var response = await http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            JsonElement? json = null;
            try { json = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text).RootElement.Clone(); }
            catch (JsonException) { /* not JSON */ }

            if (response.IsSuccessStatusCode)
                return new GraphResult(true, "OK", json);

            var message = json is { } j && j.TryGetProperty("error", out var e)
                ? (e.TryGetProperty("error_user_msg", out var um) ? um.GetString() : null)
                  ?? (e.TryGetProperty("message", out var m) ? m.GetString() : null)
                  ?? text
                : text;
            var code = json is { } j2 && j2.TryGetProperty("error", out var e2) && e2.TryGetProperty("code", out var c) ? $" (code {c})" : "";
            logger.LogWarning("Graph API {Method} {Path} failed: {Status}{Code} {Message}",
                method, new Uri(url).AbsolutePath, (int)response.StatusCode, code, message);
            return new GraphResult(false, $"{message}{code}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new GraphResult(false, "Could not reach Meta: " + ex.Message);
        }
    }
}
