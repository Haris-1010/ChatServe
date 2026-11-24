namespace ChatServe.Api.Messaging;

/// <summary>Webhook se nikala hua ek customer message, jo queue mein jata hai.</summary>
public sealed record IncomingMessage(
    string MessageId,
    string PhoneNumberId,   // restaurant ka number (multi-tenant key)
    string From,            // customer ka WhatsApp number (wa_id)
    string? CustomerName,
    string Type,
    string? Text,           // text, button ya list reply ka text; baaki types mein null
    DateTimeOffset ReceivedAt);
