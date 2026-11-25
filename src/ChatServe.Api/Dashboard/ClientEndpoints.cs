using ChatServe.Api.Data;
using ChatServe.Api.Orders;
using Microsoft.EntityFrameworkCore;

namespace ChatServe.Api.Dashboard;

/// <summary>
/// Admin only: clients (organizations). One screen to see every client's restaurants, today's numbers and logins,
/// add a new client in one step, rename or suspend a client.
/// </summary>
public static class ClientEndpoints
{
    public static void MapClientEndpoints(this RouteGroupBuilder admin)
    {
        // Everything the admin needs on the "All clients" page, in one call.
        admin.MapGet("/clients", async (AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            var pkNow = now.ToOffset(Money.PakistanOffset);
            var dayStart = new DateTimeOffset(pkNow.Date, Money.PakistanOffset).ToUniversalTime();

            var orgs = await db.Organizations.AsNoTracking().OrderBy(o => o.Name).ToListAsync(ct);
            var restaurants = await db.Restaurants.AsNoTracking().OrderBy(r => r.Name)
                .Select(r => new { r.Id, r.OrganizationId, r.Name, r.WhatsAppPhoneNumberId, r.IsBotEnabled })
                .ToListAsync(ct);
            var users = await db.DashboardUsers.AsNoTracking().OrderBy(u => u.Username)
                .Select(u => new { u.Id, u.OrganizationId, u.Username, u.LastLoginAt })
                .ToListAsync(ct);

            var ordersToday = await db.Orders.AsNoTracking()
                .Where(o => o.ConfirmedAt >= dayStart && o.Status != OrderStatus.Cancelled)
                .GroupBy(o => o.RestaurantId)
                .Select(g => new { RestaurantId = g.Key, Count = g.Count(), Sales = g.Sum(o => o.Total) })
                .ToDictionaryAsync(x => x.RestaurantId, ct);
            var activeOrders = await db.Orders.AsNoTracking()
                .Where(o => o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Preparing || o.Status == OrderStatus.OutForDelivery)
                .GroupBy(o => o.RestaurantId)
                .Select(g => new { RestaurantId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RestaurantId, x => x.Count, ct);
            var chats = await db.Customers.AsNoTracking()
                .GroupBy(c => c.RestaurantId)
                .Select(g => new
                {
                    RestaurantId = g.Key,
                    Today = g.Count(c => c.LastMessageAt >= dayStart),
                    Last = g.Max(c => (DateTimeOffset?)c.LastMessageAt),
                })
                .ToDictionaryAsync(x => x.RestaurantId, ct);

            var list = orgs.Select(o =>
            {
                var rs = restaurants.Where(r => r.OrganizationId == o.Id).Select(r => new
                {
                    r.Id,
                    r.Name,
                    r.WhatsAppPhoneNumberId,
                    r.IsBotEnabled,
                    OrdersToday = ordersToday.TryGetValue(r.Id, out var t) ? t.Count : 0,
                    SalesToday = ordersToday.TryGetValue(r.Id, out var s) ? s.Sales : 0m,
                    ActiveOrders = activeOrders.GetValueOrDefault(r.Id),
                    ChatsToday = chats.TryGetValue(r.Id, out var c) ? c.Today : 0,
                    LastMessageAt = chats.TryGetValue(r.Id, out var l) ? l.Last : null,
                }).ToList();
                return new
                {
                    o.Id,
                    o.Name,
                    o.IsActive,
                    o.CreatedAt,
                    OrdersToday = rs.Sum(r => r.OrdersToday),
                    SalesToday = rs.Sum(r => r.SalesToday),
                    ChatsToday = rs.Sum(r => r.ChatsToday),
                    Restaurants = rs,
                    Users = users.Where(u => u.OrganizationId == o.Id).Select(u => new { u.Id, u.Username, u.LastLoginAt }).ToList(),
                };
            }).ToList();

            return Results.Ok(new
            {
                Totals = new
                {
                    ClientCount = list.Count,
                    ActiveClients = list.Count(o => o.IsActive),
                    Restaurants = restaurants.Count,
                    OrdersToday = list.Sum(o => o.OrdersToday),
                    SalesToday = list.Sum(o => o.SalesToday),
                    ChatsToday = list.Sum(o => o.ChatsToday),
                },
                Clients = list,
            });
        });

        // New client in one step: the business, optionally its first restaurant and its owner login.
        admin.MapPost("/clients", async (NewClientRequest req, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > 200)
                return Bad("Enter the client's business name.");

            var org = new Organization { Name = req.Name.Trim(), CreatedAt = clock.GetUtcNow() };

            var wantsRestaurant = !string.IsNullOrWhiteSpace(req.WhatsAppPhoneNumberId) || !string.IsNullOrWhiteSpace(req.RestaurantName);
            if (wantsRestaurant)
            {
                var restaurantName = string.IsNullOrWhiteSpace(req.RestaurantName) ? org.Name : req.RestaurantName.Trim();
                if (await RestaurantEndpoints.NewRestaurantProblem(db, restaurantName, req.WhatsAppPhoneNumberId, ct) is { } problem)
                    return Bad(problem);
                org.Restaurants.Add(new Restaurant { Name = restaurantName, WhatsAppPhoneNumberId = req.WhatsAppPhoneNumberId!.Trim() });
            }

            DashboardUser? login = null;
            if (!string.IsNullOrWhiteSpace(req.Username) || !string.IsNullOrEmpty(req.Password))
            {
                if (await UserEndpoints.NewUserProblem(db, req.Username, req.Password, ct) is { } problem)
                    return Bad(problem);
                login = UserEndpoints.NewUser(req.Username, req.Password!, clock);
            }

            db.Organizations.Add(org);
            await db.SaveChangesAsync(ct);
            if (login is not null)
            {
                login.OrganizationId = org.Id;
                db.DashboardUsers.Add(login);
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(new { org.Id, RestaurantId = org.Restaurants.FirstOrDefault()?.Id, Username = login?.Username });
        });

        // Rename, or suspend / reactivate (suspended clients can't log in; their open sessions stop working too).
        admin.MapPut("/clients/{id:int}", async (int id, UpdateClientRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (org is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > 200)
                return Bad("Enter the client's business name.");
            org.Name = req.Name.Trim();
            org.IsActive = req.IsActive;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Only an empty client (no restaurants) can be deleted, so no orders or chats are ever lost by a misclick.
        admin.MapDelete("/clients/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (org is null) return Results.NotFound();
            if (await db.Restaurants.AnyAsync(r => r.OrganizationId == id, ct))
                return Bad("This client still has restaurants. Move or remove them first, or suspend the client instead.");
            db.DashboardUsers.RemoveRange(db.DashboardUsers.Where(u => u.OrganizationId == id));
            db.Organizations.Remove(org);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Move a restaurant to another client (e.g. a branch added under the wrong client).
        admin.MapPut("/restaurants/{rid:int}/client", async (int rid, MoveRestaurantRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var r = await db.Restaurants.FirstOrDefaultAsync(x => x.Id == rid, ct);
            if (r is null) return Results.NotFound();
            if (!await db.Organizations.AnyAsync(o => o.Id == req.OrganizationId, ct))
                return Bad("That client doesn't exist.");
            r.OrganizationId = req.OrganizationId;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static IResult Bad(string message) => Results.Problem(message, statusCode: StatusCodes.Status400BadRequest);

    public sealed record NewClientRequest(
        string? Name, string? RestaurantName = null, string? WhatsAppPhoneNumberId = null,
        string? Username = null, string? Password = null);

    public sealed record UpdateClientRequest(string? Name, bool IsActive);
    public sealed record MoveRestaurantRequest(int OrganizationId);
}
