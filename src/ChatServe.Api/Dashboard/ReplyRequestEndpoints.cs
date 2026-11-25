using System.Security.Claims;
using ChatServe.Api.Ai;
using ChatServe.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ChatServe.Api.Dashboard;

/// <summary>
/// Clients ask for more AI replies; the admin approves or rejects. The admin can also add replies
/// for this month (top-up) without a request. The package itself is changed with PUT /clients/{id}/limit.
/// </summary>
public static class ReplyRequestEndpoints
{
    public const int MaxReplies = 100_000;

    public static void MapReplyRequestEndpoints(this RouteGroupBuilder loggedIn, RouteGroupBuilder admin)
    {
        // Owner: their own requests. Admin: everyone's (pending first).
        loggedIn.MapGet("/reply-requests", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var own = user.OwnOrganizationId();
            return await db.ReplyRequests.AsNoTracking()
                .Where(r => own == null || r.OrganizationId == own)
                .OrderBy(r => r.Status == ReplyRequestStatus.Pending ? 0 : 1).ThenByDescending(r => r.CreatedAt)
                .Take(100)
                .Select(r => new
                {
                    r.Id, r.OrganizationId,
                    ClientName = db.Organizations.Where(o => o.Id == r.OrganizationId).Select(o => o.Name).FirstOrDefault(),
                    r.RequestedBy, r.Replies, r.Kind, r.Note, r.Status, r.ApprovedReplies, r.AdminNote, r.CreatedAt, r.DecidedAt,
                })
                .ToListAsync(ct);
        });

        // Owner asks for more replies. One open request at a time; the admin gets a WhatsApp.
        loggedIn.MapPost("/reply-requests", async (NewRequest req, ClaimsPrincipal user, AppDbContext db, UsageAlerts alerts,
            TimeProvider clock, CancellationToken ct) =>
        {
            if (user.OwnOrganizationId() is not { } orgId)
                return RestaurantEndpoints.Bad("Only a client login can request replies. As admin, change the limit directly.");
            if (req.Replies is < 1 or > MaxReplies)
                return RestaurantEndpoints.Bad($"Enter how many replies you need (1 to {MaxReplies:N0}).");
            if (await db.ReplyRequests.AnyAsync(r => r.OrganizationId == orgId && r.Status == ReplyRequestStatus.Pending, ct))
                return RestaurantEndpoints.Bad("You already have a request waiting. You'll see the answer here.");

            var org = await db.Organizations.FirstAsync(o => o.Id == orgId, ct);
            var request = new ReplyRequest
            {
                OrganizationId = orgId,
                RequestedBy = user.Identity?.Name ?? "owner",
                Replies = req.Replies,
                Kind = req.Kind,
                Note = Clean(req.Note),
                Status = ReplyRequestStatus.Pending,
                CreatedAt = clock.GetUtcNow(),
            };
            db.ReplyRequests.Add(request);
            await db.SaveChangesAsync(ct);

            var what = req.Kind == ReplyRequestKind.TopUp ? "for this month" : "every month (bigger package)";
            await alerts.NotifyAdminAsync(
                $"📩 *ChatServe:* {org.Name} asks for {req.Replies:N0} more AI replies {what}." +
                (request.Note is null ? "" : $"\nNote: {request.Note}") + "\nDashboard → AI usage to approve.", ct);
            return Results.Ok(request);
        });

        // Owner can withdraw a request that is still waiting
        loggedIn.MapDelete("/reply-requests/{id:int}", async (int id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var own = user.OwnOrganizationId();
            var r = await db.ReplyRequests.FirstOrDefaultAsync(x => x.Id == id && (own == null || x.OrganizationId == own), ct);
            if (r is null) return Results.NotFound();
            if (r.Status != ReplyRequestStatus.Pending) return RestaurantEndpoints.Bad("This request has already been answered.");
            db.ReplyRequests.Remove(r);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        admin.MapPost("/reply-requests/{id:int}/approve", async (int id, Decision req, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var r = await db.ReplyRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            if (r.Status != ReplyRequestStatus.Pending) return RestaurantEndpoints.Bad("This request has already been answered.");
            var replies = req.Replies ?? r.Replies;
            if (replies is < 1 or > MaxReplies) return RestaurantEndpoints.Bad($"Replies must be 1 to {MaxReplies:N0}.");

            var org = await db.Organizations.FirstAsync(o => o.Id == r.OrganizationId, ct);
            var now = clock.GetUtcNow();
            if (r.Kind == ReplyRequestKind.TopUp)
                AddTopUp(org, replies, now);
            else
                org.MonthlyReplyLimit = (org.MonthlyReplyLimit ?? 0) + replies;
            org.UsageAlertLevel = 0; // more room: the alerts can fire again

            r.Status = ReplyRequestStatus.Approved;
            r.ApprovedReplies = replies;
            r.AdminNote = Clean(req.AdminNote);
            r.DecidedAt = now;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { r.Id, r.Status, org.MonthlyReplyLimit, ExtraThisMonth = org.ExtraFor(UsageAlerts.Month(now).Month) });
        });

        admin.MapPost("/reply-requests/{id:int}/reject", async (int id, Decision req, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var r = await db.ReplyRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            if (r.Status != ReplyRequestStatus.Pending) return RestaurantEndpoints.Bad("This request has already been answered.");
            r.Status = ReplyRequestStatus.Rejected;
            r.AdminNote = Clean(req.AdminNote);
            r.DecidedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { r.Id, r.Status });
        });

        // Admin: add (or take back, with a negative number) replies for this month only
        admin.MapPost("/clients/{id:int}/topup", async (int id, TopUp req, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (org is null) return Results.NotFound();
            if (org.MonthlyReplyLimit is null)
                return RestaurantEndpoints.Bad("This client has no monthly limit (unlimited). Set a package first.");
            if (req.Replies == 0 || Math.Abs(req.Replies) > MaxReplies)
                return RestaurantEndpoints.Bad($"Enter a number of replies (up to {MaxReplies:N0}).");
            AddTopUp(org, req.Replies, clock.GetUtcNow());
            if (req.Replies > 0) org.UsageAlertLevel = 0;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { org.Id, ExtraThisMonth = org.ExtraReplies });
        });
    }

    /// <summary>Adds to this month's top-up (a new month starts from zero). Never below zero.</summary>
    private static void AddTopUp(Organization org, int replies, DateTimeOffset now)
    {
        var month = UsageAlerts.Month(now).Month;
        var current = org.ExtraFor(month);
        org.ExtraRepliesMonth = month;
        org.ExtraReplies = Math.Max(0, current + replies);
    }

    private static string? Clean(string? s)
    {
        var v = s?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length <= 500 ? v : v[..500];
    }

    public sealed record NewRequest(int Replies, ReplyRequestKind Kind, string? Note);
    public sealed record Decision(int? Replies, string? AdminNote);
    public sealed record TopUp(int Replies);
}
