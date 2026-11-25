using ChatServe.Api.Ai;
using ChatServe.Api.Data;
using ChatServe.Api.Orders;
using ChatServe.Api.WhatsApp;
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
    StaffCommandHandler? staffCommands = null,
    IVoiceTranscriber? voice = null,
    IWhatsAppClient? media = null,
    UsageAlerts? usageAlerts = null,
    FeedbackFlow? feedback = null,
    ReplyButtons? buttons = null) : IReplyGenerator
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

        // A voice note: turn it into text first, then it's handled like a typed message.
        var isVoice = message.Type == "audio";
        var text = message.Text;
        if (string.IsNullOrWhiteSpace(text) && isVoice)
            text = await TranscribeAsync(message, ct);

        var now = clock.GetUtcNow();
        var customer = await UpsertCustomerAsync(restaurant.Id, message, now, ct);

        db.ChatMessages.Add(new ChatMessage
        {
            RestaurantId = restaurant.Id,
            CustomerId = customer.Id,
            Role = ChatRole.Customer,
            Text = isVoice && text is not null ? "🎤 " + text : text ?? $"[{message.Type}]",
            WhatsAppMessageId = message.MessageId,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);

        if (!restaurant.IsBotEnabled)
            return null; // staff will reply themselves (Phase 5: from the dashboard)

        if (customer.BotPausedUntil > now)
            return null; // human handover: staff are talking to this customer

        // Fixed messages go out in the customer's language (Roman Urdu or English), like the AI replies.
        var ru = await CustomerText.PrefersRomanUrduAsync(db, customer.Id, text, ct);

        string reply;
        AiResult? result = null;
        Order? newDraft = null;
        InteractiveMessage? interactive = null;
        FeedbackOutcome? fb = null;
        var staffAlerts = new List<string>();
        var pending = string.IsNullOrWhiteSpace(text) ? null : await orders.GetPendingDraftAsync(customer.Id, now, ct);
        var lastBotId = await LastBotMessageIdAsync(customer.Id, ct);

        if (string.IsNullOrWhiteSpace(text))
        {
            reply = CustomerText.CannotRead(ru, isVoice); // no AI call needed, save money
        }
        else if (pending is not null && (message.ReplyId == ConfirmId ||
                 OrderFormatter.IsConfirmation(text, pending.SummaryChatMessageId is not null && lastBotId == pending.SummaryChatMessageId)))
        {
            // The customer confirmed: the order now goes to the kitchen. Fixed reply, no AI call.
            await orders.ConfirmAsync(pending, now, ct);
            reply = CustomerText.Confirmed(pending, restaurant.Currency, ru);
            staffAlerts.Add(OrderFormatter.KitchenTicket(pending, restaurant.Currency));
            logger.LogInformation("Order {OrderId} confirmed for {Restaurant}, total {Total}", pending.Id, restaurant.Name, pending.Total);
        }
        else if (pending is not null && (message.ReplyId == CancelId || OrderFormatter.IsCancellation(text)))
        {
            await orders.CancelDraftAsync(pending, now, ct);
            reply = CustomerText.DraftCancelled(ru);
        }
        else if (feedback is not null &&
                 (fb = await feedback.TryHandleAsync(restaurant, customer, message.ReplyId, text, lastBotId, now, ru, ct)) is { Reply: not null })
        {
            // Delivery received / rating / short thanks: fixed replies, no AI call.
            reply = fb.Reply;
            interactive = fb.Buttons;
            staffAlerts.AddRange(fb.StaffAlerts);
        }
        else
        {
            // A feedback comment was saved (fb.Reply null): the AI still answers it, in case it also asks for something.
            if (fb is not null) staffAlerts.AddRange(fb.StaffAlerts);
            var tools = new OrderToolExecutor(orders, restaurant, customer, now, ru);
            var history = WithCurrentTime(await LoadHistoryAsync(customer.Id, ct), now, PendingNote(pending, restaurant.Currency));
            var failed = false;
            try
            {
                result = await ai.CompleteAsync(SystemPromptBuilder.Build(restaurant), history, RestaurantTools.Build(restaurant), tools, ct);
                logger.LogInformation(
                    "AI reply for {Restaurant}: in={In} out={Out} cacheRead={Cache} cacheWrite={CacheWrite} tools={Tools} refused={Refused}",
                    restaurant.Name, result.InputTokens, result.OutputTokens, result.CacheReadTokens, result.CacheWriteTokens, result.ToolCalls, result.Refused);

                failed = result.Refused || result.Text is null;
                reply = failed
                    ? tools.HandedOver ? CustomerText.Handover(ru) : CustomerText.Fallback(ru)
                    : result.Text!;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "AI call failed for message {MessageId}", message.MessageId);
                failed = true;
                reply = tools.HandedOver ? CustomerText.Handover(ru) : CustomerText.Fallback(ru);
            }

            newDraft = tools.Draft;
            staffAlerts.AddRange(tools.StaffAlerts);

            // Safety net: if a draft was created, always show the customer the correct total, even if the AI left out the summary.
            if (newDraft is not null && !reply.Contains(OrderFormatter.TotalText(newDraft, restaurant.Currency), StringComparison.Ordinal))
            {
                var summary = OrderFormatter.DraftSummary(newDraft, restaurant.Currency, ru);
                reply = failed && !tools.HandedOver ? summary : reply + "\n\n" + summary;
            }

            if (newDraft is not null && !tools.HandedOver)
                interactive = DraftButtons(ru);
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

        if (fb is { AskedForInput: true })
        {
            fb.Order.FeedbackPromptMessageId = botMessage.Id; // a plain "5" or a comment only counts right after this
            await db.SaveChangesAsync(ct);
        }

        if (buttons is not null)
            buttons.Value = interactive;

        foreach (var alert in staffAlerts)
            await staff.NotifyAsync(restaurant, alert, ct);

        if (result is not null && usageAlerts is not null)
        {
            try { await usageAlerts.CheckAsync(restaurant.OrganizationId, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Usage alert check failed"); }
        }

        return reply;
    }

    /// <summary>Downloads and transcribes a voice note. Null (and a "please type" reply) when voice is off or it fails.</summary>
    private async Task<string?> TranscribeAsync(IncomingMessage message, CancellationToken ct)
    {
        if (voice is not { IsConfigured: true } || media is null || message.MediaId is null)
            return null;
        try
        {
            var file = await media.DownloadMediaAsync(message.MediaId, ct);
            var text = await voice.TranscribeAsync(file, ct);
            logger.LogInformation("Voice note {MessageId} transcribed ({Bytes} bytes, {Chars} chars)", message.MessageId, file.Data.Length, text?.Length ?? 0);
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Voice note {MessageId} could not be transcribed", message.MessageId);
            return null;
        }
    }

    public const string HandoverReply =
        "Thank you! A member of our team will reply to you here soon. 🙏";

    public const string ConfirmId = "order:confirm";
    public const string ChangeId = "order:change";
    public const string CancelId = "order:cancel";

    /// <summary>Buttons under an order summary. "Change" goes to the AI, which asks what to change.</summary>
    public static InteractiveMessage DraftButtons(bool ru) => new(
    [
        new(ConfirmId, ru ? "✅ Confirm karein" : "✅ Confirm"),
        new(ChangeId, ru ? "✏️ Tabdeeli" : "✏️ Change"),
        new(CancelId, "❌ Cancel"),
    ]);

    /// <summary>The id of the bot's last message to this customer. "1" only confirms right after the summary; "5" only rates right after the rating question.</summary>
    private Task<long?> LastBotMessageIdAsync(int customerId, CancellationToken ct) =>
        db.ChatMessages
            .Where(m => m.CustomerId == customerId && m.Role == ChatRole.Bot)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Select(m => (long?)m.Id)
            .FirstOrDefaultAsync(ct);

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
