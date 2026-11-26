using System.Text.RegularExpressions;
using ChatServe.Api.Data;
using ChatServe.Api.Orders;
using Microsoft.EntityFrameworkCore;

namespace ChatServe.Api.Messaging;

/// <summary>
/// The fixed (non-AI) messages customers get: order confirmed, status updates, "can't read this", etc.
/// Sent in Roman Urdu when the customer writes Roman Urdu (or hasn't written any text yet), otherwise in English,
/// so the bot sounds like the same person as the AI replies.
/// </summary>
public static partial class CustomerText
{
    private static readonly HashSet<string> RomanUrduWords =
    [
        "hai", "hain", "ho", "hy", "kar", "karo", "karein", "karen", "kr", "krdo", "kardo", "krna", "karna", "kya", "kia",
        "ka", "ki", "ke", "ko", "mein", "mujhe", "mjhe", "mujhay", "aap", "ap", "apka", "aapka", "bhai", "bhej", "bhejo",
        "bhejdo", "bhejein", "chahiye", "chahye", "chahiay", "nahi", "nai", "nahin", "haan", "han", "ji", "jee", "theek", "thik",
        "gea", "gaya", "gya", "gayi", "kab", "kitna", "kitne", "kitni", "abhi", "wala", "wali", "dein", "sath", "saath",
        "aur", "yar", "yaar", "salam", "assalam", "aoa", "shukriya", "jazakallah", "kahan", "kaise", "kesa", "kaisa", "hoga",
        "hogi", "raha", "rha", "rahi", "rhi", "tak", "se", "bhi", "acha", "achha", "lena", "dena", "milega", "milay",
    ];

    /// <summary>True when the text looks like Roman Urdu ("order kab tak aye ga?").</summary>
    public static bool IsRomanUrdu(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (UrduScript().IsMatch(text)) return true;
        var words = NonWord().Split(text.ToLowerInvariant()).Where(w => w.Length > 0).ToArray();
        var hits = words.Count(RomanUrduWords.Contains);
        return hits >= 2 || (hits == 1 && words.Length <= 3);
    }

    /// <summary>
    /// Should this customer get Roman Urdu? Looks at the current text and their last few messages.
    /// No text at all yet (only a voice note or picture): Roman Urdu, the common case in Pakistan.
    /// </summary>
    public static async Task<bool> PrefersRomanUrduAsync(AppDbContext db, int customerId, string? current, CancellationToken ct)
    {
        var recent = await db.ChatMessages.AsNoTracking()
            .Where(m => m.CustomerId == customerId && m.Role == ChatRole.Customer)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Take(6)
            .Select(m => m.Text)
            .ToListAsync(ct);
        var texts = recent.Prepend(current ?? "").Where(t => !string.IsNullOrWhiteSpace(t) && !t.StartsWith('[')).ToList();
        return texts.Count == 0 || texts.Any(IsRomanUrdu);
    }

    public static string Confirmed(Order o, string currency, bool ru)
    {
        var total = OrderFormatter.TotalText(o, currency);
        var pay = string.IsNullOrWhiteSpace(o.PaymentMethod) ? "" : $" ({o.PaymentMethod})";
        if (!ru) return OrderFormatter.ConfirmedReply(o, currency);
        var next = o.Type switch
        {
            OrderType.Delivery => "Rider nikalte hi aap ko bata dein ge.",
            OrderType.Takeaway => "Tayyar hote hi aap ko message kar dein ge.",
            _ => "Aap ke aane tak tayyar ho jaye ga.",
        };
        return $"✅ Shukriya {o.CustomerName}! Aap ka order *#{o.Id}* confirm ho gaya hai.\n" +
               $"Total: *{total}*{pay}\n" +
               $"Hamari team ne kaam shuru kar diya hai. {next}";
    }

    public static string DraftCancelled(bool ru) => ru
        ? "Theek hai, order cancel kar diya hai. Kuch aur chahiye ho to bata dein 🙂"
        : OrderFormatter.DraftCancelledReply;

    /// <summary>The reply to a voice note that can't be turned into text (same in Roman Urdu and English).</summary>
    public const string VoiceReply = "Please text me 🙏";

    public static string CannotRead(bool ru, bool voice) => (ru, voice) switch
    {
        (true, true) => VoiceReply,
        (true, false) => "Maaf kijiye, abhi hum sirf likhe hue messages parh sakte hain. Please apna sawal likh kar bhej dein 🙏",
        (false, true) => VoiceReply,
        _ => ClaudeReplyGenerator.NonTextReply,
    };

    public static string Fallback(bool ru) => ru
        ? "Maaf kijiye, abhi jawab dene mein thori mushkil aa rahi hai. Hamari team jaldi yahin aap ko jawab de gi 🙏"
        : ClaudeReplyGenerator.FallbackReply;

    /// <summary>The line above tappable choices when the AI wrote no text of its own.</summary>
    public static string ChooseBelow(bool ru) => ru ? "Neeche se choose karein 👇" : "Please choose below 👇";

    public static string Handover(bool ru) => ru
        ? "Shukriya! Hamari team ka koi member jaldi yahin aap ko jawab de ga 🙏"
        : ClaudeReplyGenerator.HandoverReply;

    /// <summary>The message a customer gets when staff move their order on (dashboard or WhatsApp command).</summary>
    public static string Status(Order o, string currency, bool ru)
    {
        if (!ru) return Dashboard.OperationsEndpoints.OrderStatusMessage(o, currency);
        var total = OrderFormatter.TotalText(o, currency);
        return o.Status switch
        {
            OrderStatus.Confirmed => $"✅ Aap ka order *#{o.Id}* confirm ho gaya hai. Total: *{total}*",
            OrderStatus.Preparing => $"👨‍🍳 Aap ka order *#{o.Id}* tayyar ho raha hai.",
            OrderStatus.OutForDelivery => $"🛵 Aap ka order *#{o.Id}* le kar rider nikal gaya hai. Total: *{total}*",
            OrderStatus.Completed => o.Type == OrderType.Takeaway
                ? $"🎉 Aap ka order *#{o.Id}* tayyar hai, aa kar le jayen. Shukriya!"
                : $"🙏 Aap ka order *#{o.Id}* deliver kar diya gaya hai. Kya aap ko order mil gaya? 😊\nKoi cheez kam ya ghalat ho to yahin bata dein. Khana kaisa laga, zaroor batayein!",
            OrderStatus.Cancelled => $"Maaf kijiye, aap ka order *#{o.Id}* cancel ho gaya hai. Koi sawal ho to yahin message kar dein.",
            _ => $"Order *#{o.Id}*: {OrderFormatter.StatusText(o.Status)}",
        };
    }

    [GeneratedRegex(@"[^\p{L}\p{N}']+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"[؀-ۿ]")]
    private static partial Regex UrduScript();
}
