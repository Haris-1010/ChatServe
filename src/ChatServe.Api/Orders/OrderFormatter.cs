using System.Text;
using System.Text.RegularExpressions;
using ChatServe.Api.Data;

namespace ChatServe.Api.Orders;

/// <summary>Fixed messages for the customer and kitchen (no AI, always correct numbers).</summary>
public static partial class OrderFormatter
{
    public static string TotalText(Order o, string currency) => $"{currency} {Money.Format(o.Total)}";

    /// <summary>The summary shown to the customer before they confirm.</summary>
    public static string DraftSummary(Order o, string currency)
    {
        var sb = new StringBuilder();
        sb.AppendLine("🧾 *Order summary*");
        AppendItems(sb, o, currency);
        if (o.Type == OrderType.Delivery)
            sb.AppendLine($"Delivery ({o.DeliveryArea}): {currency} {Money.Format(o.DeliveryCharge)}");
        sb.AppendLine($"*Total: {TotalText(o, currency)}*");
        sb.AppendLine();
        sb.AppendLine($"Name: {o.CustomerName}");
        sb.AppendLine(o.Type switch
        {
            OrderType.Delivery => $"Address: {o.DeliveryAddress}",
            OrderType.Takeaway => "Takeaway (pick up from the restaurant)",
            _ => "Dine-in",
        });
        if (!string.IsNullOrWhiteSpace(o.PaymentMethod)) sb.AppendLine($"Payment: {o.PaymentMethod}");
        if (!string.IsNullOrWhiteSpace(o.Notes)) sb.AppendLine($"Note: {o.Notes}");
        sb.AppendLine();
        sb.Append("Reply *confirm* (or 1) to place the order. Let us know if you want to change anything.");
        return sb.ToString();
    }

    public static string ConfirmedReply(Order o, string currency)
    {
        var next = o.Type switch
        {
            OrderType.Delivery => "We'll let you know as soon as the rider leaves.",
            OrderType.Takeaway => "We'll message you when it's ready.",
            _ => "It will be ready when you arrive.",
        };
        return $"✅ Thank you {o.CustomerName}! Your order *#{o.Id}* is confirmed.\n" +
               $"Total: *{TotalText(o, currency)}*" + (string.IsNullOrWhiteSpace(o.PaymentMethod) ? "" : $" ({o.PaymentMethod})") + "\n" +
               $"Your order has been sent to the kitchen. {next}";
    }

    public const string DraftCancelledReply = "OK, your order has been cancelled. Let us know if you need anything else. 🙂";

    /// <summary>The ticket sent to the staff/kitchen WhatsApp.</summary>
    public static string KitchenTicket(Order o, string currency)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🔔 *NEW ORDER #{o.Id}* ({TypeText(o.Type)})");
        AppendItems(sb, o, currency);
        if (o.Type == OrderType.Delivery)
            sb.AppendLine($"Delivery: {currency} {Money.Format(o.DeliveryCharge)}");
        sb.AppendLine($"*Total: {TotalText(o, currency)}*");
        sb.AppendLine($"Customer: {o.CustomerName}, +{o.Phone}");
        if (o.Type == OrderType.Delivery) sb.AppendLine($"Address: {o.DeliveryAddress} ({o.DeliveryArea})");
        if (!string.IsNullOrWhiteSpace(o.PaymentMethod)) sb.AppendLine($"Payment: {o.PaymentMethod}");
        if (!string.IsNullOrWhiteSpace(o.Notes)) sb.AppendLine($"Note: {o.Notes}");
        sb.AppendLine();
        sb.Append(o.Type == OrderType.Delivery
            ? $"Reply: *prep {o.Id}* / *out {o.Id}* / *done {o.Id}*"
            : $"Reply: *prep {o.Id}* / *done {o.Id}*");
        return sb.ToString().TrimEnd();
    }

    public static string BookingTicket(TableBooking b) =>
        $"📅 *TABLE BOOKING REQUEST #{b.Id}*\n" +
        $"{b.BookingTime:dddd d MMM, h:mm tt}, {b.People} people\n" +
        $"Customer: {b.CustomerName}, +{b.Phone}" +
        (string.IsNullOrWhiteSpace(b.Notes) ? "" : $"\nNote: {b.Notes}") +
        "\nPlease confirm with the customer.";

    public static string HandoverTicket(Customer c, string reason) =>
        $"🙋 *Customer needs staff*\n{c.Name ?? "Customer"}, +{c.Phone}\nReason: {reason}\nThe bot is paused for this customer. Reply from the WhatsApp Business app or the dashboard.";

    public static string StatusText(OrderStatus s) => s switch
    {
        OrderStatus.Confirmed => "confirmed, sent to kitchen",
        OrderStatus.Preparing => "being prepared",
        OrderStatus.OutForDelivery => "out for delivery (rider on the way)",
        OrderStatus.Completed => "completed/delivered",
        OrderStatus.Cancelled => "cancelled",
        _ => "awaiting customer confirmation",
    };

    public static string TypeText(OrderType t) => t switch
    {
        OrderType.Delivery => "Delivery",
        OrderType.Takeaway => "Takeaway",
        _ => "Dine-in",
    };

    private static void AppendItems(StringBuilder sb, Order o, string currency)
    {
        foreach (var i in o.Items)
        {
            sb.Append($"- {i.Quantity} x {i.Name}: {currency} {Money.Format(i.LineTotal)}");
            if (!string.IsNullOrWhiteSpace(i.Note)) sb.Append($" ({i.Note})");
            sb.AppendLine();
        }
    }

    // ---- Recognising customer replies (without AI) ----

    /// <summary>
    /// "confirm", "confirm kar do", "ok confirm" always count. Right after the summary (<paramref name="summaryJustShown"/>)
    /// a plain yes also counts: "1", "haan", "ji done kr do", "ok bhej do". False if it has a negative word,
    /// asks a question or asks for a change ("haan aur ek coke"): then the AI handles it.
    /// </summary>
    public static bool IsConfirmation(string text, bool summaryJustShown)
    {
        var words = Words(text);
        if (words.Length == 0) return false;
        if (summaryJustShown && words is ["1"]) return true;
        if (words.Any(NegativeWords.Contains)) return false;
        // "na karo" means "don't"; but "confirm kar do na" (with "na" at the end) is a yes
        for (var i = 0; i < words.Length - 1; i++)
            if (words[i] == "na" && words[i + 1].StartsWith("kar", StringComparison.Ordinal)) return false;

        if (words.Length <= 5 && words.Any(ConfirmWords.Contains))
            return true;

        return summaryJustShown
               && words.Length <= 6
               && !text.Contains('?')
               && words.Any(YesWords.Contains)
               && !words.Any(w => ChangeWords.Contains(w) || w.Any(char.IsDigit));
    }

    private static readonly HashSet<string> ConfirmWords = ["confirm", "confirmed", "confrm", "conform", "confirmd"];

    private static readonly HashSet<string> YesWords =
    [
        "han", "haan", "haa", "ha", "hn", "hanji", "g", "ji", "jee", "jii", "yes", "yeah", "yep", "yup",
        "ok", "okay", "okk", "okey", "theek", "thik", "theak", "sahi", "done", "perfect", "bilkul", "zaroor",
        "bhej", "bhejo", "bhejden", "bhejdo", "place", "final",
    ];

    /// <summary>A yes with one of these is probably a change ("haan aur ek coke"), not a confirm.</summary>
    private static readonly HashSet<string> ChangeWords =
    [
        "add", "change", "aur", "or", "lekin", "magar", "but", "plus", "remove", "hata", "hatao", "badal", "badlo",
        "instead", "extra", "sath", "saath", "mazeed", "bhi", "sirf", "only", "kam", "zyada", "ziada", "replace",
        "without", "baghair", "minus", "address", "time", "kab", "kitna", "kitni", "kya",
    ];

    /// <summary>"cancel", "cancel kar do", etc.</summary>
    public static bool IsCancellation(string text)
    {
        var words = Words(text);
        return words.Length is > 0 and <= 5 && words.Any(w => w is "cancel" or "cancelled" or "cancle");
    }

    private static readonly HashSet<string> NegativeWords = ["nahi", "nahin", "nai", "not", "no", "mat", "cancel", "dont", "don't"];

    private static string[] Words(string text) =>
        NonWord().Replace(text.ToLowerInvariant(), " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);

    [GeneratedRegex(@"[^\p{L}\p{N}']+")]
    private static partial Regex NonWord();
}
