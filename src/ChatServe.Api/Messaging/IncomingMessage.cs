namespace ChatServe.Api.Messaging;

/// <summary>A customer message parsed from the webhook, put on the queue.</summary>
public sealed record IncomingMessage(
    string MessageId,
    string PhoneNumberId,   // the restaurant's number (multi-tenant key)
    string From,            // the customer's WhatsApp number (wa_id)
    string? CustomerName,
    string Type,
    string? Text,           // text, or the text of a button/list reply; null for other types
    DateTimeOffset ReceivedAt,
    string? MediaId = null); // voice note / audio: downloaded and transcribed
