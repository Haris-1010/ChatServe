using System.Security.Cryptography;
using System.Text.Json;
using ChatServe.Api.Configuration;
using ChatServe.Api.Data;
using ChatServe.Api.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Dashboard;

/// <summary>
/// Admin: how a restaurant's WhatsApp number is connected.
/// - Classic (Cloud API only): the number was added in Meta by hand; the app-wide token in Settings is used. Nothing changes.
/// - Embedded Signup: the client logs in with Facebook from the restaurant page and picks their number. With
///   "coexistence" the number stays in their WhatsApp Business app too; owner replies from the phone pause the bot.
/// </summary>
public static class WhatsAppConnectEndpoints
{
    public const string ModeCloudApi = "CloudApi";
    public const string ModeCoexistence = "Coexistence";

    public static void MapWhatsAppConnectEndpoints(this RouteGroupBuilder admin)
    {
        // What the browser needs to open Meta's Embedded Signup popup.
        admin.MapGet("/whatsapp/signup-config", (IOptionsMonitor<WhatsAppOptions> options) =>
        {
            var o = options.CurrentValue;
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(o.AppId)) missing.Add("App ID");
            if (string.IsNullOrWhiteSpace(o.EmbeddedSignupConfigId)) missing.Add("Embedded Signup configuration ID");
            if (string.IsNullOrWhiteSpace(o.AppSecret)) missing.Add("App secret");
            return Results.Ok(new SignupConfig(o.AppId, o.EmbeddedSignupConfigId, o.GraphApiVersion, missing.Count == 0, missing));
        });

        var one = admin.MapGroup("/restaurants/{rid:int}/whatsapp");

        one.MapGet("", async (int rid, AppDbContext db, CancellationToken ct) =>
            await db.Restaurants.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rid, ct) is { } r
                ? Results.Ok(ToDto(r))
                : Results.NotFound());

        // Manual changes, e.g. a number onboarded through a partner, or turning the phone-reply pause off.
        one.MapPut("", async (int rid, ConnectionUpdate input, AppDbContext db, WhatsAppTokens tokens, CancellationToken ct) =>
        {
            var r = await db.Restaurants.FirstOrDefaultAsync(x => x.Id == rid, ct);
            if (r is null) return Results.NotFound();
            if (!TryParseMode(input.Mode, out var mode))
                return RestaurantEndpoints.Bad("Mode must be CloudApi or Coexistence.");

            r.WhatsAppMode = mode;
            r.WhatsAppBusinessAccountId = Clean(input.WhatsAppBusinessAccountId);
            r.PauseBotOnPhoneReply = input.PauseBotOnPhoneReply;
            if (input.AccessToken is not null) // null = keep, "" = remove (use the app-wide token again)
                r.WhatsAppAccessToken = Clean(input.AccessToken);
            await db.SaveChangesAsync(ct);
            tokens.Forget(r.WhatsAppPhoneNumberId);
            return Results.Ok(ToDto(r));
        });

        // The end of Embedded Signup: swap the code for a token, subscribe our app, then register (Cloud API) or
        // ask for the phone's contacts and history (coexistence). Each step's result is returned.
        one.MapPost("/connect", async (int rid, ConnectRequest input, AppDbContext db, MetaGraph graph, WhatsAppTokens tokens,
            IOptionsMonitor<WhatsAppOptions> options, TimeProvider clock, CancellationToken ct) =>
        {
            var r = await db.Restaurants.FirstOrDefaultAsync(x => x.Id == rid, ct);
            if (r is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.Code))
                return RestaurantEndpoints.Bad("Meta did not return a login code. Please try again.");
            if (string.IsNullOrWhiteSpace(input.WabaId))
                return RestaurantEndpoints.Bad("Meta did not return the WhatsApp Business account. Please finish all steps in the popup.");
            if (!TryParseMode(input.Mode, out var mode))
                return RestaurantEndpoints.Bad("Mode must be CloudApi or Coexistence.");
            var o = options.CurrentValue;
            if (string.IsNullOrWhiteSpace(o.AppId) || string.IsNullOrWhiteSpace(o.AppSecret))
                return RestaurantEndpoints.Bad("Set the App ID and App secret in Settings > WhatsApp first.");

            var steps = new List<Step>();
            var exchange = await graph.ExchangeCodeAsync(input.Code.Trim(), ct);
            var token = exchange.Body is { } b && b.TryGetProperty("access_token", out var t) ? t.GetString() : null;
            if (!exchange.Ok || string.IsNullOrEmpty(token))
                return RestaurantEndpoints.Bad("Could not get a token from Meta: " + exchange.Message);
            steps.Add(new("Token from Meta", true, "Received"));

            var wabaId = input.WabaId.Trim();
            var phoneId = Clean(input.PhoneNumberId);
            if (phoneId is null)
            {
                // Meta normally sends the Phone number ID; if not, a WABA with exactly one number is clear enough.
                var list = await graph.PhoneNumbersAsync(wabaId, token, ct);
                var ids = list.Body is { } lb && lb.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                    ? data.EnumerateArray().Select(x => x.TryGetProperty("id", out var id) ? id.GetString() : null).OfType<string>().ToList()
                    : [];
                if (ids.Count != 1)
                    return RestaurantEndpoints.Bad(ids.Count == 0
                        ? "No phone number was found in that WhatsApp Business account."
                        : "That WhatsApp Business account has several numbers. Enter the Phone number ID by hand.");
                phoneId = ids[0];
            }
            if (await db.Restaurants.AnyAsync(x => x.Id != rid && x.WhatsAppPhoneNumberId == phoneId, ct))
                return RestaurantEndpoints.Bad($"Phone number ID {phoneId} already belongs to another restaurant.");

            // Save first: even if a later step fails, the token and number are kept and the step can be retried.
            var oldPhoneId = r.WhatsAppPhoneNumberId;
            var now = clock.GetUtcNow();
            r.WhatsAppPhoneNumberId = phoneId;
            r.WhatsAppBusinessAccountId = wabaId;
            r.WhatsAppAccessToken = token;
            r.WhatsAppMode = mode;
            r.WhatsAppConnectedAt = now;
            await db.SaveChangesAsync(ct);
            tokens.Forget(oldPhoneId);
            tokens.Forget(phoneId);
            steps.Add(new("Saved", true, $"Phone number ID {phoneId}, WhatsApp Business account {wabaId}"));

            var sub = await graph.SubscribeAppAsync(wabaId, token, ct);
            steps.Add(new("Webhooks (subscribe app)", sub.Ok, sub.Ok ? "Messages for this number will reach ChatServe" : sub.Message));

            string? pin = null;
            if (mode == WhatsAppMode.CloudApi)
            {
                pin = string.IsNullOrWhiteSpace(input.Pin) ? RandomPin() : input.Pin.Trim();
                if (pin.Length != 6 || !pin.All(char.IsAsciiDigit))
                    steps.Add(new("Register number", false, "The PIN must be 6 digits."));
                else
                {
                    var reg = await graph.RegisterAsync(phoneId, pin, token, ct);
                    steps.Add(new("Register number", reg.Ok, reg.Ok
                        ? $"Registered. Two-step verification PIN: {pin} (write it down)"
                        : reg.Message));
                }
            }
            else
            {
                steps.AddRange(await RequestSyncAsync(r, graph, token, now, ct));
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new ConnectResult(steps.All(s => s.Ok), steps, ToDto(r)));
        });

        // Coexistence: ask again for the phone's contacts and history (Meta allows it within 24 hours of onboarding).
        one.MapPost("/sync", async (int rid, AppDbContext db, MetaGraph graph, WhatsAppTokens tokens, TimeProvider clock, CancellationToken ct) =>
        {
            var r = await db.Restaurants.FirstOrDefaultAsync(x => x.Id == rid, ct);
            if (r is null) return Results.NotFound();
            if (r.WhatsAppMode != WhatsAppMode.Coexistence)
                return RestaurantEndpoints.Bad("History sync is only for coexistence numbers.");
            var token = await tokens.ForPhoneNumberAsync(r.WhatsAppPhoneNumberId, ct);
            var steps = await RequestSyncAsync(r, graph, token, clock.GetUtcNow(), ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new ConnectResult(steps.All(s => s.Ok), steps, ToDto(r)));
        });

        // Check this restaurant's own number with the token it will really use.
        one.MapPost("/test", async (int rid, AppDbContext db, MetaGraph graph, WhatsAppTokens tokens, CancellationToken ct) =>
        {
            var r = await db.Restaurants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == rid, ct);
            if (r is null) return Results.NotFound();
            var token = await tokens.ForPhoneNumberAsync(r.WhatsAppPhoneNumberId, ct);
            if (string.IsNullOrWhiteSpace(token))
                return Results.Ok(new SettingsEndpoints.TestResult(false, "No token: connect the number or set the Access token in Settings."));
            var info = await graph.PhoneInfoAsync(r.WhatsAppPhoneNumberId, token, ct);
            if (!info.Ok)
                return Results.Ok(new SettingsEndpoints.TestResult(false, "Meta rejected the request: " + info.Message));
            string? Str(string name) => info.Body is { } j && j.TryGetProperty(name, out var v) ? v.ToString() : null;
            return Results.Ok(new SettingsEndpoints.TestResult(true,
                $"Connected: {Str("verified_name")} ({Str("display_phone_number")}), quality {Str("quality_rating") ?? "-"}, " +
                $"platform {Str("platform_type") ?? "-"}, status {Str("status") ?? "-"}"));
        });
    }

    private static async Task<List<Step>> RequestSyncAsync(Restaurant r, MetaGraph graph, string token, DateTimeOffset now, CancellationToken ct)
    {
        var contacts = await graph.RequestPhoneSyncAsync(r.WhatsAppPhoneNumberId, "smb_app_state_sync", token, ct);
        var history = await graph.RequestPhoneSyncAsync(r.WhatsAppPhoneNumberId, "history", token, ct);
        if (contacts.Ok || history.Ok)
            r.HistorySyncRequestedAt = now;
        return
        [
            new("Contacts from the phone", contacts.Ok, contacts.Ok ? "Requested; they arrive in the next minutes" : contacts.Message),
            new("Chat history from the phone", history.Ok, history.Ok
                ? "Requested; the owner may have to approve it in the WhatsApp Business app. Up to 6 months arrives in the background"
                : history.Message),
        ];
    }

    private static bool TryParseMode(string? value, out WhatsAppMode mode)
    {
        mode = WhatsAppMode.CloudApi;
        if (string.IsNullOrWhiteSpace(value)) return true;
        return Enum.TryParse(value.Trim(), ignoreCase: true, out mode) && Enum.IsDefined(mode);
    }

    private static string RandomPin() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    internal static ConnectionDto ToDto(Restaurant r) => new(
        r.Id, r.WhatsAppPhoneNumberId, r.WhatsAppMode.ToString(), r.WhatsAppBusinessAccountId,
        HasOwnToken: !string.IsNullOrWhiteSpace(r.WhatsAppAccessToken),
        r.WhatsAppConnectedAt, r.HistorySyncRequestedAt, r.HistoryMessagesImported, r.PauseBotOnPhoneReply, r.LastPhoneReplyAt);

    public sealed record SignupConfig(string AppId, string ConfigId, string GraphApiVersion, bool Ready, IReadOnlyList<string> Missing);
    public sealed record ConnectRequest(string? Code, string? WabaId, string? PhoneNumberId, string? Mode, string? Pin = null);
    public sealed record ConnectionUpdate(string? Mode, string? WhatsAppBusinessAccountId, string? AccessToken, bool PauseBotOnPhoneReply);
    public sealed record Step(string Name, bool Ok, string Message);
    public sealed record ConnectResult(bool Ok, IReadOnlyList<Step> Steps, ConnectionDto Connection);
    public sealed record ConnectionDto(
        int RestaurantId, string PhoneNumberId, string Mode, string? WhatsAppBusinessAccountId, bool HasOwnToken,
        DateTimeOffset? ConnectedAt, DateTimeOffset? HistorySyncRequestedAt, int HistoryMessagesImported,
        bool PauseBotOnPhoneReply, DateTimeOffset? LastPhoneReplyAt);
}
