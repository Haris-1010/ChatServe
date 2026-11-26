using ChatServe.Api.Data;
using ChatServe.Api.Orders;
using ChatServe.Api.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Messaging;

/// <summary>
/// Coexistence (the number is on the WhatsApp Business app AND the API):
/// - a reply the owner sends from the phone is saved in Chats and pauses the bot for that customer;
/// - old chats and saved contacts from the phone are imported once, so the bot and the dashboard know them.
/// Nothing here sends a WhatsApp message.
/// </summary>
public sealed class PhoneAppSync(
    AppDbContext db,
    SentMessages sent,
    IOptionsMonitor<OrderOptions> orderOptions,
    TimeProvider clock,
    ILogger<PhoneAppSync> logger)
{
    /// <summary>Shown in front of messages the owner sent from the phone, so Chats shows where they came from.</summary>
    public const string PhonePrefix = "📱 ";

    public async Task SavePhoneReplyAsync(IncomingMessage message, CancellationToken ct)
    {
        if (sent.WasSentByUs(message.MessageId))
            return;

        var restaurant = await db.Restaurants.FirstOrDefaultAsync(r => r.WhatsAppPhoneNumberId == message.PhoneNumberId, ct);
        if (restaurant is null)
        {
            logger.LogWarning("Phone reply for unknown phone_number_id {PhoneNumberId}", message.PhoneNumberId);
            return;
        }

        // The owner chatting with their own kitchen/rider from the phone is not a customer chat.
        if (message.From.Length == 0 || StaffNumbers.Contains(restaurant.StaffWhatsAppNumber, message.From))
            return;

        if (await db.ChatMessages.AnyAsync(m => m.WhatsAppMessageId == message.MessageId, ct))
            return;

        var now = clock.GetUtcNow();
        var sentAt = message.ReceivedAt < now ? message.ReceivedAt : now;
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.RestaurantId == restaurant.Id && c.Phone == message.From, ct);
        if (customer is null)
        {
            customer = new Customer
            {
                RestaurantId = restaurant.Id,
                Phone = message.From,
                Name = await ContactNameAsync(restaurant.Id, message.From, ct),
                FirstSeenAt = sentAt,
                LastMessageAt = sentAt,
            };
            db.Customers.Add(customer);
            await db.SaveChangesAsync(ct);
        }
        if (sentAt > customer.LastMessageAt)
            customer.LastMessageAt = sentAt;

        db.ChatMessages.Add(new ChatMessage
        {
            RestaurantId = restaurant.Id,
            CustomerId = customer.Id,
            Role = ChatRole.Staff,
            Text = PhonePrefix + (string.IsNullOrWhiteSpace(message.Text) ? $"[{message.Type}]" : message.Text),
            WhatsAppMessageId = message.MessageId,
            CreatedAt = sentAt,
        });

        if (restaurant.PauseBotOnPhoneReply)
        {
            var until = now.AddHours(orderOptions.CurrentValue.HandoverPauseHours);
            if (customer.BotPausedUntil is null || customer.BotPausedUntil < until)
                customer.BotPausedUntil = until;
        }
        restaurant.LastPhoneReplyAt = now;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Owner replied from the phone to {Customer} ({Restaurant}); bot paused: {Paused}",
            WhatsAppClient.Mask(message.From), restaurant.Name, restaurant.PauseBotOnPhoneReply);
    }

    public async Task ImportAsync(PhoneSyncBatch batch, CancellationToken ct)
    {
        var restaurant = await db.Restaurants.FirstOrDefaultAsync(r => r.WhatsAppPhoneNumberId == batch.PhoneNumberId, ct);
        if (restaurant is null)
        {
            logger.LogWarning("Phone sync for unknown phone_number_id {PhoneNumberId}", batch.PhoneNumberId);
            return;
        }

        foreach (var error in batch.Errors)
            logger.LogWarning("History sync for {Restaurant} returned an error: {Error}", restaurant.Name, error);

        if (batch.Contacts.Count > 0)
            await ImportContactsAsync(restaurant, batch.Contacts, ct);

        if (batch.Messages.Count > 0)
        {
            var added = await ImportMessagesAsync(restaurant, batch.Messages, ct);
            restaurant.HistoryMessagesImported += added;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Imported {Added} of {Count} old messages for {Restaurant}", added, batch.Messages.Count, restaurant.Name);
        }
    }

    private async Task ImportContactsAsync(Restaurant restaurant, IReadOnlyList<ContactChange> changes, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var phones = changes.Select(c => c.Phone).Distinct().ToList();
        var existing = await db.PhoneContacts
            .Where(c => c.RestaurantId == restaurant.Id && phones.Contains(c.Phone))
            .ToDictionaryAsync(c => c.Phone, ct);

        foreach (var change in changes)
        {
            existing.TryGetValue(change.Phone, out var contact);
            if (change.Removed || string.IsNullOrWhiteSpace(change.Name))
            {
                if (contact is not null)
                {
                    db.PhoneContacts.Remove(contact);
                    existing.Remove(change.Phone);
                }
                continue;
            }

            var name = Cut(change.Name, 100);
            if (contact is null)
            {
                contact = new PhoneContact { RestaurantId = restaurant.Id, Phone = change.Phone, Name = name, UpdatedAt = now };
                db.PhoneContacts.Add(contact);
                existing[change.Phone] = contact;
            }
            else
            {
                contact.Name = name;
                contact.UpdatedAt = now;
            }
        }

        // Customers who have no name yet get the name the owner saved.
        var named = existing.Values.ToDictionary(c => c.Phone, c => c.Name);
        var customers = await db.Customers
            .Where(c => c.RestaurantId == restaurant.Id && c.Name == null && phones.Contains(c.Phone))
            .ToListAsync(ct);
        foreach (var c in customers)
            if (named.TryGetValue(c.Phone, out var n))
                c.Name = n;

        await db.SaveChangesAsync(ct);
    }

    private async Task<int> ImportMessagesAsync(Restaurant restaurant, IReadOnlyList<HistoryMessage> messages, CancellationToken ct)
    {
        // Skip what we already have (Meta can resend a chunk) and the owner's chats with staff.
        var ids = messages.Select(m => m.MessageId).Distinct().ToList();
        var known = new HashSet<string>();
        foreach (var part in ids.Chunk(500))
        {
            var found = await db.ChatMessages
                .Where(m => m.RestaurantId == restaurant.Id && m.WhatsAppMessageId != null && part.Contains(m.WhatsAppMessageId))
                .Select(m => m.WhatsAppMessageId!)
                .ToListAsync(ct);
            known.UnionWith(found);
        }

        var fresh = messages
            .Where(m => known.Add(m.MessageId) && !StaffNumbers.Contains(restaurant.StaffWhatsAppNumber, m.CustomerPhone))
            .ToList();
        if (fresh.Count == 0)
            return 0;

        var phones = fresh.Select(m => m.CustomerPhone).Distinct().ToList();
        var customers = await db.Customers
            .Where(c => c.RestaurantId == restaurant.Id && phones.Contains(c.Phone))
            .ToDictionaryAsync(c => c.Phone, ct);
        var names = await db.PhoneContacts.AsNoTracking()
            .Where(c => c.RestaurantId == restaurant.Id && phones.Contains(c.Phone))
            .ToDictionaryAsync(c => c.Phone, c => c.Name, ct);

        foreach (var group in fresh.GroupBy(m => m.CustomerPhone))
        {
            var first = group.Min(m => m.SentAt);
            var last = group.Max(m => m.SentAt);
            if (!customers.TryGetValue(group.Key, out var customer))
            {
                customer = new Customer
                {
                    RestaurantId = restaurant.Id,
                    Phone = group.Key,
                    Name = names.GetValueOrDefault(group.Key),
                    FirstSeenAt = first,
                    LastMessageAt = last,
                };
                db.Customers.Add(customer);
                customers[group.Key] = customer;
            }
            else
            {
                if (first < customer.FirstSeenAt) customer.FirstSeenAt = first;
                if (last > customer.LastMessageAt) customer.LastMessageAt = last;
            }
        }
        await db.SaveChangesAsync(ct); // new customers get their ids

        foreach (var m in fresh)
        {
            db.ChatMessages.Add(new ChatMessage
            {
                RestaurantId = restaurant.Id,
                CustomerId = customers[m.CustomerPhone].Id,
                Role = m.FromCustomer ? ChatRole.Customer : ChatRole.Staff,
                Text = string.IsNullOrWhiteSpace(m.Text) ? $"[{m.Type}]" : m.Text,
                WhatsAppMessageId = m.MessageId,
                CreatedAt = m.SentAt,
            });
        }
        await db.SaveChangesAsync(ct);
        return fresh.Count;
    }

    private Task<string?> ContactNameAsync(int restaurantId, string phone, CancellationToken ct) =>
        db.PhoneContacts.AsNoTracking()
            .Where(c => c.RestaurantId == restaurantId && c.Phone == phone)
            .Select(c => (string?)c.Name)
            .FirstOrDefaultAsync(ct);

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];
}
