using System.Text.Json.Serialization;

namespace ChatServe.Api.WhatsApp.Models;

// Only the parts of the Meta webhook payload that we need. Fields:
//   "messages"            customer messages (both setups)
//   "smb_message_echoes"  coexistence: messages the owner sent from the WhatsApp Business app
//   "history"             coexistence: old chats from the phone (after we ask for them, once)
//   "smb_app_state_sync"  coexistence: contacts saved in the phone
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

    /// <summary>smb_message_echoes: from = the business number, to = the customer.</summary>
    [JsonPropertyName("message_echoes")] public List<WebhookMessage> MessageEchoes { get; set; } = [];

    [JsonPropertyName("history")] public List<WebhookHistoryChunk> History { get; set; } = [];

    [JsonPropertyName("state_sync")] public List<WebhookStateSync> StateSync { get; set; } = [];
}

public sealed class WebhookHistoryChunk
{
    [JsonPropertyName("metadata")] public WebhookHistoryMetadata? Metadata { get; set; }
    [JsonPropertyName("threads")] public List<WebhookHistoryThread> Threads { get; set; } = [];

    /// <summary>Set when the owner declined to share history (then there are no threads).</summary>
    [JsonPropertyName("errors")] public List<WebhookError> Errors { get; set; } = [];
}

public sealed class WebhookHistoryMetadata
{
    [JsonPropertyName("phase")] public int? Phase { get; set; }
    [JsonPropertyName("chunk_order")] public int? ChunkOrder { get; set; }
    [JsonPropertyName("progress")] public int? Progress { get; set; }
}

public sealed class WebhookHistoryThread
{
    /// <summary>The customer's WhatsApp number (wa_id).</summary>
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("messages")] public List<WebhookMessage> Messages { get; set; } = [];
}

public sealed class WebhookStateSync
{
    /// <summary>"contact"</summary>
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("contact")] public WebhookSyncedContact? Contact { get; set; }

    /// <summary>"add" or "remove"</summary>
    [JsonPropertyName("action")] public string? Action { get; set; }
}

public sealed class WebhookSyncedContact
{
    [JsonPropertyName("full_name")] public string? FullName { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
}

public sealed class WebhookError
{
    [JsonPropertyName("code")] public int? Code { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
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

    /// <summary>Only in echoes (and some history messages): who the business wrote to.</summary>
    [JsonPropertyName("to")] public string? To { get; set; }

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
