using ChatServe.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ChatServe.Api.Configuration;

/// <summary>
/// Configuration provider for dashboard (DB) settings. It is added to the builder last,
/// so it takes priority over appsettings.json, user-secrets and env vars.
/// It doesn't read the DB itself: <see cref="SettingsStore"/> passes values on startup and on every save,
/// then the reload token fires and everything using IOptionsMonitor picks up the new value (no restart needed).
/// </summary>
public sealed class DashboardSettingsProvider : ConfigurationProvider, IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    public void Replace(IReadOnlyDictionary<string, string> values)
    {
        Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
            Data[key] = value;
        OnReload();
    }
}

public enum SettingKind { Text, Secret, Bool, Number, Select, TextArea, Decimal }

public sealed record SettingDefinition(
    string Key,
    string Group,
    string Label,
    SettingKind Kind,
    string Help,
    string[]? Options = null,
    bool RequiresRestart = false);

/// <summary>Settings that can be changed from the dashboard. The connection string isn't here (it can't live in the DB it points to).</summary>
public static class SettingsCatalog
{
    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        new("WhatsApp:PhoneNumberId", "WhatsApp", "Phone number ID", SettingKind.Text,
            "Meta > WhatsApp > API Setup > \"Phone number ID\" (the ID, not the phone number). The demo restaurant is created for this ID."),
        new("WhatsApp:AccessToken", "WhatsApp", "Access token", SettingKind.Secret,
            "The temporary token from API Setup (24 hours) or a permanent System User token."),
        new("WhatsApp:VerifyToken", "WhatsApp", "Verify token", SettingKind.Secret,
            "Any long random string you choose. Enter the same value in Meta > WhatsApp > Configuration > Webhook."),
        new("WhatsApp:AppSecret", "WhatsApp", "App secret", SettingKind.Secret,
            "App settings > Basic > App secret. Used to check the webhook signature."),
        new("WhatsApp:ValidateSignature", "WhatsApp", "Webhook signature check", SettingKind.Bool,
            "Always ON in production. Turn OFF only for local testing."),
        new("WhatsApp:GraphApiVersion", "WhatsApp", "Graph API version", SettingKind.Text,
            "For example v23.0. Use the version shown in the curl example on Meta's API Setup page."),

        new("Anthropic:ApiKey", "AI (Claude)", "Anthropic API key", SettingKind.Secret,
            "console.anthropic.com > API Keys. If empty, the ANTHROPIC_API_KEY env var is used."),
        new("Anthropic:Model", "AI (Claude)", "Model", SettingKind.Text,
            "Haiku is cheap and fast (enough for chat). Sonnet/Opus are smarter but cost more.",
            ["claude-haiku-5-5", "claude-sonnet-5-5", "claude-opus-5-5"]),
        new("Anthropic:Effort", "AI (Claude)", "Effort", SettingKind.Select,
            "low = cheaper and faster. Use medium if replies seem weak.", ["low", "medium", "high"]),
        new("Anthropic:MaxTokens", "AI (Claude)", "Max tokens (reply length)", SettingKind.Number,
            "WhatsApp replies should be short. Default 2048."),
        new("Anthropic:HistoryLimit", "AI (Claude)", "Chat memory (messages)", SettingKind.Number,
            "How many past messages per customer the AI remembers. Default 20."),
        new("Anthropic:MaxToolRounds", "AI (Claude)", "Max tool rounds", SettingKind.Number,
            "How many times the AI can use tools for one message. Default 5."),

        new("Voice:OpenAiApiKey", "Voice notes", "OpenAI API key (Whisper)", SettingKind.Secret,
            "platform.openai.com > API keys. With a key, customers' voice notes are turned into text and answered. Empty = they get a \"please type\" reply."),
        new("Voice:Model", "Voice notes", "Speech-to-text model", SettingKind.Text,
            "Default whisper-1."),

        new("Usage:InputUsdPerMillion", "AI cost", "Input price ($ per 1M tokens)", SettingKind.Decimal,
            "From Anthropic's pricing page for the model above. claude-haiku-5-5: 0.10, claude-sonnet-5-5: 2, claude-opus-5-5: 4."),
        new("Usage:OutputUsdPerMillion", "AI cost", "Output price ($ per 1M tokens)", SettingKind.Decimal,
            "claude-haiku-5-5: 0.50, claude-sonnet-5-5: 10, claude-opus-5-5: 20."),
        new("Usage:CacheReadUsdPerMillion", "AI cost", "Cache read price ($ per 1M tokens)", SettingKind.Decimal,
            "Cached menu/instructions re-used between messages. Check the pricing page; default 0.01 (10% of the Haiku input price)."),
        new("Usage:CacheWriteUsdPerMillion", "AI cost", "Cache write price ($ per 1M tokens)", SettingKind.Decimal,
            "Usually 1.25 x the input price. Default 0.125."),
        new("Usage:PkrPerUsd", "AI cost", "Dollar rate (PKR per $)", SettingKind.Decimal,
            "Used to show AI cost in rupees. Default 280."),
        new("Usage:WarnAtPercent", "AI cost", "Warn at (% of monthly limit)", SettingKind.Number,
            "Clients over this share of their monthly AI limit are highlighted. Default 80."),

        new("Bot:UseAi", "Bot", "Reply with AI", SettingKind.Bool,
            "OFF = a simple welcome message (no AI cost). ON = Claude answers from the menu."),
        new("Bot:WelcomeMessage", "Bot", "Welcome message (when AI is OFF)", SettingKind.TextArea,
            "{0} is replaced with the customer's message."),
        new("Bot:DemoStaffWhatsAppNumber", "Bot", "Demo staff/kitchen number", SettingKind.Text,
            "For example 923001234567. A new demo restaurant gets this staff number (you can also change it on the Restaurant page)."),

        new("Orders:DraftExpiryMinutes", "Orders", "Wait for confirmation (minutes)", SettingKind.Number,
            "How long to wait for the customer to reply \"confirm\". Default 180."),
        new("Orders:HandoverPauseHours", "Orders", "Handover pause (hours)", SettingKind.Number,
            "After a handover to staff, the bot stays quiet for this many hours. Default 12."),
        new("Orders:MaxQuantityPerItem", "Orders", "Max quantity per item", SettingKind.Number,
            "Protects against fake or joke orders. Default 30."),
        new("Orders:MaxBookingDaysAhead", "Orders", "Max days ahead for bookings", SettingKind.Number,
            "Default 30."),

        new("Dashboard:AdminPassword", "Dashboard", "Dashboard password", SettingKind.Secret,
            "The login password for this dashboard. After changing it, log in with the new password."),
    ];

    public static SettingDefinition? Find(string key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Saves settings to the DB and reloads the configuration provider.</summary>
public sealed class SettingsStore(AppDbContext db, DashboardSettingsProvider provider, TimeProvider clock)
{
    /// <summary>Load all settings from the DB into the provider (on startup, after migrations).</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var values = await db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        provider.Replace(values);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetStoredAsync(CancellationToken ct = default) =>
        await db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase, ct);

    /// <summary>
    /// values: key → new value (null = remove the dashboard value and fall back to appsettings/env vars).
    /// Only keys in the catalog are accepted.
    /// </summary>
    public async Task SaveAsync(IReadOnlyDictionary<string, string?> values, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        foreach (var (rawKey, value) in values)
        {
            var def = SettingsCatalog.Find(rawKey) ?? throw new ArgumentException($"Unknown setting '{rawKey}'.");
            var existing = await db.AppSettings.FindAsync([def.Key], ct);
            if (value is null)
            {
                if (existing is not null)
                    db.AppSettings.Remove(existing);
                continue;
            }

            var clean = Normalize(def, value);
            if (existing is null)
                db.AppSettings.Add(new AppSetting { Key = def.Key, Value = clean, UpdatedAt = now });
            else
            {
                existing.Value = clean;
                existing.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(ct);
        await LoadAsync(ct);
    }

    private static string Normalize(SettingDefinition def, string value)
    {
        value = def.Kind == SettingKind.TextArea ? value : value.Trim();
        switch (def.Kind)
        {
            case SettingKind.Bool:
                if (!bool.TryParse(value, out var b))
                    throw new ArgumentException($"{def.Label}: must be true or false.");
                return b ? "true" : "false";
            case SettingKind.Number:
                if (!int.TryParse(value, out var n) || n < 0)
                    throw new ArgumentException($"{def.Label}: enter a valid number.");
                return n.ToString();
            case SettingKind.Decimal:
                if (!decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var d) || d < 0)
                    throw new ArgumentException($"{def.Label}: enter a valid number, e.g. 0.10.");
                return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case SettingKind.Select:
                if (def.Options is not null && !def.Options.Contains(value))
                    throw new ArgumentException($"{def.Label}: choose one of: {string.Join(", ", def.Options)}.");
                return value;
            default:
                return value;
        }
    }
}
