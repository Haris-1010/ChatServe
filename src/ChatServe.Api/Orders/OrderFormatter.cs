using System.Text;
using System.Text.RegularExpressions;
using ChatServe.Api.Data;

namespace ChatServe.Api.Orders;

/// <summary>Customer aur kitchen ko jaane wale fixed messages (AI ke baghair, hamesha sahi numbers).</summary>
public static partial class OrderFormatter
{
    public static string TotalText(Order o, string currency) => $"{currency} {Money.Format(o.Total)}";

    /// <summary>Confirm se pehle customer ko dikhane wali summary.</summary>
    public static string DraftSummary(Order o, string currency)
    {
        var sb = new StringBuilder();
        sb.AppendLine("🧾 *Order summary*");
        AppendItems(sb, o, currency);
        if (o.Type == OrderType.Delivery)
            sb.AppendLine($"Delivery ({o.DeliveryArea}): {currency} {Money.Format(o.DeliveryCharge)}");
        sb.AppendLine($"*Total: {TotalText(o, currency)}*");
        sb.AppendLine();
        sb.AppendLine($"Naam: {o.CustomerName}");
        sb.AppendLine(o.Type switch
        {
            OrderType.Delivery => $"Address: {o.DeliveryAddress}",
            OrderType.Takeaway => "Takeaway (restaurant se pick up)",
            _ => "Dine-in",
        });
        if (!string.IsNullOrWhiteSpace(o.PaymentMethod)) sb.AppendLine($"Payment: {o.PaymentMethod}");
        if (!string.IsNullOrWhiteSpace(o.Notes)) sb.AppendLine($"Note: {o.Notes}");
        sb.AppendLine();
        sb.Append("Order pakka karne ke liye *confirm* likhein (ya 1). Kuch badalna ho to bata dein.");
        return sb.ToString();
    }

    public static string ConfirmedReply(Order o, string currency)
    {
        var next = o.Type switch
        {
            OrderType.Delivery => "Rider nikalte hi aap ko bata diya jayega.",
            OrderType.Takeaway => "Tayyar hone par aap ko message kar diya jayega.",
            _ => "Aap ke aane par tayyar milega.",
        };
        return $"✅ Shukriya {o.CustomerName}! Aap ka order *#{o.Id}* confirm ho gaya hai.\n" +
               $"Total: *{TotalText(o, currency)}*" + (string.IsNullOrWhiteSpace(o.PaymentMethod) ? "" : $" ({o.PaymentMethod})") + "\n" +
               $"Order kitchen ko bhej diya gaya hai. {next}";
    }

    public const string DraftCancelledReply = "Theek hai, order cancel kar diya gaya hai. Kuch aur chahiye ho to bata dein. 🙂";

    /// <summary>Staff/kitchen ke WhatsApp par jaane wala ticket.</summary>
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
        return sb.ToString().TrimEnd();
    }

    public static string BookingTicket(TableBooking b) =>
        $"📅 *TABLE BOOKING REQUEST #{b.Id}*\n" +
        $"{b.BookingTime:dddd d MMM, h:mm tt}, {b.People} afraad\n" +
        $"Customer: {b.CustomerName}, +{b.Phone}" +
        (string.IsNullOrWhiteSpace(b.Notes) ? "" : $"\nNote: {b.Notes}") +
        "\nCustomer ko confirm kar dein.";

    public static string HandoverTicket(Customer c, string reason) =>
        $"🙋 *Customer ko staff chahiye*\n{c.Name ?? "Customer"}, +{c.Phone}\nWajah: {reason}\nBot is customer ke liye ruk gaya hai, WhatsApp Business app/dashboard se jawab dein.";

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

    // ---- Customer ke jawab pehchanna (AI ke baghair) ----

    /// <summary>"confirm", "confirm kar do", "ok confirm", "1" waghera. Inkaar wale alfaaz ho to false.</summary>
    public static bool IsConfirmation(string text, bool allowNumericShortcut)
    {
        var words = Words(text);
        if (words.Length == 0) return false;
        if (allowNumericShortcut && words is ["1"]) return true;
        if (words.Length > 5 || words.Any(NegativeWords.Contains)) return false;
        // "na karo" = mat karo; lekin "confirm kar do na" (aakhir mein "na") haan hai
        for (var i = 0; i < words.Length - 1; i++)
            if (words[i] == "na" && words[i + 1].StartsWith("kar", StringComparison.Ordinal)) return false;
        return words.Any(w => w is "confirm" or "confirmed" or "confrm" or "conform" or "confirmd");
    }

    /// <summary>"cancel", "cancel kar do" waghera.</summary>
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
