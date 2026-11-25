using System.Globalization;
using System.Text.RegularExpressions;
using ChatServe.Api.Data;
using ChatServe.Api.WhatsApp;
using Microsoft.EntityFrameworkCore;

namespace ChatServe.Api.Orders;

/// <summary>What the feedback flow answered. <see cref="Reply"/> null: something was saved but the AI should write the reply. <see cref="AskedForInput"/>: the reply asks for a rating or comment,
/// so the saved bot message id goes on the order (<see cref="Order.FeedbackPromptMessageId"/>).</summary>
public sealed record FeedbackOutcome(string? Reply, InteractiveMessage? Buttons, Order Order, bool AskedForInput, IReadOnlyList<string> StaffAlerts);

/// <summary>
/// After a delivery, without AI:
/// 1. the customer says they got it ("mil gaya", "received", or the "✅ Mil gaya" button) → the order is marked received
///    (and completed if the rider hadn't been marked yet), and the bot asks for a 1-5 star rating (a WhatsApp list);
/// 2. the rating is saved; the bot asks for a comment (optional);
/// 3. the next message is saved as the comment. Low ratings and comments go to the staff WhatsApp.
/// "⚠️ Problem" hands the chat to staff. Everything shows on the dashboard's Feedback page.
/// </summary>
public sealed partial class FeedbackFlow(AppDbContext db, OrderService orders)
{
    /// <summary>How long after the last status change "mil gaya" still counts for a delivery.</summary>
    private static readonly TimeSpan ReceiveWindow = TimeSpan.FromHours(24);

    public const int LowRating = 3;

    public async Task<FeedbackOutcome?> TryHandleAsync(Restaurant restaurant, Customer customer, string? replyId, string text,
        long? lastBotMessageId, DateTimeOffset now, bool ru, CancellationToken ct)
    {
        // 1) A tapped button / list row carries the order number
        if (ParseReplyId(replyId) is { } tap)
        {
            var tapped = await db.Orders.FirstOrDefaultAsync(o => o.Id == tap.OrderId && o.CustomerId == customer.Id, ct);
            if (tapped is null) return null;
            return tap.Kind switch
            {
                "rcv" => await ReceivedAsync(tapped, now, ru, ct),
                "prob" => await ProblemAsync(customer, tapped, now, ru, ct),
                "rate" when tap.Value is >= 1 and <= 5 => await RateAsync(tapped, tap.Value, now, ru, ct),
                _ => null,
            };
        }

        // 2) Right after we asked for a rating or comment: a plain "5" / "⭐⭐⭐⭐" / any comment
        if (lastBotMessageId is { } lastBot)
        {
            var asked = await db.Orders.FirstOrDefaultAsync(o => o.CustomerId == customer.Id && o.FeedbackPromptMessageId == lastBot, ct);
            if (asked is not null)
            {
                if (asked.Rating is null && ParseRating(text) is { } stars)
                    return await RateAsync(asked, stars, now, ru, ct);
                if (asked.Rating is not null && !text.Contains('?'))
                    return await CommentAsync(asked, text, now, ru, ct);
            }
        }

        // 3) "mil gaya" / "received" for a delivery that's on the way or just delivered
        if (IsReceived(text))
        {
            var since = now - ReceiveWindow;
            var delivery = await db.Orders
                .Where(o => o.CustomerId == customer.Id && o.Type == OrderType.Delivery && o.ReceivedAt == null &&
                            (o.Status == OrderStatus.OutForDelivery || o.Status == OrderStatus.Completed) && o.UpdatedAt >= since)
                .OrderByDescending(o => o.UpdatedAt)
                .FirstOrDefaultAsync(ct);
            if (delivery is not null)
                return await ReceivedAsync(delivery, now, ru, ct);
        }

        return null;
    }

    private async Task<FeedbackOutcome?> ReceivedAsync(Order o, DateTimeOffset now, bool ru, CancellationToken ct)
    {
        if (o.Status is OrderStatus.PendingConfirmation or OrderStatus.Cancelled)
            return null;

        var alerts = new List<string>();
        if (o.ReceivedAt is null)
        {
            o.ReceivedAt = now;
            if (o.Status != OrderStatus.Completed)
            {
                o.Status = OrderStatus.Completed;
                o.UpdatedAt = now;
            }
            alerts.Add($"✅ {o.CustomerName} received order #{o.Id}. Marked completed.");
            await db.SaveChangesAsync(ct);
        }

        if (o.Rating is not null) // tapped "Mil gaya" again after rating
            return new FeedbackOutcome(ru ? "Shukriya! 🙏 Phir zaroor order kijiye ga." : "Thank you! 🙏 Hope to serve you again soon.",
                null, o, false, alerts);

        return new FeedbackOutcome(AskRatingText(o, ru), RatingList(o.Id, ru), o, true, alerts);
    }

    private async Task<FeedbackOutcome> ProblemAsync(Customer customer, Order o, DateTimeOffset now, bool ru, CancellationToken ct)
    {
        await orders.PauseBotAsync(customer, now, ct);
        return new FeedbackOutcome(
            ru ? $"Maaf kijiye 😔 Order *#{o.Id}* mein kya masla hai? Hamari team abhi yahin aap se baat kare gi."
               : $"We're sorry 😔 What went wrong with order *#{o.Id}*? Our team will reply to you here right away.",
            null, o, false,
            [$"⚠️ *{o.CustomerName}* (+{o.Phone}) reports a problem with order #{o.Id}. The bot is paused for this chat: please reply to the customer."]);
    }

    private async Task<FeedbackOutcome> RateAsync(Order o, int stars, DateTimeOffset now, bool ru, CancellationToken ct)
    {
        o.Rating = stars;
        o.FeedbackAt = now;
        o.ReceivedAt ??= now;
        await db.SaveChangesAsync(ct);

        var alerts = stars <= LowRating
            ? new List<string> { $"⚠️ Low rating: {Stars(stars)} ({stars}/5) for order #{o.Id} from {o.CustomerName} (+{o.Phone}). Asking them what went wrong." }
            : new List<string>();
        var reply = (stars >= 4, ru) switch
        {
            (true, true) => $"{Stars(stars)} Bohat shukriya! 🙏 Kuch aur kehna chahein to yahin likh dein.",
            (true, false) => $"{Stars(stars)} Thank you so much! 🙏 Anything else you'd like to tell us? Just type it here.",
            (false, true) => $"{Stars(stars)} Afsos hai 😔 Kya cheez pasand nahi aayi? Likh kar batayein, hum zaroor behtar karein ge.",
            _ => $"{Stars(stars)} We're sorry 😔 What didn't you like? Please tell us, we'll make it better.",
        };
        return new FeedbackOutcome(reply, null, o, true, alerts);
    }

    /// <summary>
    /// The message after the rating question: saved as the comment. A plain "ok/thanks" gets a fixed reply; anything else
    /// is answered by the AI (Reply = null), because it may also ask for something ("acha tha, ek aur bhej dein").
    /// </summary>
    private async Task<FeedbackOutcome> CommentAsync(Order o, string text, DateTimeOffset now, bool ru, CancellationToken ct)
    {
        o.FeedbackPromptMessageId = null; // one comment; after that the AI answers as usual
        if (IsJustThanks(text))
        {
            await db.SaveChangesAsync(ct);
            return new FeedbackOutcome(ru ? "Shukriya! 😊 Phir zaroor order kijiye ga." : "Thank you! 😊 Hope to serve you again soon.",
                null, o, false, []);
        }

        var comment = text.Trim();
        o.FeedbackComment = o.FeedbackComment is null ? comment : o.FeedbackComment + "\n" + comment;
        if (o.FeedbackComment.Length > 1000) o.FeedbackComment = o.FeedbackComment[..1000];
        o.FeedbackAt = now;
        await db.SaveChangesAsync(ct);
        return new FeedbackOutcome(null, null, o, false,
            [$"💬 Feedback on order #{o.Id} ({Stars(o.Rating ?? 0)} {o.Rating}/5) from {o.CustomerName}: \"{comment}\""]);
    }

    // ---- Messages and buttons ----

    public static string AskRatingText(Order o, bool ru) => ru
        ? $"Shukriya {o.CustomerName}! 😊 Order *#{o.Id}* kaisa laga? Neeche se rating dein ⭐"
        : $"Thank you {o.CustomerName}! 😊 How was order *#{o.Id}*? Please rate it below ⭐";

    public static InteractiveMessage RatingList(int orderId, bool ru) => new(
    [
        new($"rate:{orderId}:5", "⭐⭐⭐⭐⭐", ru ? "Zabardast" : "Excellent"),
        new($"rate:{orderId}:4", "⭐⭐⭐⭐", ru ? "Acha tha" : "Good"),
        new($"rate:{orderId}:3", "⭐⭐⭐", ru ? "Theek tha" : "Okay"),
        new($"rate:{orderId}:2", "⭐⭐", ru ? "Pasand nahi aaya" : "Not good"),
        new($"rate:{orderId}:1", "⭐", ru ? "Bohat bura" : "Very bad"),
    ], ListButton: ru ? "⭐ Rating dein" : "⭐ Rate your order", Header: ru ? "Aap ki rating" : "Your rating");

    /// <summary>Buttons under the "rider has left" and "delivered" messages.</summary>
    public static InteractiveMessage? DeliveryButtons(Order o, bool ru)
    {
        if (o.Type != OrderType.Delivery || o.ReceivedAt is not null) return null;
        return o.Status switch
        {
            OrderStatus.OutForDelivery => new([new($"rcv:{o.Id}", ru ? "✅ Order mil gaya" : "✅ Got my order")],
                Footer: ru ? "Order milne par button dabayein" : "Tap when your order arrives"),
            OrderStatus.Completed => new(
            [
                new($"rcv:{o.Id}", ru ? "✅ Mil gaya" : "✅ Got it"),
                new($"prob:{o.Id}", ru ? "⚠️ Masla hai" : "⚠️ Problem"),
            ]),
            _ => null,
        };
    }

    public static string Stars(int n) => new('⭐', Math.Clamp(n, 0, 5));

    // ---- Recognising replies ----

    private sealed record Tap(string Kind, int OrderId, int Value);

    private static Tap? ParseReplyId(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var parts = id.Split(':');
        if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var orderId))
            return null;
        var value = parts.Length > 2 && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0;
        return parts[0] is "rcv" or "prob" or "rate" ? new Tap(parts[0], orderId, value) : null;
    }

    /// <summary>"5", "4/5", "5 stars", "⭐⭐⭐⭐". Null if it isn't a rating.</summary>
    public static int? ParseRating(string text)
    {
        var t = text.Trim();
        var stars = t.Count(c => c == '⭐' || c == '★');
        if (stars is >= 1 and <= 5 && t.All(c => c is '⭐' or '★' or ' ' or '️'))
            return stars;
        var m = RatingRegex().Match(t.ToLowerInvariant());
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>"mil gaya", "order mil gaya shukriya", "received", "got it", "aa gaya". Not "nahi mila" or a question.</summary>
    public static bool IsReceived(string text)
    {
        if (text.Contains('?')) return false;
        var words = Words(text);
        if (words.Length is 0 or > 7) return false;
        if (words.Any(w => w is "nahi" or "nahin" or "nai" or "not" or "no" or "kab" or "kahan" or "missing" or "late")) return false;
        if (words.Any(ReceivedWords.Contains) || words.Any(w => w is "milgaya" or "milgya" or "agaya" or "agya" or "aagaya"))
            return true;
        if (words.Any(w => w is "mil" or "aa" or "pohnch" or "pahunch" or "pohanch" or "puhanch") && words.Any(Gone.Contains))
            return true;
        return words is ["got", "it", ..] or ["got", "my", "order", ..] or ["order", "delivered", ..] or ["delivered", ..];
    }

    private static readonly HashSet<string> ReceivedWords =
        ["received", "recieved", "recived", "receive", "recieve", "recive", "rcvd", "receved"];

    private static readonly HashSet<string> Gone = ["gaya", "gya", "gea", "gaye", "gai", "gayi", "gyi", "geya", "hogaya", "hogya"];

    private static bool IsJustThanks(string text)
    {
        var words = Words(text);
        return words.Length is > 0 and <= 4 && words.All(w => w is "ok" or "okay" or "thanks" or "thank" or "you" or "shukriya" or "jazakallah"
            or "bohat" or "bahut" or "sir" or "ji" or "g" or "theek" or "hai" or "thik" or "👍" or "nothing" or "nahi" or "kuch" or "no");
    }

    private static string[] Words(string text) =>
        NonWord().Replace(text.ToLowerInvariant(), " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);

    [GeneratedRegex(@"[^\p{L}\p{N}']+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"^([1-5])(?:\s*(?:/\s*5|stars?|star|⭐))?\s*$")]
    private static partial Regex RatingRegex();
}
