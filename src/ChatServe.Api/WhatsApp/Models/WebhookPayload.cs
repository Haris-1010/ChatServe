using System.Text.Json.Serialization;

namespace ChatServe.Api.WhatsApp.Models;

// Only the parts of the Meta webhook payload (field = "messages") that we need.
// Docs: https://developers.facebook.com/docs/whatsapp/cloud-api/webhooks/components

public sealed class WebhookPayload
{
    [JsonPropertyName("object")] public string? Object { get; set; }
    [JsonPropertyName("entry")] public List<WebhookEntry> Entry { get; set; } = [];
}

public sealed class WebhookEntry
{
    /// <summary>WhatsApp Business Account (WABA) ID.</summary>
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("changes")] public List<WebhookChange> Changes { get; set; } = [];
}

public sealed class WebhookChange
{
    [JsonPropertyName("field")] public string? Field { get; set; }
    [JsonPropertyName("value")] public WebhookValue? Value { get; set; }
}

public sealed class WebhookValue
{
    [JsonPropertyName("messaging_product")] public string? MessagingProduct { get; set; }
    [JsonPropertyName("metadata")] public WebhookMetadata? Metadata { get; set; }
    [JsonPropertyName("contacts")] public List<WebhookContact> Contacts { get; set; } = [];
    [JsonPropertyName("messages")] public List<WebhookMessage> Messages { get; set; } = [];
    [JsonPropertyName("statuses")] public List<WebhookStatus> Statuses { get; set; } = [];
}

public sealed class WebhookMetadata
{
    [JsonPropertyName("display_phone_number")] public string? DisplayPhoneNumber { get; set; }

    /// <summary>Used to identify the restaurant (multi-tenant).</summary>
    [JsonPropertyName("phone_number_id")] public string? PhoneNumberId { get; set; }
}

public sealed class WebhookContact
{
    [JsonPropertyName("wa_id")] public string? WaId { get; set; }
    [JsonPropertyName("profile")] public WebhookProfile? Profile { get; set; }
}

public sealed class WebhookProfile
{
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public sealed class WebhookMessage
{
    [JsonPropertyName("from")] public string? From { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("timestamp")] public string? Timestamp { get; set; }

    /// <summary>text, image, audio, location, interactive, button, ...</summary>
    [JsonPropertyName("type")] public string? Type { get; set; }

    [JsonPropertyName("text")] public WebhookText? Text { get; set; }
    [JsonPropertyName("interactive")] public WebhookInteractive? Interactive { get; set; }
    [JsonPropertyName("button")] public WebhookButton? Button { get; set; }
    [JsonPropertyName("audio")] public WebhookMedia? Audio { get; set; }
}

/// <summary>A media attachment (voice note, audio). The file is downloaded separately with its id.</summary>
public sealed class WebhookMedia
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("mime_type")] public string? MimeType { get; set; }
    [JsonPropertyName("voice")] public bool? Voice { get; set; }
}

public sealed class WebhookText
{
    [JsonPropertyName("body")] public string? Body { get; set; }
}

public sealed class WebhookInteractive
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("button_reply")] public WebhookReply? ButtonReply { get; set; }
    [JsonPropertyName("list_reply")] public WebhookReply? ListReply { get; set; }
}

public sealed class WebhookReply
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
}

public sealed class WebhookButton
{
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("payload")] public string? Payload { get; set; }
}

public sealed class WebhookStatus
{
    [JsonPropertyName("id")] public string? Id { get; set; }

    /// <summary>sent, delivered, read, failed</summary>
    [JsonPropertyName("status")] public string? Status { get; set; }

    [JsonPropertyName("recipient_id")] public string? RecipientId { get; set; }
    [JsonPropertyName("timestamp")] public string? Timestamp { get; set; }
}
