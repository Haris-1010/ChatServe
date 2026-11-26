using ChatServe.Api.Data;
using ChatServe.Api.WhatsApp;

namespace ChatServe.Api.Orders;

/// <summary>
/// Buttons for the kitchen/staff WhatsApp: staff tap instead of typing "prep 12". Only the next steps that make sense
/// for the order are offered. Typed commands keep working (the footer reminds them).
/// Ids: "staff:prep:12", "staff:out:12", "staff:done:12", "staff:cancel:12", "staff:show:12", "staff:orders",
/// "staff:bconfirm:5", "staff:bcancel:5".
/// </summary>
public static class StaffButtons
{
    public const string Prefix = "staff:";
    public const string OrdersId = "staff:orders";

    /// <summary>The next steps for this order, or null when it is finished.</summary>
    public static InteractiveMessage? ForOrder(Order o)
    {
        var id = o.Id;
        var delivery = o.Type == OrderType.Delivery;
        ReplyOption Prep() => new($"staff:prep:{id}", $"👨‍🍳 Prep #{id}");
        ReplyOption Out() => new($"staff:out:{id}", $"🛵 Rider gaya #{id}");
        ReplyOption Done() => new($"staff:done:{id}", delivery ? $"✅ Delivered #{id}" : $"✅ Ready #{id}");
        ReplyOption Cancel() => new($"staff:cancel:{id}", $"❌ Cancel #{id}");

        IReadOnlyList<ReplyOption>? options = o.Status switch
        {
            OrderStatus.Confirmed => delivery ? [Prep(), Out(), Done()] : [Prep(), Done(), Cancel()],
            OrderStatus.Preparing => delivery ? [Out(), Done(), Cancel()] : [Done(), Cancel()],
            OrderStatus.OutForDelivery => [Done(), Cancel()],
            _ => null,
        };
        if (options is null)
            return null;

        var typed = delivery ? $"prep {id} / out {id} / done {id}" : $"prep {id} / done {id}";
        return new InteractiveMessage(options, Footer: "Ya likhein: " + typed);
    }

    public static InteractiveMessage ForBooking(TableBooking b) => new(
    [
        new($"staff:bconfirm:{b.Id}", "✅ Confirm booking"),
        new($"staff:bcancel:{b.Id}", "❌ Cancel booking"),
    ], Footer: "Customer ko khud message chala jayega");

    /// <summary>Active orders as a list: tapping one shows it with its buttons.</summary>
    public static InteractiveMessage OrdersList(IReadOnlyList<Order> active, string currency) => new(
        active.Take(InteractiveMessage.MaxRows).Select(o => new ReplyOption(
            $"staff:show:{o.Id}",
            $"#{o.Id} {o.CustomerName}",
            $"{OrderFormatter.TypeText(o.Type)} · {OrderFormatter.TotalText(o, currency)} · {StatusLabel(o.Status)}")).ToList(),
        ListButton: "Orders dekhein",
        Header: "Active orders");

    public static InteractiveMessage Menu => new([new(OrdersId, "📋 Active orders")]);

    public static string StatusLabel(OrderStatus s) => s switch
    {
        OrderStatus.Confirmed => "New",
        OrderStatus.Preparing => "Preparing",
        OrderStatus.OutForDelivery => "Out for delivery",
        OrderStatus.Completed => "Completed",
        OrderStatus.Cancelled => "Cancelled",
        _ => "Awaiting customer confirmation",
    };
}
