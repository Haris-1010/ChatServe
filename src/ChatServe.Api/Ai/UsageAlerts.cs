using ChatServe.Api.Configuration;
using ChatServe.Api.Data;
using ChatServe.Api.Orders;
using ChatServe.Api.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Ai;

/// <summary>
/// After an AI reply: if the client has just reached the warning % or 100% of its monthly replies, the admin gets a
/// WhatsApp message (once per level per month). It is only an alert; the bot keeps replying.
/// </summary>
public sealed class UsageAlerts(
    AppDbContext db,
    IWhatsAppClient whatsApp,
    IOptionsMonitor<UsageOptions> usage,
    IOptionsMonitor<WhatsAppOptions> whatsAppOptions,
    TimeProvider clock,
    ILogger<UsageAlerts> logger)
{
    public async Task CheckAsync(int organizationId, CancellationToken ct)
    {
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId, ct);
        var (month, from, to) = Month(clock.GetUtcNow());
        if (org is null || org.ReplyLimitFor(month) is not { } limit || limit <= 0)
            return;

        var rids = await db.Restaurants.Where(r => r.OrganizationId == organizationId).Select(r => r.Id).ToListAsync(ct);
        var replies = await db.ChatMessages.CountAsync(m =>
            rids.Contains(m.RestaurantId) && m.Role == ChatRole.Bot && m.InputTokens != null && m.CreatedAt >= from && m.CreatedAt < to, ct);

        var opts = usage.CurrentValue;
        var level = replies >= limit ? 2 : replies * 100 >= limit * opts.WarnAtPercent ? 1 : 0;
        if (org.UsageAlertMonth != month)
        {
            org.UsageAlertMonth = month;
            org.UsageAlertLevel = 0;
        }
        if (level <= org.UsageAlertLevel)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        org.UsageAlertLevel = level;
        await db.SaveChangesAsync(ct); // saved first: one alert per level even if sending fails

        var text = level == 2
            ? $"🔴 *ChatServe:* {org.Name} has used all {limit:N0} AI replies of this month ({replies:N0}). The bot keeps replying. Check Dashboard → AI usage."
            : $"⚠️ *ChatServe:* {org.Name} has used {replies * 100 / limit}% of this month's AI replies ({replies:N0} of {limit:N0}). Dashboard → AI usage.";
        logger.LogInformation("Usage alert for {Client}: {Replies}/{Limit}", org.Name, replies, limit);

        await NotifyAdminAsync(text, ct);
    }

    /// <summary>A WhatsApp message to the admin's number (Settings → AI cost). Nothing if no number is set; never throws.</summary>
    public async Task NotifyAdminAsync(string text, CancellationToken ct)
    {
        var to = new string(usage.CurrentValue.AdminWhatsAppNumber.Where(char.IsAsciiDigit).ToArray());
        var fromId = whatsAppOptions.CurrentValue.PhoneNumberId;
        if (to.Length < 10 || string.IsNullOrWhiteSpace(fromId))
            return; // no admin number set: the dashboard still shows it
        try
        {
            await whatsApp.SendTextAsync(fromId, to, text, replyToMessageId: null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "WhatsApp to admin failed (24-hour window closed?)");
        }
    }

    /// <summary>The current month in Pakistan time, as "2026-11" plus its UTC start and end.</summary>
    public static (string Month, DateTimeOffset From, DateTimeOffset To) Month(DateTimeOffset utcNow)
    {
        var pk = utcNow.ToOffset(Money.PakistanOffset);
        var first = new DateTime(pk.Year, pk.Month, 1);
        return (first.ToString("yyyy-MM"),
            new DateTimeOffset(first, Money.PakistanOffset).ToUniversalTime(),
            new DateTimeOffset(first.AddMonths(1), Money.PakistanOffset).ToUniversalTime());
    }
}
