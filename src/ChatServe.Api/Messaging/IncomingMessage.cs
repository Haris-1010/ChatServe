namespace ChatServe.Api.Messaging;

public enum MessageKind
{
    /// <summary>A customer wrote to the restaurant: the bot may reply.</summary>
    Customer = 0,
    /// <summary>Coexistence: the owner replied from the WhatsApp Business app. Saved to the chat; the bot pauses. <see cref="IncomingMessage.From"/> is the customer.</summary>
    PhoneReply = 1,
}

/// <summary>A message parsed from the webhook, put on the queue.</summary>
public sealed record IncomingMessage(
    string MessageId,
    string PhoneNumberId,   // the restaurant's number (multi-tenant key)
    string From,            // the customer's WhatsApp number (wa_id)
    string? CustomerName,
    string Type,
    string? Text,           // text, or the text of a button/list reply; null for other types
    DateTimeOffset ReceivedAt,
    string? MediaId = null, // voice note / audio: downloaded and transcribed
    string? ReplyId = null, // the id of the button or list row the customer tapped (e.g. "rate:12:5")
    MessageKind Kind = MessageKind.Customer);
