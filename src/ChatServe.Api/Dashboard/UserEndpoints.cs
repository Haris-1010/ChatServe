using System.Security.Claims;
using System.Text.RegularExpressions;
using ChatServe.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ChatServe.Api.Dashboard;

/// <summary>Restaurant owner logins: the admin creates, resets and deletes them; owners change their own password.</summary>
public static partial class UserEndpoints
{
    public const int MinPasswordLength = 8;

    private static readonly PasswordHasher<DashboardUser> Hasher = new();

    public static string HashPassword(DashboardUser user, string password) => Hasher.HashPassword(user, password);

    public static bool VerifyPassword(DashboardUser user, string password) =>
        Hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;

    public static string NormalizeUsername(string? username) => (username ?? "").Trim().ToLowerInvariant();

    /// <summary>Who is logged in. Used by the dashboard to show or hide admin-only pages.</summary>
    public static void MapMeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new
        {
            username = user.Identity?.Name,
            role = user.IsAdmin() ? AccessControl.AdminRole : AccessControl.OwnerRole,
            restaurantId = user.OwnRestaurantId(),
        }));

        api.MapPost("/me/password", async (ChangePasswordRequest req, ClaimsPrincipal user, AppDbContext db,
            CancellationToken ct) =>
        {
            if (user.IsAdmin())
                return Bad("The admin password is changed in Settings > Dashboard.");
            var account = await db.DashboardUsers.FirstOrDefaultAsync(u => u.Id == user.UserId(), ct);
            if (account is null) return Results.Unauthorized();
            if (!VerifyPassword(account, req.CurrentPassword ?? ""))
                return Bad("Your current password is wrong.");
            if (PasswordProblem(req.NewPassword) is { } problem)
                return Bad(problem);

            account.PasswordHash = HashPassword(account, req.NewPassword!);
            account.SecurityStamp = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    public static void MapUserAdminEndpoints(this RouteGroupBuilder admin)
    {
        admin.MapGet("/users", async (AppDbContext db, CancellationToken ct) =>
            await (from u in db.DashboardUsers.AsNoTracking()
                   join r in db.Restaurants.AsNoTracking() on u.RestaurantId equals r.Id
                   orderby r.Name, u.Username
                   select new { u.Id, u.Username, u.RestaurantId, RestaurantName = r.Name, u.CreatedAt, u.LastLoginAt })
                .ToListAsync(ct));

        admin.MapPost("/users", async (NewUserRequest req, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var username = NormalizeUsername(req.Username);
            if (UsernameProblem(username) is { } bad) return Bad(bad);
            if (PasswordProblem(req.Password) is { } weak) return Bad(weak);
            if (!await db.Restaurants.AnyAsync(r => r.Id == req.RestaurantId, ct))
                return Bad("Choose a restaurant for this login.");
            if (await db.DashboardUsers.AnyAsync(u => u.Username == username, ct))
                return Bad("That username is already taken.");

            var user = new DashboardUser
            {
                Username = username,
                PasswordHash = "",
                RestaurantId = req.RestaurantId,
                CreatedAt = clock.GetUtcNow(),
            };
            user.PasswordHash = HashPassword(user, req.Password!);
            db.DashboardUsers.Add(user);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { user.Id, user.Username, user.RestaurantId });
        });

        admin.MapPut("/users/{id:int}/password", async (int id, ResetPasswordRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var user = await db.DashboardUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user is null) return Results.NotFound();
            if (PasswordProblem(req.Password) is { } weak) return Bad(weak);
            user.PasswordHash = HashPassword(user, req.Password!);
            user.SecurityStamp = Guid.NewGuid().ToString("N"); // logs the owner out everywhere
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        admin.MapDelete("/users/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var user = await db.DashboardUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user is null) return Results.NotFound();
            db.DashboardUsers.Remove(user);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static string? UsernameProblem(string username) =>
        username.Length is < 3 or > 50 ? "Username must be 3 to 50 characters."
        : !UsernamePattern().IsMatch(username) ? "Username can only use letters, numbers, dots, dashes and underscores."
        : username == AccessControl.AdminUsername ? "\"admin\" is reserved for the admin login."
        : null;

    private static string? PasswordProblem(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < MinPasswordLength
            ? $"Password must be at least {MinPasswordLength} characters."
            : password.Length > 200 ? "Password is too long." : null;

    private static IResult Bad(string message) => Results.Problem(message, statusCode: StatusCodes.Status400BadRequest);

    [GeneratedRegex("^[a-z0-9._-]+$")]
    private static partial Regex UsernamePattern();

    public sealed record NewUserRequest(string? Username, string? Password, int RestaurantId);
    public sealed record ResetPasswordRequest(string? Password);
    public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
}
