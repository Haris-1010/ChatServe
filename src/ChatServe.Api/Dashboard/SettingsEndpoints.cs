using System.Text.Json;
using ChatServe.Api.Ai;
using ChatServe.Api.Configuration;
using ChatServe.Api.Data;
using ChatServe.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Dashboard;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/settings", async (SettingsStore store, IConfiguration config, HttpRequest request, CancellationToken ct) =>
        {
            var stored = await store.GetStoredAsync(ct);
            var items = SettingsCatalog.All.Select(def =>
            {
                var effective = config[def.Key];
                var fromDashboard = stored.ContainsKey(def.Key);
                var source = fromDashboard ? "dashboard" : string.IsNullOrEmpty(effective) ? "default" : "config";
                var isSecret = def.Kind == SettingKind.Secret;
                return new
                {
                    def.Key,
                    def.Group,
                    def.Label,
                    Kind = def.Kind.ToString().ToLowerInvariant(),
                    def.Help,
                    def.Options,
                    Value = isSecret ? null : effective ?? "",
                    IsSet = !string.IsNullOrEmpty(effective),
                    Hint = isSecret && !string.IsNullOrEmpty(effective) ? Mask(effective) : null,
                    Source = source,
                };
            });

            return Results.Ok(new
            {
                Settings = items,
                WebhookUrl = WebhookUrl(request),
                Database = DescribeConnection(config),
            });
        });

        api.MapPut("/settings", async (SaveSettingsRequest req, SettingsStore store, CancellationToken ct) =>
        {
            try
            {
                await store.SaveAsync(req.Values, ct);
                return Results.NoContent();
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        });

        // Meta se phone number ki details mangwa kar dekhte hain ke token + phone number ID sahi hain.
        api.MapPost("/settings/test/whatsapp", async (IOptionsMonitor<WhatsAppOptions> options, IHttpClientFactory http, CancellationToken ct) =>
        {
            var o = options.CurrentValue;
            if (string.IsNullOrWhiteSpace(o.AccessToken) || string.IsNullOrWhiteSpace(o.PhoneNumberId))
                return Results.Ok(new TestResult(false, "Pehle Access token aur Phone number ID save karein."));

            var url = $"{o.GraphApiBaseUrl.TrimEnd('/')}/{o.GraphApiVersion}/{o.PhoneNumberId}?fields=display_phone_number,verified_name,quality_rating";
            using var msg = new HttpRequestMessage(HttpMethod.Get, url);
            msg.Headers.Authorization = new("Bearer", o.AccessToken);
            try
            {
                using var res = await http.CreateClient("graph").SendAsync(msg, ct);
                var body = await res.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                var root = doc.RootElement;
                if (!res.IsSuccessStatusCode)
                {
                    var err = root.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m) ? m.GetString() : body;
                    var code = root.TryGetProperty("error", out var e2) && e2.TryGetProperty("code", out var c) ? $" (code {c})" : "";
                    return Results.Ok(new TestResult(false, $"Meta ne inkaar kiya{code}: {err}"));
                }

                string? Str(string name) => root.TryGetProperty(name, out var v) ? v.ToString() : null;
                return Results.Ok(new TestResult(true,
                    $"Connected: {Str("verified_name")} ({Str("display_phone_number")}), quality {Str("quality_rating") ?? "-"}"));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return Results.Ok(new TestResult(false, "Meta tak nahi pohanch sake: " + ex.Message));
            }
        });

        // Claude ko chhota sa sawal bhej kar API key aur model check.
        api.MapPost("/settings/test/ai", async (IChatCompletionClient ai, IOptionsMonitor<AiOptions> options, CancellationToken ct) =>
        {
            if (!AnthropicClientProvider.HasKey(options.CurrentValue))
                return Results.Ok(new TestResult(false, "Pehle Anthropic API key save karein."));
            try
            {
                var result = await ai.CompleteAsync(
                    "You are a connection test. Reply with exactly: OK",
                    [new ChatTurn(true, "ping")], [], null, ct);
                return Results.Ok(new TestResult(true,
                    $"Claude ({options.CurrentValue.Model}) ne jawab diya: \"{result.Text}\" (in {result.InputTokens} / out {result.OutputTokens} tokens)"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Ok(new TestResult(false, "Claude error: " + ex.Message));
            }
        });

        // Dashboard ka home: kya kya set hai aur kya baqi hai.
        api.MapGet("/status", async (AppDbContext db, IConfiguration config, IHostEnvironment env,
            IOptionsMonitor<WhatsAppOptions> wa, IOptionsMonitor<AiOptions> ai, IOptionsMonitor<BotOptions> bot,
            HttpRequest request, CancellationToken ct) =>
        {
            var w = wa.CurrentValue;
            var checks = new List<StatusCheck>();

            bool dbOk;
            try { dbOk = await db.Database.CanConnectAsync(ct); } catch { dbOk = false; }
            checks.Add(new("Database", dbOk ? "ok" : "error", dbOk ? "PostgreSQL connected" : "Database se connect nahi ho saka"));

            checks.Add(string.IsNullOrWhiteSpace(w.AccessToken)
                ? new("WhatsApp access token", "error", "Settings > WhatsApp mein Access token daalein")
                : new("WhatsApp access token", "ok", "Set hai (24 ghante wala token expire hota hai, Test dabayein)"));

            var restaurantForNumber = string.IsNullOrWhiteSpace(w.PhoneNumberId)
                ? null
                : await db.Restaurants.AsNoTracking().FirstOrDefaultAsync(r => r.WhatsAppPhoneNumberId == w.PhoneNumberId, ct);
            checks.Add(string.IsNullOrWhiteSpace(w.PhoneNumberId)
                ? new("Phone number ID", "error", "Settings > WhatsApp mein Phone number ID daalein")
                : restaurantForNumber is null
                    ? new("Phone number ID", "warn", $"{w.PhoneNumberId} kisi restaurant se linked nahi. Restaurant page par yahi ID daalein")
                    : new("Phone number ID", "ok", $"{w.PhoneNumberId} → {restaurantForNumber.Name}"));

            checks.Add(string.IsNullOrWhiteSpace(w.VerifyToken)
                ? new("Webhook verify token", "error", "Settings > WhatsApp mein Verify token daalein (Meta mein bhi yahi)")
                : new("Webhook verify token", "ok", "Set hai"));

            checks.Add(!w.ValidateSignature
                ? new("Webhook signature", "warn", "Signature check OFF hai. Live karne se pehle ON karein")
                : string.IsNullOrWhiteSpace(w.AppSecret)
                    ? new("Webhook signature", "error", "Signature check ON hai magar App secret nahi. Har webhook reject hoga")
                    : new("Webhook signature", "ok", "App secret set, signature check ON"));

            checks.Add(!bot.CurrentValue.UseAi
                ? new("AI (Claude)", "warn", "AI OFF hai: bot sirf welcome message bhejta hai")
                : AnthropicClientProvider.HasKey(ai.CurrentValue)
                    ? new("AI (Claude)", "ok", $"Key set, model {ai.CurrentValue.Model}")
                    : new("AI (Claude)", "error", "Anthropic API key nahi. Bot har dafa \"maazrat\" wala jawab dega"));

            if (DashboardSetup.UsingDefaultPassword(config, env))
                checks.Add(new("Dashboard password", "warn", $"Default password \"{DashboardSetup.DevDefaultPassword}\" chal raha hai. Settings > Dashboard mein badlein"));

            return Results.Ok(new
            {
                Checks = checks,
                WebhookUrl = WebhookUrl(request),
                Environment = env.EnvironmentName,
            });
        });
    }

    internal static string WebhookUrl(HttpRequest request)
    {
        // ngrok/Railway proxy ke peeche X-Forwarded-* headers se asal public URL
        var proto = request.Headers["X-Forwarded-Proto"].FirstOrDefault() ?? request.Scheme;
        var host = request.Headers["X-Forwarded-Host"].FirstOrDefault() ?? request.Host.Value;
        return $"{proto}://{host}/webhook";
    }

    private static string Mask(string value) => value.Length <= 4 ? "••••" : "••••" + value[^4..];

    private static string DescribeConnection(IConfiguration config)
    {
        var raw = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(raw))
            raw = config["DATABASE_URL"];
        if (string.IsNullOrWhiteSpace(raw))
            return "(set nahi)";
        try
        {
            var b = new Npgsql.NpgsqlConnectionStringBuilder(DatabaseUrl.ToConnectionString(raw));
            return $"{b.Host}:{b.Port} / {b.Database} (user {b.Username})";
        }
        catch
        {
            return "(parse nahi ho saka)";
        }
    }

    public sealed record SaveSettingsRequest(Dictionary<string, string?> Values);
    public sealed record TestResult(bool Ok, string Message);
    public sealed record StatusCheck(string Name, string State, string Message);
}
