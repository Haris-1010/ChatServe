using ChatServe.Api.Ai;
using ChatServe.Api.Data;
using ChatServe.Api.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Messaging;

/// <summary>
/// Restaurant ka data (DB) + customer ki chat history → Claude (tools ke saath) → jawab.
/// Har message (customer aur bot) DB mein save hota hai: memory bhi, aur baad mein dashboard ke liye log bhi.
/// Order ka "confirm"/"cancel" AI ke baghair yahin handle hota hai: kitchen tak sirf customer ke confirm par jata hai.
/// </summary>
public sealed class ClaudeReplyGenerator(
    AppDbContext db,
    IChatCompletionClient ai,
    OrderService orders,
    IStaffNotifier staff,
    IOptionsMonitor<AiOptions> aiOptions,
    TimeProvider clock,
    ILogger<ClaudeReplyGenerator> logger) : IReplyGenerator
{
    public const string NonTextReply =
        "Maazrat, main abhi sirf text messages samajh sakta hoon. Meharbani kar ke apna sawal likh kar bhejein. 🙏";

    public const string FallbackReply =
        "Maazrat, is waqt jawab dene mein masla aa raha hai. Hamara staff jald aap se rabta karega. 🙏";

    public async Task<string?> GenerateReplyAsync(IncomingMessage message, CancellationToken ct)
    {
        var restaurant = await db.Restaurants
            .AsNoTracking()
            .AsSplitQuery()
            .Include(r => r.MenuItems)
            .Include(r => r.Deals)
            .Include(r => r.Faqs)
            .Include(r => r.DeliveryZones)
            .FirstOrDefaultAsync(r => r.WhatsAppPhoneNumberId == message.PhoneNumberId, ct);

        if (restaurant is null)
        {
            logger.LogWarning("No restaurant configured for phone_number_id {PhoneNumberId}", message.PhoneNumberId);
            return null;
        }

        // App restart ke baad Meta ka retry: DB se bhi duplicate check.
        if (await db.ChatMessages.AnyAsync(m => m.WhatsAppMessageId == message.MessageId, ct))
            return null;

        var now = clock.GetUtcNow();
        var customer = await UpsertCustomerAsync(restaurant.Id, message, now, ct);

        db.ChatMessages.Add(new ChatMessage
        {
            RestaurantId = restaurant.Id,
            CustomerId = customer.Id,
            Role = ChatRole.Customer,
            Text = message.Text ?? $"[{message.Type}]",
            WhatsAppMessageId = message.MessageId,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);

        if (!restaurant.IsBotEnabled)
            return null; // staff khud jawab dega (Phase 5: dashboard se)

        if (customer.BotPausedUntil > now)
            return null; // human handover: staff is customer se baat kar raha hai

        string reply;
        AiResult? result = null;
        Order? newDraft = null;
        var staffAlerts = new List<string>();
        var pending = string.IsNullOrWhiteSpace(message.Text) ? null : await orders.GetPendingDraftAsync(customer.Id, now, ct);

        if (string.IsNullOrWhiteSpace(message.Text))
        {
            reply = NonTextReply; // AI call ki zaroorat nahi, paisa bachao
        }
        else if (pending is not null && OrderFormatter.IsConfirmation(message.Text, await SummaryWasLastBotMessageAsync(customer.Id, pending, ct)))
        {
            // Customer ne khud confirm kiya: ab order kitchen jata hai. Fixed jawab, AI call nahi.
            await orders.ConfirmAsync(pending, now, ct);
            reply = OrderFormatter.ConfirmedReply(pending, restaurant.Currency);
            staffAlerts.Add(OrderFormatter.KitchenTicket(pending, restaurant.Currency));
            logger.LogInformation("Order {OrderId} confirmed for {Restaurant}, total {Total}", pending.Id, restaurant.Name, pending.Total);
        }
        else if (pending is not null && OrderFormatter.IsCancellation(message.Text))
        {
            await orders.CancelDraftAsync(pending, now, ct);
            reply = OrderFormatter.DraftCancelledReply;
        }
        else
        {
            var tools = new OrderToolExecutor(orders, restaurant, customer, now);
            var history = WithCurrentTime(await LoadHistoryAsync(customer.Id, ct), now, PendingNote(pending, restaurant.Currency));
            try
            {
                result = await ai.CompleteAsync(SystemPromptBuilder.Build(restaurant), history, RestaurantTools.Build(restaurant), tools, ct);
                logger.LogInformation(
                    "AI reply for {Restaurant}: in={In} out={Out} cacheRead={Cache} tools={Tools} refused={Refused}",
                    restaurant.Name, result.InputTokens, result.OutputTokens, result.CacheReadTokens, result.ToolCalls, result.Refused);

                reply = result.Refused || result.Text is null
                    ? tools.HandedOver ? HandoverReply : FallbackReply
                    : result.Text;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "AI call failed for message {MessageId}", message.MessageId);
                reply = tools.HandedOver ? HandoverReply : FallbackReply;
            }

            newDraft = tools.Draft;
            staffAlerts.AddRange(tools.StaffAlerts);

            // Hifazat: draft bana hai to customer ko sahi total zaroor dikhe, chahe AI ne summary chhor di ho.
            if (newDraft is not null && !reply.Contains(OrderFormatter.TotalText(newDraft, restaurant.Currency), StringComparison.Ordinal))
            {
                var summary = OrderFormatter.DraftSummary(newDraft, restaurant.Currency);
                reply = reply == FallbackReply ? summary : reply + "\n\n" + summary;
            }
        }

        var botMessage = new ChatMessage
        {
            RestaurantId = restaurant.Id,
            CustomerId = customer.Id,
            Role = ChatRole.Bot,
            Text = reply,
            CreatedAt = clock.GetUtcNow(),
            InputTokens = (int?)result?.InputTokens,
            OutputTokens = (int?)result?.OutputTokens,
            CacheReadTokens = (int?)result?.CacheReadTokens,
        };
        db.ChatMessages.Add(botMessage);
        await db.SaveChangesAsync(ct);

        if (newDraft is not null)
        {
            newDraft.SummaryChatMessageId = botMessage.Id; // "1" sirf isi summary ke foran baad confirm maana jayega
            await db.SaveChangesAsync(ct);
        }

        foreach (var alert in staffAlerts)
            await staff.NotifyAsync(restaurant, alert, ct);

        return reply;
    }

    public const string HandoverReply =
        "Shukriya! Hamari team ka koi member jald aap se isi chat par rabta karega. 🙏";

    /// <summary>"1" ko confirm tabhi maante hain jab customer ne summary ke foran baad likha ho (kisi aur sawal ka jawab na ho).</summary>
    private async Task<bool> SummaryWasLastBotMessageAsync(int customerId, Order pending, CancellationToken ct)
    {
        if (pending.SummaryChatMessageId is null)
            return false;
        var lastBotId = await db.ChatMessages
            .Where(m => m.CustomerId == customerId && m.Role == ChatRole.Bot)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Select(m => (long?)m.Id)
            .FirstOrDefaultAsync(ct);
        return lastBotId == pending.SummaryChatMessageId;
    }

    /// <summary>Tool calls history mein save nahi hote, is liye pending draft AI ko note ki shakal mein batate hain.</summary>
    internal static string? PendingNote(Order? pending, string currency) => pending is null
        ? null
        : "[System note: this customer has an UNCONFIRMED order draft:\n" + OrderFormatter.DraftSummary(pending, currency) +
          "\nIt goes to the kitchen only when the customer replies \"confirm\". If they want changes, call place_order again with the full updated order.]";

    private async Task<Customer> UpsertCustomerAsync(int restaurantId, IncomingMessage message, DateTimeOffset now, CancellationToken ct)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.RestaurantId == restaurantId && c.Phone == message.From, ct);
        if (customer is null)
        {
            customer = new Customer { RestaurantId = restaurantId, Phone = message.From, FirstSeenAt = now };
            db.Customers.Add(customer);
        }

        customer.Name = message.CustomerName ?? customer.Name;
        customer.LastMessageAt = now;
        await db.SaveChangesAsync(ct);
        return customer;
    }

    /// <summary>
    /// Aakhri N messages, Claude ke format mein: pehla message customer ka ho,
    /// aur lagataar same role wale messages ek mein jod diye jayen.
    /// </summary>
    private async Task<IReadOnlyList<ChatTurn>> LoadHistoryAsync(int customerId, CancellationToken ct)
    {
        var limit = Math.Max(1, aiOptions.CurrentValue.HistoryLimit);
        var recent = await db.ChatMessages
            .AsNoTracking()
            .Where(m => m.CustomerId == customerId)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Take(limit)
            .Select(m => new { m.Role, m.Text })
            .ToListAsync(ct);
        recent.Reverse();

        return BuildTurns(recent.Select(m => (m.Role, m.Text)));
    }

    /// <summary>
    /// Pakistan ka mojooda waqt aakhri customer message ke saath (DB mein save nahi hota).
    /// System prompt mein nahi daala taake prompt cache na toote.
    /// </summary>
    internal static IReadOnlyList<ChatTurn> WithCurrentTime(IReadOnlyList<ChatTurn> turns, DateTimeOffset utcNow, string? extraNote = null)
    {
        if (turns.Count == 0 || !turns[^1].FromCustomer)
            return turns;

        var pk = utcNow.ToOffset(Money.PakistanOffset); // PKT, daylight saving nahi
        var note = $"\n\n[Current time in Pakistan: {pk:dddd d MMMM yyyy, h:mm tt}]";
        if (extraNote is not null)
            note += "\n" + extraNote;
        var copy = turns.ToList();
        copy[^1] = copy[^1] with { Text = copy[^1].Text + note };
        return copy;
    }

    internal static IReadOnlyList<ChatTurn> BuildTurns(IEnumerable<(ChatRole Role, string Text)> messages)
    {
        var turns = new List<ChatTurn>();
        foreach (var (role, text) in messages)
        {
            var fromCustomer = role == ChatRole.Customer;
            if (turns.Count == 0 && !fromCustomer)
                continue; // conversation customer se shuru honi chahiye

            if (turns.Count > 0 && turns[^1].FromCustomer == fromCustomer)
                turns[^1] = turns[^1] with { Text = turns[^1].Text + "\n" + text };
            else
                turns.Add(new ChatTurn(fromCustomer, text));
        }
        return turns;
    }
}
