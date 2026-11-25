using System.Text;
using System.Text.RegularExpressions;
using ChatServe.Api.Dashboard;
using ChatServe.Api.Data;
using ChatServe.Api.Messaging;
using ChatServe.Api.WhatsApp;
using Microsoft.EntityFrameworkCore;

namespace ChatServe.Api.Orders;

public enum StaffCommandKind { Help, List, SetStatus }

public sealed partial record StaffCommand(StaffCommandKind Kind, int OrderId = 0, OrderStatus Status = OrderStatus.Confirmed)
{
    /// <summary>The words staff can use. Number first or last both work: "out 12", "12 out", "out #12".</summary>
    private static readonly Dictionary<string, OrderStatus> StatusWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["prep"] = OrderStatus.Preparing,
        ["preparing"] = OrderStatus.Preparing,
        ["accept"] = OrderStatus.Preparing,
        ["ok"] = OrderStatus.Preparing,
        ["out"] = OrderStatus.OutForDelivery,
        ["rider"] = OrderStatus.OutForDelivery,
        ["sent"] = OrderStatus.OutForDelivery,
        ["done"] = OrderStatus.Completed,
        ["ready"] = OrderStatus.Completed,
        ["complete"] = OrderStatus.Completed,
        ["completed"] = OrderStatus.Completed,
        ["delivered"] = OrderStatus.Completed,
        ["cancel"] = OrderStatus.Cancelled,
        ["cancelled"] = OrderStatus.Cancelled,
    };

    /// <summary>Null if the text is not a staff command (then it is handled like any other message).</summary>
    public static StaffCommand? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var t = text.Trim().TrimEnd('.', '!').Trim().ToLowerInvariant();

        if (t is "start" or "help" or "commands")
            return new StaffCommand(StaffCommandKind.Help);
        if (t is "orders" or "list" or "active")
            return new StaffCommand(StaffCommandKind.List);

        var m = CommandRegex().Match(t);
        if (!m.Success)
            return null;
        var word = m.Groups["w1"].Success ? m.Groups["w1"].Value : m.Groups["w2"].Value;
        var number = m.Groups["n1"].Success ? m.Groups["n1"].Value : m.Groups["n2"].Value;
        return StatusWords.TryGetValue(word, out var status) && int.TryParse(number, out var id)
            ? new StaffCommand(StaffCommandKind.SetStatus, id, status)
            : null;
    }

    [GeneratedRegex(@"^(?:(?<w1>[a-z]+)\s*#?(?<n1>\d{1,9})|#?(?<n2>\d{1,9})\s+(?<w2>[a-z]+))$")]
    private static partial Regex CommandRegex();
}

/// <summary>
/// Lets staff run the orders from WhatsApp instead of the dashboard: a message from one of the restaurant's
/// staff numbers like "out 12" moves order #12 on, tells the customer, and tells the other staff numbers.
/// Anything that isn't a command is handled normally (so the owner can still test the bot as a customer).
/// </summary>
public sealed class StaffCommandHandler(
    AppDbContext db,
    IWhatsAppClient whatsApp,
    IStaffNotifier staff,
    TimeProvider clock,
    ILogger<StaffCommandHandler> logger)
{
    public const string HelpText =
        "👋 *ChatServe staff commands*\n" +
        "Reply with the order number:\n" +
        "*prep 12*: order #12 is being prepared\n" +
        "*out 12*: rider has left (delivery)\n" +
        "*done 12*: completed (takeaway: ready for pickup)\n" +
        "*cancel 12*: cancel the order\n" +
        "*orders*: list of active orders\n\n" +
        "The customer gets a WhatsApp message automatically at every step.\n" +
        "Send *start* once a day from every staff number so new order tickets keep reaching you.";

    /// <summary>The reply for the staff member, or null if this isn't a staff command.</summary>
    public async Task<string?> TryHandleAsync(Restaurant restaurant, IncomingMessage message, CancellationToken ct)
    {
        if (!StaffNumbers.Contains(restaurant.StaffWhatsAppNumber, message.From))
            return null;
        var command = StaffCommand.Parse(message.Text);
        if (command is null)
            return null;

        logger.LogInformation("Staff command {Kind} #{OrderId} from {From} for {Restaurant}",
            command.Kind, command.OrderId, message.From, restaurant.Name);

        return command.Kind switch
        {
            StaffCommandKind.Help => HelpText,
            StaffCommandKind.List => await ListAsync(restaurant, ct),
            _ => await SetStatusAsync(restaurant, message.From, command.OrderId, command.Status, ct),
        };
    }

    private async Task<string> ListAsync(Restaurant restaurant, CancellationToken ct)
    {
        var active = await db.Orders.AsNoTracking()
            .Where(o => o.RestaurantId == restaurant.Id &&
                        (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Preparing || o.Status == OrderStatus.OutForDelivery))
            .OrderBy(o => o.Id)
            .Take(20)
            .ToListAsync(ct);
        if (active.Count == 0)
            return "No active orders right now. ✅";

        var sb = new StringBuilder("📋 *Active orders*\n");
        foreach (var o in active)
            sb.AppendLine($"#{o.Id} {OrderFormatter.TypeText(o.Type)}, {OrderFormatter.TotalText(o, restaurant.Currency)}, {StatusLabel(o.Status)}, {o.CustomerName}");
        return sb.ToString().TrimEnd();
    }

    private async Task<string> SetStatusAsync(Restaurant restaurant, string from, int orderId, OrderStatus status, CancellationToken ct)
    {
        var order = await db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.RestaurantId == restaurant.Id, ct);
        if (order is null)
            return $"❌ Order #{orderId} not found. Send *orders* to see the active orders.";
        if (order.Status == OrderStatus.PendingConfirmation)
            return $"⏳ The customer hasn't confirmed order #{orderId} yet.";
        if (order.Status == status)
            return $"Order #{orderId} is already: {StatusLabel(status)}.";
        if (order.Status is OrderStatus.Completed or OrderStatus.Cancelled)
            return $"Order #{orderId} is already {StatusLabel(order.Status)}. Use the dashboard to change it.";
        if (status == OrderStatus.OutForDelivery && order.Type != OrderType.Delivery)
            return $"Order #{orderId} is {OrderFormatter.TypeText(order.Type)}, not delivery. Send *done {orderId}* when it's ready.";

        var now = clock.GetUtcNow();
        order.Status = status;
        order.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        var customerText = OperationsEndpoints.OrderStatusMessage(order, restaurant.Currency);
        var error = await OperationsEndpoints.SendStaffMessageAsync(db, whatsApp, restaurant, order.CustomerId, order.Phone, customerText, now, ct);

        // Keep the rest of the team in the loop (like a group).
        await staff.NotifyAsync(restaurant, $"ℹ️ Order #{order.Id} ({order.CustomerName}): {StatusLabel(status)}. Updated by +{from}.", ct, exceptPhone: from);

        return $"✅ Order #{order.Id}: {StatusLabel(status)}." + (error is null
            ? " The customer has been told."
            : $"\n⚠️ The customer could not be messaged: {error}");
    }

    private static string StatusLabel(OrderStatus s) => s switch
    {
        OrderStatus.Confirmed => "New",
        OrderStatus.Preparing => "Preparing",
        OrderStatus.OutForDelivery => "Out for delivery",
        OrderStatus.Completed => "Completed",
        OrderStatus.Cancelled => "Cancelled",
        _ => "Awaiting customer confirmation",
    };
}
