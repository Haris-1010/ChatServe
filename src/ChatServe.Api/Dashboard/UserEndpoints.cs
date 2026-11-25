using System.Security.Claims;
using System.Text.RegularExpressions;
using ChatServe.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ChatServe.Api.Dashboard;

/// <summary>Client (organization) logins: the admin creates, resets and deletes them; owners change their own password.</summary>
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
        api.MapGet("/me", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var oid = user.OwnOrganizationId();
            var orgName = oid is null ? null
                : await db.Organizations.AsNoTracking().Where(o => o.Id == oid).Select(o => o.Name).FirstOrDefaultAsync(ct);
            return Results.Ok(new
            {
                username = user.Identity?.Name,
                role = user.IsAdmin() ? AccessControl.AdminRole : AccessControl.OwnerRole,
                organizationId = oid,
                organizationName = orgName,
            });
        });

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
                   join o in db.Organizations.AsNoTracking() on u.OrganizationId equals o.Id
                   orderby o.Name, u.Username
                   select new { u.Id, u.Username, u.OrganizationId, OrganizationName = o.Name, u.CreatedAt, u.LastLoginAt })
                .ToListAsync(ct));

        admin.MapPost("/users", async (NewUserRequest req, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (await NewUserProblem(db, req.Username, req.Password, ct) is { } problem) return Bad(problem);
            if (!await db.Organizations.AnyAsync(o => o.Id == req.OrganizationId, ct))
                return Bad("Choose a client for this login.");

            var user = NewUser(req.Username, req.Password!, clock);
            user.OrganizationId = req.OrganizationId;
            db.DashboardUsers.Add(user);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { user.Id, user.Username, user.OrganizationId });
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

    /// <summary>Username/password rules for a new login, including "already taken".</summary>
    public static async Task<string?> NewUserProblem(AppDbContext db, string? rawUsername, string? password, CancellationToken ct)
    {
        var username = NormalizeUsername(rawUsername);
        if (UsernameProblem(username) is { } bad) return bad;
        if (PasswordProblem(password) is { } weak) return weak;
        return await db.DashboardUsers.AnyAsync(u => u.Username == username, ct) ? "That username is already taken." : null;
    }

    /// <summary>A new login (OrganizationId still to be set). Call <see cref="NewUserProblem"/> first.</summary>
    public static DashboardUser NewUser(string? username, string password, TimeProvider clock)
    {
        var user = new DashboardUser { Username = NormalizeUsername(username), PasswordHash = "", CreatedAt = clock.GetUtcNow() };
        user.PasswordHash = HashPassword(user, password);
        return user;
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

    public sealed record NewUserRequest(string? Username, string? Password, int OrganizationId);
    public sealed record ResetPasswordRequest(string? Password);
    public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
}
