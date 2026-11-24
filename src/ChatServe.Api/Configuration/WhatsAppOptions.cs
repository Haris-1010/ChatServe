using System.ComponentModel.DataAnnotations;

namespace ChatServe.Api.Configuration;

/// <summary>
/// Meta WhatsApp Cloud API settings. Secrets (AccessToken, AppSecret, VerifyToken) ko
/// appsettings.json mein commit na karein: local par user-secrets, server par environment variables.
/// Env var format: WhatsApp__AccessToken, WhatsApp__PhoneNumberId, waghera.
/// </summary>
public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    [Required, Url]
    public string GraphApiBaseUrl { get; set; } = "https://graph.facebook.com";

    [Required]
    public string GraphApiVersion { get; set; } = "v23.0";

    /// <summary>Apni marzi ka random string. Yahi Meta dashboard mein "Verify token" mein daalna hai.</summary>
    [Required]
    public string VerifyToken { get; set; } = string.Empty;

    /// <summary>Temporary (24 ghante) ya System User ka permanent token.</summary>
    [Required]
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Default sender number. Webhook payload mein aane wala phone_number_id is par tarjeeh rakhta hai.</summary>
    [Required]
    public string PhoneNumberId { get; set; } = string.Empty;

    /// <summary>App Settings &gt; Basic &gt; App secret. X-Hub-Signature-256 check karne ke liye.</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>Production mein hamesha true. Local testing mein false kar sakte hain.</summary>
    public bool ValidateSignature { get; set; } = true;
}

public sealed class BotOptions
{
    public const string SectionName = "Bot";

    /// <summary>Phase 1 ka simple reply. {0} ki jagah customer ka message aata hai.</summary>
    public string WelcomeMessage { get; set; } = "Assalam o Alaikum! Aap ka message mil gaya: \"{0}\"";

    /// <summary>Demo restaurant ka kitchen/staff WhatsApp number (923001234567). Confirmed orders yahan jaate hain.</summary>
    public string DemoStaffWhatsAppNumber { get; set; } = string.Empty;
}
