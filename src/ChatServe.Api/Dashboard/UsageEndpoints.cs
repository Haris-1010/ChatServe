using System.Globalization;
using System.Security.Claims;
using ChatServe.Api.Ai;
using ChatServe.Api.Configuration;
using ChatServe.Api.Data;
using ChatServe.Api.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Dashboard;

/// <summary>
/// AI usage per client and restaurant for a month (Pakistan time): replies, tokens and cost in $ and PKR,
/// against the client's monthly AI reply limit. The admin sees every client; an owner only their own.
/// The limit is only a warning: the bot keeps replying.
/// </summary>
public static class UsageEndpoints
{
    public static void MapUsageEndpoints(this RouteGroupBuilder loggedIn, RouteGroupBuilder admin)
    {
        loggedIn.MapGet("/usage", async (string? month, ClaimsPrincipal user, AppDbContext db,
            IOptionsMonitor<UsageOptions> prices, TimeProvider clock, CancellationToken ct) =>
        {
            var pkNow = clock.GetUtcNow().ToOffset(Money.PakistanOffset);
            DateTime first;
            if (string.IsNullOrWhiteSpace(month))
                first = new DateTime(pkNow.Year, pkNow.Month, 1);
            else if (!DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out first))
                return RestaurantEndpoints.Bad("Month must look like 2026-11.");
            var from = new DateTimeOffset(first, Money.PakistanOffset).ToUniversalTime();
            var to = new DateTimeOffset(first.AddMonths(1), Money.PakistanOffset).ToUniversalTime();

            var own = user.OwnOrganizationId();
            var orgs = await db.Organizations.AsNoTracking()
                .Where(o => own == null || o.Id == own)
                .OrderBy(o => o.Name)
                .Select(o => new { o.Id, o.Name, o.MonthlyReplyLimit, o.IsActive })
                .ToListAsync(ct);
            var orgIds = orgs.Select(o => o.Id).ToList();
            var restaurants = await db.Restaurants.AsNoTracking()
                .Where(r => orgIds.Contains(r.OrganizationId))
                .OrderBy(r => r.Name)
                .Select(r => new { r.Id, r.Name, r.OrganizationId })
                .ToListAsync(ct);
            var rids = restaurants.Select(r => r.Id).ToList();

            // One row per AI call (bot replies + other AI use such as reading screenshots), totalled in memory:
            // per restaurant, per client and per day.
            var botRows = await db.ChatMessages.AsNoTracking()
                .Where(m => rids.Contains(m.RestaurantId) && m.Role == ChatRole.Bot && m.InputTokens != null
                            && m.CreatedAt >= from && m.CreatedAt < to)
                .Select(m => new Row(m.RestaurantId, m.CreatedAt, 1,
                    m.InputTokens ?? 0, m.OutputTokens ?? 0, m.CacheReadTokens ?? 0, m.CacheWriteTokens ?? 0))
                .ToListAsync(ct);
            var extraRows = await db.AiUsageLogs.AsNoTracking()
                .Where(l => rids.Contains(l.RestaurantId) && l.CreatedAt >= from && l.CreatedAt < to)
                .Select(l => new Row(l.RestaurantId, l.CreatedAt, 0, l.InputTokens, l.OutputTokens, l.CacheReadTokens, l.CacheWriteTokens))
                .ToListAsync(ct);
            var rows = botRows.Concat(extraRows).ToList();

            var p = prices.CurrentValue;
            var showCost = own == null; // owners see their replies, not our tokens and cost
            UsageLine Line(IEnumerable<Row> part)
            {
                long i = 0, o = 0, cr = 0, cw = 0;
                var n = 0;
                foreach (var r in part) { n += r.Replies; i += r.Input; o += r.Output; cr += r.CacheRead; cw += r.CacheWrite; }
                if (!showCost) return new UsageLine(n, 0, 0, 0, 0, 0, 0);
                var usd = p.CostUsd(i, o, cr, cw);
                return new UsageLine(n, i, o, cr, cw, Math.Round(usd, 4), Math.Round(usd * p.PkrPerUsd, 0));
            }

            var total = Line(rows);
            var days = Enumerable.Range(0, (first.AddMonths(1) - first).Days).Select(d => first.AddDays(d)).ToList();
            static DateTime PkDay(DateTimeOffset t) => t.ToOffset(Money.PakistanOffset).Date;

            var clients = orgs.Select(o =>
            {
                var orgRids = restaurants.Where(r => r.OrganizationId == o.Id).Select(r => r.Id).ToHashSet();
                var orgRows = rows.Where(r => orgRids.Contains(r.RestaurantId)).ToList();
                var rs = restaurants.Where(r => r.OrganizationId == o.Id).Select(r => new
                {
                    r.Id, r.Name,
                    Usage = Line(orgRows.Where(x => x.RestaurantId == r.Id)),
                }).ToList();
                var usage = Line(orgRows);
                var tokens = usage.InputTokens + usage.OutputTokens + usage.CacheReadTokens + usage.CacheWriteTokens;
                var byDay = orgRows.ToLookup(r => PkDay(r.At));
                var daily = days.Where(d => d <= pkNow.Date)
                    .Select(d => new { Date = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Usage = Line(byDay[d]) })
                    .ToList();
                int? percent = o.MonthlyReplyLimit is > 0 ? (int)Math.Round(usage.Replies * 100m / o.MonthlyReplyLimit.Value) : null;
                var isThisMonth = first.Year == pkNow.Year && first.Month == pkNow.Month;
                var daysGone = isThisMonth ? pkNow.Day : days.Count;
                return new
                {
                    o.Id, o.Name, o.IsActive, o.MonthlyReplyLimit,
                    RemainingReplies = o.MonthlyReplyLimit is { } l ? Math.Max(0, l - usage.Replies) : (int?)null,
                    Percent = percent,
                    Warning = percent >= p.WarnAtPercent,
                    OverLimit = percent >= 100,
                    Usage = usage,
                    TotalTokens = tokens,
                    AvgTokensPerReply = usage.Replies > 0 ? tokens / usage.Replies : 0,
                    AvgCostPkrPerReply = usage.Replies > 0 ? Math.Round(usage.CostPkr / usage.Replies, 2) : 0,
                    SharePercent = showCost && total.CostUsd > 0 ? (int)Math.Round(usage.CostUsd * 100 / total.CostUsd) : (int?)null,
                    RepliesPerDay = daysGone > 0 ? Math.Round(usage.Replies / (decimal)daysGone, 1) : 0,
                    // At this month's pace, the replies at month end (to see early who will run out)
                    ProjectedReplies = isThisMonth && daysGone > 0 ? (int)Math.Round(usage.Replies * (decimal)days.Count / daysGone) : usage.Replies,
                    Daily = daily,
                    Restaurants = rs,
                };
            }).ToList();

            return Results.Ok(new
            {
                Month = first.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                ShowCost = showCost,
                PkrPerUsd = showCost ? p.PkrPerUsd : 0,
                p.WarnAtPercent,
                Total = total,
                Credit = showCost ? await CreditAsync(db, p, total, ct) : null,
                Clients = clients,
            });
        });

        // The admin types in the balance from console.anthropic.com → Billing (Anthropic has no API for it).
        // From then on, AI use is subtracted from it to show what's left.
        admin.MapPut("/usage/credit", async (CreditRequest req, SettingsStore store, TimeProvider clock, CancellationToken ct) =>
        {
            if (req.BalanceUsd is < 0)
                return RestaurantEndpoints.Bad("The balance can't be negative.");
            await store.SaveInternalAsync(new Dictionary<string, string?>
            {
                ["Usage:CreditBalanceUsd"] = req.BalanceUsd?.ToString(CultureInfo.InvariantCulture),
                ["Usage:CreditBalanceSetAt"] = req.BalanceUsd is null ? null : clock.GetUtcNow().ToString("o", CultureInfo.InvariantCulture),
            }, ct);
            return Results.NoContent();
        });

        admin.MapPut("/clients/{id:int}/limit", async (int id, LimitRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (org is null) return Results.NotFound();
            if (req.MonthlyReplyLimit is < 0)
                return RestaurantEndpoints.Bad("The limit can't be negative.");
            org.MonthlyReplyLimit = req.MonthlyReplyLimit is > 0 ? req.MonthlyReplyLimit : null;
            org.UsageAlertLevel = 0; // a new limit: alerts can fire again
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { org.Id, org.MonthlyReplyLimit });
        });
    }

    /// <summary>
    /// What's left of the Anthropic credit: the entered balance minus the cost of all AI use since it was entered
    /// (every client). Tokens and replies left are estimates at the average cost seen since then
    /// (or this month's average when there's no use yet).
    /// </summary>
    private static async Task<object?> CreditAsync(AppDbContext db, UsageOptions p, UsageLine month, CancellationToken ct)
    {
        if (p.CreditBalanceUsd is not { } balance || p.CreditBalanceSetAt is not { } since)
            return null;

        var bot = await db.ChatMessages.AsNoTracking()
            .Where(m => m.Role == ChatRole.Bot && m.InputTokens != null && m.CreatedAt >= since)
            .GroupBy(m => 1)
            .Select(g => new Tokens(0, g.Count(),
                g.Sum(m => (long?)m.InputTokens) ?? 0, g.Sum(m => (long?)m.OutputTokens) ?? 0,
                g.Sum(m => (long?)m.CacheReadTokens) ?? 0, g.Sum(m => (long?)m.CacheWriteTokens) ?? 0))
            .FirstOrDefaultAsync(ct);
        var other = await db.AiUsageLogs.AsNoTracking()
            .Where(l => l.CreatedAt >= since)
            .GroupBy(l => 1)
            .Select(g => new Tokens(0, 0,
                g.Sum(l => (long)l.InputTokens), g.Sum(l => (long)l.OutputTokens),
                g.Sum(l => (long)l.CacheReadTokens), g.Sum(l => (long)l.CacheWriteTokens)))
            .FirstOrDefaultAsync(ct);
        var t = (bot ?? new Tokens(0, 0, 0, 0, 0, 0)) + (other ?? new Tokens(0, 0, 0, 0, 0, 0));
        var spent = p.CostUsd(t.Input, t.Output, t.CacheRead, t.CacheWrite);
        var remaining = Math.Max(0, balance - spent);

        // Average cost per token and per reply: since the balance was entered, else this month
        var tokens = t.Input + t.Output + t.CacheRead + t.CacheWrite;
        var (cost, tokenCount, replyCount) = tokens > 0
            ? (spent, tokens, t.Replies)
            : (month.CostUsd, month.InputTokens + month.OutputTokens + month.CacheReadTokens + month.CacheWriteTokens, month.Replies);
        long? tokensLeft = cost > 0 && tokenCount > 0 ? (long)Math.Floor(remaining * tokenCount / cost) : null;
        long? repliesLeft = cost > 0 && replyCount > 0 ? (long)Math.Floor(remaining * replyCount / cost) : null;

        return new
        {
            BalanceUsd = balance,
            SetAt = since,
            SpentUsd = Math.Round(spent, 4),
            SpentTokens = tokens,
            RemainingUsd = Math.Round(remaining, 4),
            RemainingPkr = Math.Round(remaining * p.PkrPerUsd, 0),
            Percent = balance > 0 ? (int)Math.Round(remaining * 100m / balance) : 0,
            TokensLeft = tokensLeft,
            RepliesLeft = repliesLeft,
        };
    }

    public sealed record CreditRequest(decimal? BalanceUsd);

    private sealed record Row(int RestaurantId, DateTimeOffset At, int Replies, long Input, long Output, long CacheRead, long CacheWrite);

    private sealed record Tokens(int RestaurantId, int Replies, long Input, long Output, long CacheRead, long CacheWrite)
    {
        public static Tokens operator +(Tokens a, Tokens b) => new(a.RestaurantId, a.Replies + b.Replies,
            a.Input + b.Input, a.Output + b.Output, a.CacheRead + b.CacheRead, a.CacheWrite + b.CacheWrite);
    }

    public sealed record UsageLine(int Replies, long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens,
        decimal CostUsd, decimal CostPkr);

    public sealed record LimitRequest(int? MonthlyReplyLimit);
}
