using ChatServe.Api.Ai;
using ChatServe.Api.Data;
using ChatServe.Api.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Messaging;

/// <summary>
/// Restaurant data (DB) + the customer's chat history → Claude (with tools) → reply.
/// Every message (customer and bot) is saved to the DB: it is the bot's memory and the dashboard's chat log.
/// Order "confirm"/"cancel" is handled here without AI: an order only goes to the kitchen when the customer confirms.
/// </summary>
public sealed class ClaudeReplyGenerator(
    AppDbContext db,
    IChatCompletionClient ai,
    OrderService orders,
    IStaffNotifier staff,
    IOptionsMonitor<AiOptions> aiOptions,
    TimeProvider clock,
    ILogger<ClaudeReplyGenerator> logger,
    StaffCommandHandler? staffCommands = null) : IReplyGenerator
{
    public const string NonTextReply =
        "Sorry, I can only read text messages for now. Please type your question. 🙏";

    public const string FallbackReply =
        "Sorry, we're having trouble replying right now. Our staff will contact you soon. 🙏";

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

        // Meta retries after an app restart: also check the DB for duplicates.
        if (await db.ChatMessages.AnyAsync(m => m.WhatsAppMessageId == message.MessageId, ct))
            return null;

        // A staff number sending "out 12", "orders", etc.: run the command, no AI and no customer record.
        if (staffCommands is not null && await staffCommands.TryHandleAsync(restaurant, message, ct) is { } staffReply)
            return staffReply;

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
            return null; // staff will reply themselves (Phase 5: from the dashboard)

        if (customer.BotPausedUntil > now)
            return null; // human handover: staff are talking to this customer

        string reply;
        AiResult? result = null;
        Order? newDraft = null;
        var staffAlerts = new List<string>();
        var pending = string.IsNullOrWhiteSpace(message.Text) ? null : await orders.GetPendingDraftAsync(customer.Id, now, ct);

        if (string.IsNullOrWhiteSpace(message.Text))
        {
            reply = NonTextReply; // no AI call needed, save money
        }
        else if (pending is not null && OrderFormatter.IsConfirmation(message.Text, await SummaryWasLastBotMessageAsync(customer.Id, pending, ct)))
        {
            // The customer confirmed: the order now goes to the kitchen. Fixed reply, no AI call.
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

            // Safety net: if a draft was created, always show the customer the correct total, even if the AI left out the summary.
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
            CacheWriteTokens = (int?)result?.CacheWriteTokens,
        };
        db.ChatMessages.Add(botMessage);
        await db.SaveChangesAsync(ct);

        if (newDraft is not null)
        {
            newDraft.SummaryChatMessageId = botMessage.Id; // "1" only counts as confirm right after this summary
            await db.SaveChangesAsync(ct);
        }

        foreach (var alert in staffAlerts)
            await staff.NotifyAsync(restaurant, alert, ct);

        return reply;
    }

    public const string HandoverReply =
        "Thank you! A member of our team will reply to you here soon. 🙏";

    /// <summary>"1" only counts as confirm when the customer sends it right after the summary (not as an answer to some other question).</summary>
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

    /// <summary>Tool calls aren't saved in history, so we tell the AI about the pending draft as a note.</summary>
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
    /// The last N messages in Claude's format: the first message must be from the customer,
    /// and consecutive messages with the same role are merged into one.
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
    /// The current Pakistan time, added to the last customer message (not saved to the DB).
    /// Not put in the system prompt so the prompt cache isn't broken.
    /// </summary>
    internal static IReadOnlyList<ChatTurn> WithCurrentTime(IReadOnlyList<ChatTurn> turns, DateTimeOffset utcNow, string? extraNote = null)
    {
        if (turns.Count == 0 || !turns[^1].FromCustomer)
            return turns;

        var pk = utcNow.ToOffset(Money.PakistanOffset); // PKT, no daylight saving
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
                continue; // the conversation must start with the customer

            if (turns.Count > 0 && turns[^1].FromCustomer == fromCustomer)
                turns[^1] = turns[^1] with { Text = turns[^1].Text + "\n" + text };
            else
                turns.Add(new ChatTurn(fromCustomer, text));
        }
        return turns;
    }
}
