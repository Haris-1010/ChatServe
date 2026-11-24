using System.ComponentModel.DataAnnotations;

namespace ChatServe.Api.Configuration;

/// <summary>
/// Meta WhatsApp Cloud API settings. Don't commit secrets (AccessToken, AppSecret, VerifyToken)
/// to appsettings.json: use user-secrets locally and environment variables on the server.
/// Env var format: WhatsApp__AccessToken, WhatsApp__PhoneNumberId, etc.
/// They can also be set from the dashboard's Settings page (saved in the DB, no restart needed).
/// The app runs even with empty tokens, so they can be set from the dashboard the first time.
/// </summary>
public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    [Required, Url]
    public string GraphApiBaseUrl { get; set; } = "https://graph.facebook.com";

    [Required]
    public string GraphApiVersion { get; set; } = "v23.0";

    /// <summary>Any random string you choose. Enter the same value as the "Verify token" in the Meta dashboard.</summary>
    public string VerifyToken { get; set; } = string.Empty;

    /// <summary>A temporary (24-hour) token or a permanent System User token.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Default sender number. The phone_number_id in the webhook payload takes priority over it.</summary>
    public string PhoneNumberId { get; set; } = string.Empty;

    /// <summary>App Settings &gt; Basic &gt; App secret. Used to check X-Hub-Signature-256.</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>Always true in production. Can be false for local testing.</summary>
    public bool ValidateSignature { get; set; } = true;
}

public sealed class BotOptions
{
    public const string SectionName = "Bot";

    /// <summary>The simple Phase 1 reply. {0} is replaced with the customer's message.</summary>
    public string WelcomeMessage { get; set; } = "Hello! We received your message: \"{0}\"";

    /// <summary>Kitchen/staff WhatsApp number for the demo restaurant (923001234567). Confirmed orders are sent here.</summary>
    public string DemoStaffWhatsAppNumber { get; set; } = string.Empty;

    /// <summary>false = the simple Phase 1 welcome reply (no AI cost).</summary>
    public bool UseAi { get; set; } = true;
}
