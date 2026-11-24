using ChatServe.Api.WhatsApp.Models;

namespace ChatServe.Api.Messaging;

public static class WebhookParser
{
    /// <summary>
    /// Payload se customer messages nikalta hai. Status updates (sent/delivered/read) ignore hote hain.
    /// </summary>
    public static IReadOnlyList<IncomingMessage> ExtractMessages(WebhookPayload payload, string fallbackPhoneNumberId)
    {
        var result = new List<IncomingMessage>();
        if (payload.Object != "whatsapp_business_account")
            return result;

        foreach (var change in payload.Entry.SelectMany(e => e.Changes))
        {
            if (change.Field != "messages" || change.Value is null)
                continue;

            var value = change.Value;
            var phoneNumberId = value.Metadata?.PhoneNumberId ?? fallbackPhoneNumberId;

            foreach (var msg in value.Messages)
            {
                if (string.IsNullOrEmpty(msg.Id) || string.IsNullOrEmpty(msg.From))
                    continue;

                var name = value.Contacts.FirstOrDefault(c => c.WaId == msg.From)?.Profile?.Name
                           ?? value.Contacts.FirstOrDefault()?.Profile?.Name;

                result.Add(new IncomingMessage(
                    MessageId: msg.Id,
                    PhoneNumberId: phoneNumberId,
                    From: msg.From,
                    CustomerName: name,
                    Type: msg.Type ?? "unknown",
                    Text: GetText(msg),
                    ReceivedAt: ParseTimestamp(msg.Timestamp)));
            }
        }

        return result;
    }

    private static string? GetText(WebhookMessage msg) => msg.Type switch
    {
        "text" => msg.Text?.Body,
        "interactive" => msg.Interactive?.ButtonReply?.Title ?? msg.Interactive?.ListReply?.Title,
        "button" => msg.Button?.Text,
        _ => null
    };

    private static DateTimeOffset ParseTimestamp(string? unixSeconds) =>
        long.TryParse(unixSeconds, out var s) ? DateTimeOffset.FromUnixTimeSeconds(s) : DateTimeOffset.UtcNow;
}
