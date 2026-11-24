using ChatServe.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ChatServe.Api.Configuration;

/// <summary>
/// Dashboard (DB) wali settings ka configuration provider. Builder mein sab se aakhir mein add hota hai,
/// is liye appsettings.json, user-secrets aur env vars par tarjeeh rakhta hai.
/// DB khud nahi parhta: startup aur har save par <see cref="SettingsStore"/> values de deta hai,
/// phir reload token chalta hai aur IOptionsMonitor wali har cheez nayi value le leti hai (restart ki zaroorat nahi).
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

public enum SettingKind { Text, Secret, Bool, Number, Select, TextArea }

public sealed record SettingDefinition(
    string Key,
    string Group,
    string Label,
    SettingKind Kind,
    string Help,
    string[]? Options = null,
    bool RequiresRestart = false);

/// <summary>Wo settings jo dashboard se badli ja sakti hain. Connection string yahan nahi (DB ke baghair DB ki setting kahan rakhen).</summary>
public static class SettingsCatalog
{
    public static readonly IReadOnlyList<SettingDefinition> All =
    [
        new("WhatsApp:PhoneNumberId", "WhatsApp", "Phone number ID", SettingKind.Text,
            "Meta > WhatsApp > API Setup > \"Phone number ID\" (number nahi, ID). Demo restaurant isi par banta hai."),
        new("WhatsApp:AccessToken", "WhatsApp", "Access token", SettingKind.Secret,
            "API Setup ka temporary token (24 ghante) ya System User ka permanent token."),
        new("WhatsApp:VerifyToken", "WhatsApp", "Verify token", SettingKind.Secret,
            "Apni marzi ka lamba random string. Yahi Meta > WhatsApp > Configuration > Webhook mein daalna hai."),
        new("WhatsApp:AppSecret", "WhatsApp", "App secret", SettingKind.Secret,
            "App settings > Basic > App secret. Webhook ka signature check isi se hota hai."),
        new("WhatsApp:ValidateSignature", "WhatsApp", "Webhook signature check", SettingKind.Bool,
            "Production mein hamesha ON. Sirf local testing mein OFF karein."),
        new("WhatsApp:GraphApiVersion", "WhatsApp", "Graph API version", SettingKind.Text,
            "Maslan v23.0. Meta ke API Setup wale curl example mein jo version ho."),

        new("Anthropic:ApiKey", "AI (Claude)", "Anthropic API key", SettingKind.Secret,
            "console.anthropic.com > API Keys. Khali ho to ANTHROPIC_API_KEY env var use hota hai."),
        new("Anthropic:Model", "AI (Claude)", "Model", SettingKind.Text,
            "Haiku sasta aur tez hai (chat ke liye kaafi). Sonnet/Opus behtar magar mehenge.",
            ["claude-haiku-5-5", "claude-sonnet-5-5", "claude-opus-5-5"]),
        new("Anthropic:Effort", "AI (Claude)", "Effort", SettingKind.Select,
            "low = sasta aur tez. Jawab kamzor lagen to medium.", ["low", "medium", "high"]),
        new("Anthropic:MaxTokens", "AI (Claude)", "Max tokens (jawab ki lambai)", SettingKind.Number,
            "WhatsApp replies chhote hone chahiye. Default 2048."),
        new("Anthropic:HistoryLimit", "AI (Claude)", "Chat memory (messages)", SettingKind.Number,
            "Har customer ki kitni pichli messages AI ko yaad rahen. Default 20."),
        new("Anthropic:MaxToolRounds", "AI (Claude)", "Max tool rounds", SettingKind.Number,
            "Ek message par AI kitni dafa tools chala sakta hai. Default 5."),

        new("Bot:UseAi", "Bot", "AI se jawab", SettingKind.Bool,
            "OFF = simple welcome message (AI kharcha zero). ON = Claude menu se jawab deta hai."),
        new("Bot:WelcomeMessage", "Bot", "Welcome message (AI OFF ho to)", SettingKind.TextArea,
            "{0} ki jagah customer ka message aata hai."),
        new("Bot:DemoStaffWhatsAppNumber", "Bot", "Demo staff/kitchen number", SettingKind.Text,
            "Maslan 923001234567. Naye demo restaurant ko yahi staff number milta hai (restaurant page par alag se bhi badal sakte hain)."),

        new("Orders:DraftExpiryMinutes", "Orders", "Confirm ka intezar (minute)", SettingKind.Number,
            "Itni der tak customer ke \"confirm\" ka intezar. Default 180."),
        new("Orders:HandoverPauseHours", "Orders", "Handover pause (ghante)", SettingKind.Number,
            "Staff ko handover ke baad bot itne ghante chup rehta hai. Default 12."),
        new("Orders:MaxQuantityPerItem", "Orders", "Ek item ki max quantity", SettingKind.Number,
            "Fake/mazaq wale orders se bachao. Default 30."),
        new("Orders:MaxBookingDaysAhead", "Orders", "Booking kitne din aage tak", SettingKind.Number,
            "Default 30."),

        new("Dashboard:AdminPassword", "Dashboard", "Dashboard password", SettingKind.Secret,
            "Is dashboard ka login password. Badalne ke baad naye password se login karna hoga."),
    ];

    public static SettingDefinition? Find(string key) =>
        All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Settings DB mein save karta hai aur configuration provider ko reload karta hai.</summary>
public sealed class SettingsStore(AppDbContext db, DashboardSettingsProvider provider, TimeProvider clock)
{
    /// <summary>DB se saari settings provider mein daalo (startup par migrations ke baad).</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var values = await db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        provider.Replace(values);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetStoredAsync(CancellationToken ct = default) =>
        await db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase, ct);

    /// <summary>
    /// values: key → nayi value (null = dashboard wali value hata do, wapas appsettings/env var ki value).
    /// Sirf catalog wali keys qubool hoti hain.
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
                    throw new ArgumentException($"{def.Label}: true ya false hona chahiye.");
                return b ? "true" : "false";
            case SettingKind.Number:
                if (!int.TryParse(value, out var n) || n < 0)
                    throw new ArgumentException($"{def.Label}: sahi number likhein.");
                return n.ToString();
            case SettingKind.Select:
                if (def.Options is not null && !def.Options.Contains(value))
                    throw new ArgumentException($"{def.Label}: in mein se chunein: {string.Join(", ", def.Options)}.");
                return value;
            default:
                return value;
        }
    }
}
