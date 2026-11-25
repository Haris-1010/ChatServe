using System.Security.Claims;
using ChatServe.Api.Data;
using ChatServe.Api.Orders;
using Microsoft.EntityFrameworkCore;

namespace ChatServe.Api.Dashboard;

/// <summary>Restaurant profile, menu, deals, FAQs and delivery zones. The bot answers from these (new data applies from the next message).</summary>
public static class RestaurantEndpoints
{
    public static void MapRestaurantEndpoints(this RouteGroupBuilder api)
    {
        // Admin sees every restaurant; an owner only their organization's restaurants.
        api.MapGet("/restaurants", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var own = user.OwnOrganizationId();
            return await (from r in db.Restaurants.AsNoTracking()
                          join o in db.Organizations.AsNoTracking() on r.OrganizationId equals o.Id
                          where own == null || r.OrganizationId == own
                          orderby o.Name, r.Name, r.Id
                          select new
                          {
                              r.Id, r.Name, r.WhatsAppPhoneNumberId, r.IsBotEnabled,
                              r.OrganizationId, OrganizationName = o.Name,
                          })
                .ToListAsync(ct);
        });

        // Admin: add a restaurant (branch) to a client. Without an organization, a new client with the same name is created.
        api.MapPost("/restaurants", async (NewRestaurantInput input, AppDbContext db, CancellationToken ct) =>
        {
            if (await NewRestaurantProblem(db, input.Name, input.WhatsAppPhoneNumberId, ct) is { } problem)
                return Bad(problem);

            var r = new Restaurant { Name = input.Name!.Trim(), WhatsAppPhoneNumberId = input.WhatsAppPhoneNumberId!.Trim() };
            if (input.OrganizationId is { } oid)
            {
                if (!await db.Organizations.AnyAsync(o => o.Id == oid, ct))
                    return Bad("That client doesn't exist.");
                r.OrganizationId = oid;
                db.Restaurants.Add(r);
            }
            else
            {
                var org = new Organization { Name = r.Name };
                org.Restaurants.Add(r);
                db.Organizations.Add(org);
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { r.Id, r.OrganizationId });
        }).RequireAuthorization(AccessControl.AdminPolicy);

        var one = api.MapGroup("/restaurants/{rid:int}");

        one.MapGet("", async (int rid, AppDbContext db, CancellationToken ct) =>
            await db.Restaurants.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rid, ct) is { } r
                ? Results.Ok(ToProfile(r))
                : Results.NotFound());

        one.MapPut("", async (int rid, RestaurantProfile input, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var r = await db.Restaurants.FirstOrDefaultAsync(x => x.Id == rid, ct);
            if (r is null) return Results.NotFound();
            // Only the admin may move a restaurant to another WhatsApp number.
            if (!user.IsAdmin())
                input = input with { WhatsAppPhoneNumberId = r.WhatsAppPhoneNumberId };
            if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.WhatsAppPhoneNumberId))
                return Bad("Name and WhatsApp Phone number ID are required.");
            var phoneId = input.WhatsAppPhoneNumberId.Trim();
            if (await db.Restaurants.AnyAsync(x => x.Id != rid && x.WhatsAppPhoneNumberId == phoneId, ct))
                return Bad("This Phone number ID belongs to another restaurant.");
            var (staff, staffError) = StaffNumbers.Normalize(input.StaffWhatsAppNumber);
            if (staffError is not null)
                return Bad(staffError);

            r.Name = input.Name.Trim();
            r.WhatsAppPhoneNumberId = phoneId;
            r.IsBotEnabled = input.IsBotEnabled;
            r.BotTone = Clean(input.BotTone);
            r.Address = Clean(input.Address);
            r.Timings = Clean(input.Timings);
            r.DeliveryAreas = Clean(input.DeliveryAreas);
            r.MinimumOrder = input.MinimumOrder is > 0 ? input.MinimumOrder : null;
            r.PaymentMethods = Clean(input.PaymentMethods);
            r.ExtraInfo = Clean(input.ExtraInfo);
            r.Currency = string.IsNullOrWhiteSpace(input.Currency) ? "Rs" : input.Currency.Trim();
            r.StaffWhatsAppNumber = staff;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToProfile(r));
        });

        // ---- Menu ----
        one.MapGet("/menu", async (int rid, AppDbContext db, CancellationToken ct) =>
            await db.MenuItems.AsNoTracking().Where(m => m.RestaurantId == rid)
                .OrderBy(m => m.SortOrder).ThenBy(m => m.Id).ToListAsync(ct));

        one.MapPost("/menu", async (int rid, MenuItemInput input, AppDbContext db, CancellationToken ct) =>
        {
            if (await Validate(db, rid, input.Validate(), ct) is { } error) return error;
            var maxSort = await db.MenuItems.Where(m => m.RestaurantId == rid).MaxAsync(m => (int?)m.SortOrder, ct) ?? -1;
            var item = new MenuItem { RestaurantId = rid, Category = "", Name = "" };
            input.Apply(item);
            if (input.SortOrder is null) item.SortOrder = maxSort + 1;
            db.MenuItems.Add(item);
            await db.SaveChangesAsync(ct);
            return Results.Ok(item);
        });

        one.MapPut("/menu/{id:int}", async (int rid, int id, MenuItemInput input, AppDbContext db, CancellationToken ct) =>
        {
            if (input.Validate() is { } msg) return Bad(msg);
            var item = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == id && m.RestaurantId == rid, ct);
            if (item is null) return Results.NotFound();
            input.Apply(item);
            await db.SaveChangesAsync(ct);
            return Results.Ok(item);
        });

        one.MapDelete("/menu/{id:int}", async (int rid, int id, AppDbContext db, CancellationToken ct) =>
        {
            var item = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == id && m.RestaurantId == rid, ct);
            if (item is null) return Results.NotFound();
            db.MenuItems.Remove(item); // old orders keep the name/price in OrderItem
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ---- Deals ----
        one.MapGet("/deals", async (int rid, AppDbContext db, CancellationToken ct) =>
            await db.Deals.AsNoTracking().Where(d => d.RestaurantId == rid).OrderBy(d => d.Id).ToListAsync(ct));

        one.MapPost("/deals", async (int rid, DealInput input, AppDbContext db, CancellationToken ct) =>
        {
            if (await Validate(db, rid, input.Validate(), ct) is { } error) return error;
            var deal = new Deal { RestaurantId = rid, Name = "", Details = "" };
            input.Apply(deal);
            db.Deals.Add(deal);
            await db.SaveChangesAsync(ct);
            return Results.Ok(deal);
        });

        one.MapPut("/deals/{id:int}", async (int rid, int id, DealInput input, AppDbContext db, CancellationToken ct) =>
        {
            if (input.Validate() is { } msg) return Bad(msg);
            var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == id && d.RestaurantId == rid, ct);
            if (deal is null) return Results.NotFound();
            input.Apply(deal);
            await db.SaveChangesAsync(ct);
            return Results.Ok(deal);
        });

        one.MapDelete("/deals/{id:int}", async (int rid, int id, AppDbContext db, CancellationToken ct) =>
        {
            var deal = await db.Deals.FirstOrDefaultAsync(d => d.Id == id && d.RestaurantId == rid, ct);
            if (deal is null) return Results.NotFound();
            db.Deals.Remove(deal);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ---- FAQs ----
        one.MapGet("/faqs", async (int rid, AppDbContext db, CancellationToken ct) =>
            await db.Faqs.AsNoTracking().Where(f => f.RestaurantId == rid).OrderBy(f => f.Id).ToListAsync(ct));

        one.MapPost("/faqs", async (int rid, FaqInput input, AppDbContext db, CancellationToken ct) =>
        {
            if (await Validate(db, rid, input.Validate(), ct) is { } error) return error;
            var faq = new Faq { RestaurantId = rid, Question = input.Question!.Trim(), Answer = input.Answer!.Trim() };
            db.Faqs.Add(faq);
            await db.SaveChangesAsync(ct);
            return Results.Ok(faq);
        });

        one.MapPut("/faqs/{id:int}", async (int rid, int id, FaqInput input, AppDbContext db, CancellationToken ct) =>
        {
            if (input.Validate() is { } msg) return Bad(msg);
            var faq = await db.Faqs.FirstOrDefaultAsync(f => f.Id == id && f.RestaurantId == rid, ct);
            if (faq is null) return Results.NotFound();
            faq.Question = input.Question!.Trim();
            faq.Answer = input.Answer!.Trim();
            await db.SaveChangesAsync(ct);
            return Results.Ok(faq);
        });

        one.MapDelete("/faqs/{id:int}", async (int rid, int id, AppDbContext db, CancellationToken ct) =>
        {
            var faq = await db.Faqs.FirstOrDefaultAsync(f => f.Id == id && f.RestaurantId == rid, ct);
            if (faq is null) return Results.NotFound();
            db.Faqs.Remove(faq);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ---- Delivery zones ----
        one.MapGet("/zones", async (int rid, AppDbContext db, CancellationToken ct) =>
            await db.DeliveryZones.AsNoTracking().Where(z => z.RestaurantId == rid).OrderBy(z => z.Id).ToListAsync(ct));

        one.MapPost("/zones", async (int rid, ZoneInput input, AppDbContext db, CancellationToken ct) =>
        {
            if (await Validate(db, rid, input.Validate(), ct) is { } error) return error;
            var zone = new DeliveryZone { RestaurantId = rid, Name = "" };
            input.Apply(zone);
            db.DeliveryZones.Add(zone);
            await db.SaveChangesAsync(ct);
            return Results.Ok(zone);
        });

        one.MapPut("/zones/{id:int}", async (int rid, int id, ZoneInput input, AppDbContext db, CancellationToken ct) =>
        {
            if (input.Validate() is { } msg) return Bad(msg);
            var zone = await db.DeliveryZones.FirstOrDefaultAsync(z => z.Id == id && z.RestaurantId == rid, ct);
            if (zone is null) return Results.NotFound();
            input.Apply(zone);
            await db.SaveChangesAsync(ct);
            return Results.Ok(zone);
        });

        one.MapDelete("/zones/{id:int}", async (int rid, int id, AppDbContext db, CancellationToken ct) =>
        {
            var zone = await db.DeliveryZones.FirstOrDefaultAsync(z => z.Id == id && z.RestaurantId == rid, ct);
            if (zone is null) return Results.NotFound();
            db.DeliveryZones.Remove(zone);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    internal static IResult Bad(string message) => Results.Problem(message, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Checks a new restaurant's name and WhatsApp Phone number ID (must be unique: incoming messages are routed by it).</summary>
    public static async Task<string?> NewRestaurantProblem(AppDbContext db, string? name, string? phoneNumberId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(phoneNumberId))
            return "Restaurant name and WhatsApp Phone number ID are both required.";
        var phoneId = phoneNumberId.Trim();
        return await db.Restaurants.AnyAsync(r => r.WhatsAppPhoneNumberId == phoneId, ct)
            ? "A restaurant already exists for this Phone number ID."
            : null;
    }

    private static async Task<IResult?> Validate(AppDbContext db, int rid, string? message, CancellationToken ct)
    {
        if (message is not null) return Bad(message);
        return await db.Restaurants.AnyAsync(r => r.Id == rid, ct) ? null : Results.NotFound();
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static RestaurantProfile ToProfile(Restaurant r) => new(
        r.Id, r.Name, r.WhatsAppPhoneNumberId, r.IsBotEnabled, r.BotTone, r.Address, r.Timings, r.DeliveryAreas,
        r.MinimumOrder, r.PaymentMethods, r.ExtraInfo, r.Currency, r.StaffWhatsAppNumber);

    public sealed record NewRestaurantInput(string? Name, string? WhatsAppPhoneNumberId, int? OrganizationId = null);

    public sealed record RestaurantProfile(
        int Id, string? Name, string? WhatsAppPhoneNumberId, bool IsBotEnabled, string? BotTone, string? Address,
        string? Timings, string? DeliveryAreas, decimal? MinimumOrder, string? PaymentMethods, string? ExtraInfo,
        string? Currency, string? StaffWhatsAppNumber);

    public sealed record MenuItemInput(string? Category, string? Name, string? Description, decimal Price, bool IsAvailable, int? SortOrder)
    {
        public string? Validate() =>
            string.IsNullOrWhiteSpace(Category) ? "Category is required."
            : string.IsNullOrWhiteSpace(Name) ? "Dish name is required."
            : Price < 0 ? "Price can't be negative." : null;

        public void Apply(MenuItem m)
        {
            m.Category = Category!.Trim();
            m.Name = Name!.Trim();
            m.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
            m.Price = Price;
            m.IsAvailable = IsAvailable;
            if (SortOrder is { } s) m.SortOrder = s;
        }
    }

    public sealed record DealInput(string? Name, string? Details, decimal Price, bool IsActive)
    {
        public string? Validate() =>
            string.IsNullOrWhiteSpace(Name) ? "Deal name is required."
            : string.IsNullOrWhiteSpace(Details) ? "Describe what's included in the deal."
            : Price < 0 ? "Price can't be negative." : null;

        public void Apply(Deal d)
        {
            d.Name = Name!.Trim();
            d.Details = Details!.Trim();
            d.Price = Price;
            d.IsActive = IsActive;
        }
    }

    public sealed record FaqInput(string? Question, string? Answer)
    {
        public string? Validate() =>
            string.IsNullOrWhiteSpace(Question) || string.IsNullOrWhiteSpace(Answer) ? "Enter both a question and an answer." : null;
    }

    public sealed record ZoneInput(string? Name, decimal Charge, bool IsActive)
    {
        public string? Validate() =>
            string.IsNullOrWhiteSpace(Name) ? "Area name is required."
            : Charge < 0 ? "Charge can't be negative." : null;

        public void Apply(DeliveryZone z)
        {
            z.Name = Name!.Trim();
            z.Charge = Charge;
            z.IsActive = IsActive;
        }
    }
}
