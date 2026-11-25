using System.Globalization;
using System.Security.Claims;
using ChatServe.Api.Ai;
using ChatServe.Api.Data;
using ChatServe.Api.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Dashboard;

/// <summary>
/// AI usage per client and restaurant for a month (Pakistan time): replies, tokens and cost in $ and PKR,
/// against the client's monthly limit. The admin sees every client; an owner only their own.
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
                .Select(o => new { o.Id, o.Name, o.MonthlyAiBudget, o.IsActive })
                .ToListAsync(ct);
            var orgIds = orgs.Select(o => o.Id).ToList();
            var restaurants = await db.Restaurants.AsNoTracking()
                .Where(r => orgIds.Contains(r.OrganizationId))
                .OrderBy(r => r.Name)
                .Select(r => new { r.Id, r.Name, r.OrganizationId })
                .ToListAsync(ct);
            var rids = restaurants.Select(r => r.Id).ToList();

            var replies = await db.ChatMessages.AsNoTracking()
                .Where(m => rids.Contains(m.RestaurantId) && m.Role == ChatRole.Bot && m.InputTokens != null
                            && m.CreatedAt >= from && m.CreatedAt < to)
                .GroupBy(m => m.RestaurantId)
                .Select(g => new Tokens(g.Key, g.Count(),
                    g.Sum(m => (long?)m.InputTokens) ?? 0, g.Sum(m => (long?)m.OutputTokens) ?? 0,
                    g.Sum(m => (long?)m.CacheReadTokens) ?? 0, g.Sum(m => (long?)m.CacheWriteTokens) ?? 0))
                .ToListAsync(ct);
            var extra = await db.AiUsageLogs.AsNoTracking()
                .Where(l => rids.Contains(l.RestaurantId) && l.CreatedAt >= from && l.CreatedAt < to)
                .GroupBy(l => l.RestaurantId)
                .Select(g => new Tokens(g.Key, 0,
                    g.Sum(l => (long)l.InputTokens), g.Sum(l => (long)l.OutputTokens),
                    g.Sum(l => (long)l.CacheReadTokens), g.Sum(l => (long)l.CacheWriteTokens)))
                .ToListAsync(ct);

            var p = prices.CurrentValue;
            UsageLine Line(IEnumerable<Tokens> parts)
            {
                var t = parts.Aggregate(new Tokens(0, 0, 0, 0, 0, 0), (a, b) => a + b);
                var usd = p.CostUsd(t.Input, t.Output, t.CacheRead, t.CacheWrite);
                return new UsageLine(t.Replies, t.Input, t.Output, t.CacheRead, t.CacheWrite,
                    Math.Round(usd, 4), Math.Round(usd * p.PkrPerUsd, 0));
            }

            var clients = orgs.Select(o =>
            {
                var rs = restaurants.Where(r => r.OrganizationId == o.Id).Select(r => new
                {
                    r.Id, r.Name,
                    Usage = Line(replies.Concat(extra).Where(t => t.RestaurantId == r.Id)),
                }).ToList();
                var usage = Line(replies.Concat(extra).Where(t => rs.Any(r => r.Id == t.RestaurantId)));
                int? percent = o.MonthlyAiBudget is > 0 ? (int)Math.Round(usage.CostPkr * 100 / o.MonthlyAiBudget.Value) : null;
                return new
                {
                    o.Id, o.Name, o.IsActive, o.MonthlyAiBudget,
                    Remaining = o.MonthlyAiBudget is { } b ? Math.Max(0, b - usage.CostPkr) : (decimal?)null,
                    Percent = percent,
                    Warning = percent >= p.WarnAtPercent,
                    OverLimit = percent >= 100,
                    Usage = usage,
                    Restaurants = rs,
                };
            }).ToList();

            return Results.Ok(new
            {
                Month = first.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                p.PkrPerUsd,
                p.WarnAtPercent,
                Total = Line(replies.Concat(extra)),
                Clients = clients,
            });
        });

        admin.MapPut("/clients/{id:int}/budget", async (int id, BudgetRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (org is null) return Results.NotFound();
            if (req.MonthlyAiBudget is < 0)
                return RestaurantEndpoints.Bad("The limit can't be negative.");
            org.MonthlyAiBudget = req.MonthlyAiBudget is > 0 ? req.MonthlyAiBudget : null;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { org.Id, org.MonthlyAiBudget });
        });
    }

    private sealed record Tokens(int RestaurantId, int Replies, long Input, long Output, long CacheRead, long CacheWrite)
    {
        public static Tokens operator +(Tokens a, Tokens b) => new(a.RestaurantId, a.Replies + b.Replies,
            a.Input + b.Input, a.Output + b.Output, a.CacheRead + b.CacheRead, a.CacheWrite + b.CacheWrite);
    }

    public sealed record UsageLine(int Replies, long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens,
        decimal CostUsd, decimal CostPkr);

    public sealed record BudgetRequest(decimal? MonthlyAiBudget);
}
