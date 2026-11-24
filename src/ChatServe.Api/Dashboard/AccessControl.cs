using System.Security.Claims;
using ChatServe.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ChatServe.Api.Dashboard;

/// <summary>
/// Who may do what in the dashboard API.
/// - admin: logs in with Dashboard:AdminPassword (username "admin"), sees every restaurant, settings and users.
/// - owner: a <see cref="DashboardUser"/>; only their own restaurant, no app settings.
/// </summary>
public static class AccessControl
{
    public const string AdminRole = "admin";
    public const string OwnerRole = "owner";
    public const string AdminPolicy = "admin";
    public const string AdminUsername = "admin";

    public const string RestaurantClaim = "rid";
    public const string UserIdClaim = "uid";
    public const string StampClaim = "stamp";

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(AdminRole);

    /// <summary>The owner's restaurant, or null for the admin.</summary>
    public static int? OwnRestaurantId(this ClaimsPrincipal user) =>
        user.IsAdmin() ? null : int.TryParse(user.FindFirstValue(RestaurantClaim), out var rid) ? rid : -1;

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
            new Claim(RestaurantClaim, u.RestaurantId.ToString()),
            new Claim(StampClaim, u.SecurityStamp),
        ], scheme));

    /// <summary>
    /// Runs on every logged-in dashboard request.
    /// Owners: the account must still exist with the same security stamp (deleted user or reset password = logged out),
    /// and any {rid} in the URL must be their own restaurant.
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
                                || account.RestaurantId != user.OwnRestaurantId())
                return Results.Unauthorized();

            if (http.Request.RouteValues.TryGetValue("rid", out var raw)
                && int.TryParse(raw?.ToString(), out var rid) && rid != account.RestaurantId)
                return Results.Problem("You can only manage your own restaurant.", statusCode: StatusCodes.Status403Forbidden);

            return await next(context);
        }
    }
}
