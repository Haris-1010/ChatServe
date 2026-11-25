using System.Security.Claims;
using ChatServe.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ChatServe.Api.Dashboard;

/// <summary>
/// Who may do what in the dashboard API.
/// - admin: logs in with Dashboard:AdminPassword (username "admin"), sees every restaurant, settings and users.
/// - owner: a <see cref="DashboardUser"/> of an <see cref="Organization"/> (the client); only that organization's
///   restaurants, no app settings. One organization can have several restaurants (branches).
/// </summary>
public static class AccessControl
{
    public const string AdminRole = "admin";
    public const string OwnerRole = "owner";
    public const string AdminPolicy = "admin";
    public const string AdminUsername = "admin";

    public const string OrganizationClaim = "oid";
    public const string UserIdClaim = "uid";
    public const string StampClaim = "stamp";

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(AdminRole);

    /// <summary>The owner's organization, or null for the admin (who sees all of them).</summary>
    public static int? OwnOrganizationId(this ClaimsPrincipal user) =>
        user.IsAdmin() ? null : int.TryParse(user.FindFirstValue(OrganizationClaim), out var oid) ? oid : -1;

    public static int? UserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(UserIdClaim), out var id) ? id : null;

    public static ClaimsPrincipal AdminPrincipal(string scheme) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, AdminUsername), new Claim(ClaimTypes.Role, AdminRole)], scheme));

    public static ClaimsPrincipal OwnerPrincipal(DashboardUser u, string scheme) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, u.Username),
            new Claim(ClaimTypes.Role, OwnerRole),
            new Claim(UserIdClaim, u.Id.ToString()),
            new Claim(OrganizationClaim, u.OrganizationId.ToString()),
            new Claim(StampClaim, u.SecurityStamp),
        ], scheme));

    /// <summary>
    /// Runs on every logged-in dashboard request.
    /// Owners: the account must still exist with the same security stamp (deleted user or reset password = logged out),
    /// its organization must be active (not suspended), and any {rid} in the URL must belong to their organization.
    /// </summary>
    public sealed class RestaurantAccessFilter : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var http = context.HttpContext;
            var user = http.User;
            if (user.IsAdmin())
                return await next(context);

            var uid = user.UserId();
            if (uid is null)
                return Results.Unauthorized();

            var db = http.RequestServices.GetRequiredService<AppDbContext>();
            var account = await db.DashboardUsers.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == uid, http.RequestAborted);
            if (account is null || account.SecurityStamp != user.FindFirstValue(StampClaim)
                                || account.OrganizationId != user.OwnOrganizationId())
                return Results.Unauthorized();

            if (!await db.Organizations.AnyAsync(o => o.Id == account.OrganizationId && o.IsActive, http.RequestAborted))
                return Results.Unauthorized();

            if (http.Request.RouteValues.TryGetValue("rid", out var raw))
            {
                // Unknown restaurant and another client's restaurant look the same (no hint that it exists).
                if (!int.TryParse(raw?.ToString(), out var rid)
                    || !await db.Restaurants.AnyAsync(r => r.Id == rid && r.OrganizationId == account.OrganizationId, http.RequestAborted))
                    return Results.Problem("You can only manage your own restaurants.", statusCode: StatusCodes.Status403Forbidden);
            }

            return await next(context);
        }
    }
}
