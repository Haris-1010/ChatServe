using ChatServe.Api.WhatsApp.Models;

namespace ChatServe.Api.Messaging;

public static class WebhookParser
{
    /// <summary>
    /// Extracts customer messages from the payload. Status updates (sent/delivered/read) are ignored.
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
                    ReceivedAt: ParseTimestamp(msg.Timestamp),
                    MediaId: msg.Type == "audio" ? msg.Audio?.Id : null,
                    ReplyId: msg.Interactive?.ButtonReply?.Id ?? msg.Interactive?.ListReply?.Id ?? msg.Button?.Payload));
            }
        }

        return result;
    }

    /// <summary>
    /// Coexistence: replies the owner sent from the WhatsApp Business app (field "smb_message_echoes").
    /// <see cref="IncomingMessage.From"/> is set to the customer the owner wrote to.
    /// </summary>
    public static IReadOnlyList<IncomingMessage> ExtractPhoneReplies(WebhookPayload payload, string fallbackPhoneNumberId)
    {
        var result = new List<IncomingMessage>();
        foreach (var (value, phoneNumberId) in Values(payload, "smb_message_echoes", fallbackPhoneNumberId))
        {
            foreach (var msg in value.MessageEchoes)
            {
                if (string.IsNullOrEmpty(msg.Id) || string.IsNullOrEmpty(msg.To))
                    continue;
                result.Add(new IncomingMessage(
                    MessageId: msg.Id,
                    PhoneNumberId: phoneNumberId,
                    From: Digits(msg.To),
                    CustomerName: null,
                    Type: msg.Type ?? "unknown",
                    Text: GetText(msg),
                    ReceivedAt: ParseTimestamp(msg.Timestamp),
                    Kind: MessageKind.PhoneReply));
            }
        }
        return result;
    }

    /// <summary>Coexistence: old chats ("history") and saved contacts ("smb_app_state_sync") from the owner's phone.</summary>
    public static IReadOnlyList<PhoneSyncBatch> ExtractPhoneSync(WebhookPayload payload, string fallbackPhoneNumberId)
    {
        var result = new List<PhoneSyncBatch>();

        foreach (var (value, phoneNumberId) in Values(payload, "history", fallbackPhoneNumberId))
        {
            var messages = new List<HistoryMessage>();
            var errors = new List<string>();
            foreach (var chunk in value.History)
            {
                errors.AddRange(chunk.Errors.Select(e => $"{e.Code}: {e.Title ?? e.Message}"));
                foreach (var thread in chunk.Threads)
                {
                    var customer = Digits(thread.Id);
                    if (customer.Length == 0)
                        continue;
                    foreach (var msg in thread.Messages)
                    {
                        if (string.IsNullOrEmpty(msg.Id))
                            continue;
                        // In a thread, messages "from" the customer are theirs; everything else was sent by the business.
                        var fromCustomer = Digits(msg.From) == customer;
                        messages.Add(new HistoryMessage(customer, fromCustomer, msg.Id, msg.Type ?? "unknown",
                            GetText(msg), ParseTimestamp(msg.Timestamp)));
                    }
                }
            }
            if (messages.Count > 0 || errors.Count > 0)
                result.Add(new PhoneSyncBatch(phoneNumberId, messages, [], errors));
        }

        foreach (var (value, phoneNumberId) in Values(payload, "smb_app_state_sync", fallbackPhoneNumberId))
        {
            var contacts = value.StateSync
                .Where(s => s.Type == "contact" && s.Contact is not null && Digits(s.Contact.PhoneNumber).Length > 0)
                .Select(s => new ContactChange(
                    Digits(s.Contact!.PhoneNumber),
                    (s.Contact.FullName ?? s.Contact.FirstName)?.Trim(),
                    Removed: string.Equals(s.Action, "remove", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (contacts.Count > 0)
                result.Add(new PhoneSyncBatch(phoneNumberId, [], contacts, []));
        }

        return result;
    }

    private static IEnumerable<(WebhookValue Value, string PhoneNumberId)> Values(WebhookPayload payload, string field, string fallbackPhoneNumberId)
    {
        if (payload.Object != "whatsapp_business_account")
            yield break;
        foreach (var change in payload.Entry.SelectMany(e => e.Changes))
        {
            if (change.Field == field && change.Value is not null)
                yield return (change.Value, change.Value.Metadata?.PhoneNumberId ?? fallbackPhoneNumberId);
        }
    }

    /// <summary>"+92 300-1234567" → "923001234567" (wa_id format).</summary>
    internal static string Digits(string? phone) =>
        phone is null ? "" : new string(phone.Where(char.IsAsciiDigit).ToArray());

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
