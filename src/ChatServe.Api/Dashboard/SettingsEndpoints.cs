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

        // Fetch the phone number details from Meta to check that the token + phone number ID are correct.
        api.MapPost("/settings/test/whatsapp", async (IOptionsMonitor<WhatsAppOptions> options, IHttpClientFactory http, CancellationToken ct) =>
        {
            var o = options.CurrentValue;
            if (string.IsNullOrWhiteSpace(o.AccessToken) || string.IsNullOrWhiteSpace(o.PhoneNumberId))
                return Results.Ok(new TestResult(false, "Save the Access token and Phone number ID first."));

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
                    return Results.Ok(new TestResult(false, $"Meta rejected the request{code}: {err}"));
                }

                string? Str(string name) => root.TryGetProperty(name, out var v) ? v.ToString() : null;
                return Results.Ok(new TestResult(true,
                    $"Connected: {Str("verified_name")} ({Str("display_phone_number")}), quality {Str("quality_rating") ?? "-"}"));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return Results.Ok(new TestResult(false, "Could not reach Meta: " + ex.Message));
            }
        });

        // Send Claude a tiny question to check the API key and model.
        api.MapPost("/settings/test/ai", async (IChatCompletionClient ai, IOptionsMonitor<AiOptions> options, CancellationToken ct) =>
        {
            if (!AnthropicClientProvider.HasKey(options.CurrentValue))
                return Results.Ok(new TestResult(false, "Save the Anthropic API key first."));
            try
            {
                var result = await ai.CompleteAsync(
                    "You are a connection test. Reply with exactly: OK",
                    [new ChatTurn(true, "ping")], [], null, ct);
                return Results.Ok(new TestResult(true,
                    $"Claude ({options.CurrentValue.Model}) replied: \"{result.Text}\" (in {result.InputTokens} / out {result.OutputTokens} tokens)"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Ok(new TestResult(false, "Claude error: " + ex.Message));
            }
        });

        // Dashboard home: what is set and what is still missing.
        api.MapGet("/status", async (AppDbContext db, IConfiguration config, IHostEnvironment env,
            IOptionsMonitor<WhatsAppOptions> wa, IOptionsMonitor<AiOptions> ai, IOptionsMonitor<BotOptions> bot,
            HttpRequest request, CancellationToken ct) =>
        {
            var w = wa.CurrentValue;
            var checks = new List<StatusCheck>();

            bool dbOk;
            try { dbOk = await db.Database.CanConnectAsync(ct); } catch { dbOk = false; }
            checks.Add(new("Database", dbOk ? "ok" : "error", dbOk ? "PostgreSQL connected" : "Could not connect to the database"));

            checks.Add(string.IsNullOrWhiteSpace(w.AccessToken)
                ? new("WhatsApp access token", "error", "Add the Access token in Settings > WhatsApp")
                : new("WhatsApp access token", "ok", "Set (temporary tokens expire after 24 hours; click Test)"));

            var restaurantForNumber = string.IsNullOrWhiteSpace(w.PhoneNumberId)
                ? null
                : await db.Restaurants.AsNoTracking().FirstOrDefaultAsync(r => r.WhatsAppPhoneNumberId == w.PhoneNumberId, ct);
            checks.Add(string.IsNullOrWhiteSpace(w.PhoneNumberId)
                ? new("Phone number ID", "error", "Add the Phone number ID in Settings > WhatsApp")
                : restaurantForNumber is null
                    ? new("Phone number ID", "warn", $"{w.PhoneNumberId} is not linked to any restaurant. Enter this ID on the Restaurant page")
                    : new("Phone number ID", "ok", $"{w.PhoneNumberId} → {restaurantForNumber.Name}"));

            checks.Add(string.IsNullOrWhiteSpace(w.VerifyToken)
                ? new("Webhook verify token", "error", "Add the Verify token in Settings > WhatsApp (and the same in Meta)")
                : new("Webhook verify token", "ok", "Set"));

            checks.Add(!w.ValidateSignature
                ? new("Webhook signature", "warn", "Signature check is OFF. Turn it ON before going live")
                : string.IsNullOrWhiteSpace(w.AppSecret)
                    ? new("Webhook signature", "error", "Signature check is ON but there is no App secret. Every webhook will be rejected")
                    : new("Webhook signature", "ok", "App secret set, signature check ON"));

            checks.Add(!bot.CurrentValue.UseAi
                ? new("AI (Claude)", "warn", "AI is OFF: the bot only sends the welcome message")
                : AnthropicClientProvider.HasKey(ai.CurrentValue)
                    ? new("AI (Claude)", "ok", $"Key set, model {ai.CurrentValue.Model}")
                    : new("AI (Claude)", "error", "No Anthropic API key. The bot will only send an \"apology\" reply"));

            if (DashboardSetup.UsingDefaultPassword(config, env))
                checks.Add(new("Dashboard password", "warn", $"Using the default password \"{DashboardSetup.DevDefaultPassword}\". Change it in Settings > Dashboard"));

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
        // Behind an ngrok/Railway proxy, get the real public URL from X-Forwarded-* headers
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
            return "(not set)";
        try
        {
            var b = new Npgsql.NpgsqlConnectionStringBuilder(DatabaseUrl.ToConnectionString(raw));
            return $"{b.Host}:{b.Port} / {b.Database} (user {b.Username})";
        }
        catch
        {
            return "(could not parse)";
        }
    }

    public sealed record SaveSettingsRequest(Dictionary<string, string?> Values);
    public sealed record TestResult(bool Ok, string Message);
    public sealed record StatusCheck(string Name, string State, string Message);
}
