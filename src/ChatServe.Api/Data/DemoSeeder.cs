using ChatServe.Api.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Data;

/// <summary>
/// Creates the demo restaurant "Talha's Karahi" (if it doesn't exist) for the configured PhoneNumberId.
/// Used for demos to restaurants. Until the dashboard (Phase 5), the menu came from here.
/// </summary>
public static class DemoSeeder
{
    public static async Task SeedAsync(
        AppDbContext db, IOptions<WhatsAppOptions> wa, ILogger logger, string? staffWhatsAppNumber = null, CancellationToken ct = default)
    {
        var phoneNumberId = wa.Value.PhoneNumberId;
        if (string.IsNullOrWhiteSpace(phoneNumberId))
            return;

        var existing = await db.Restaurants.Include(r => r.DeliveryZones)
            .FirstOrDefaultAsync(r => r.WhatsAppPhoneNumberId == phoneNumberId, ct);
        if (existing is not null)
        {
            // Demo restaurant created in Phase 2: add the Phase 3 parts (zones, staff number).
            if (existing.DeliveryZones.Count == 0)
            {
                existing.DeliveryZones.AddRange(DemoZones());
                existing.DeliveryAreas = DemoDeliveryNotes;
            }
            if (string.IsNullOrWhiteSpace(existing.StaffWhatsAppNumber) && !string.IsNullOrWhiteSpace(staffWhatsAppNumber))
                existing.StaffWhatsAppNumber = staffWhatsAppNumber;
            await db.SaveChangesAsync(ct);
            return;
        }

        var r = new Restaurant
        {
            Name = "Talha's Karahi",
            WhatsAppPhoneNumberId = phoneNumberId,
            BotTone = "Friendly, polite and brief, with warm Lahori hospitality.",
            Address = "Main Boulevard, Johar Town, Lahore",
            Timings = "Daily from 12 PM to 2 AM. Fridays from 2 PM.",
            DeliveryAreas = DemoDeliveryNotes,
            DeliveryZones = DemoZones(),
            StaffWhatsAppNumber = string.IsNullOrWhiteSpace(staffWhatsAppNumber) ? null : staffWhatsAppNumber,
            MinimumOrder = 1000,
            PaymentMethods = "Cash on delivery, JazzCash, Easypaisa, bank transfer",
            ExtraInfo = "Both dine-in and takeaway are available. We have a family hall.",
        };

        var sort = 0;
        void Add(string cat, string name, decimal price, string? desc = null, bool available = true) =>
            r.MenuItems.Add(new MenuItem { Category = cat, Name = name, Price = price, Description = desc, IsAvailable = available, SortOrder = sort++ });

        Add("Karahi", "Chicken Karahi (Half)", 1400, "Desi ghee, tomato based");
        Add("Karahi", "Chicken Karahi (Full)", 2600, "Desi ghee, tomato based");
        Add("Karahi", "Mutton Karahi (Half)", 2800);
        Add("Karahi", "Mutton Karahi (Full)", 5400);
        Add("Karahi", "Chicken White Karahi (Full)", 2900, "With yogurt and cream");
        Add("BBQ", "Chicken Tikka (Leg/Chest)", 450);
        Add("BBQ", "Seekh Kabab (4 pcs)", 700);
        Add("BBQ", "Chicken Malai Boti (10 pcs)", 950);
        Add("BBQ", "Mutton Chops (4 pcs)", 1800, available: false);
        Add("Rice", "Chicken Pulao", 550);
        Add("Rice", "Chicken Biryani", 600);
        Add("Bread", "Roti", 30);
        Add("Bread", "Naan", 60);
        Add("Bread", "Garlic Naan", 120);
        Add("Drinks", "Soft Drink 1.5L", 250);
        Add("Drinks", "Mint Margarita", 350);
        Add("Dessert", "Kheer", 250);

        r.Deals.Add(new Deal { Name = "Family Deal", Details = "Chicken Karahi Full + 4 Naan + Raita + 1.5L Drink", Price = 3300 });
        r.Deals.Add(new Deal { Name = "BBQ Platter", Details = "2 Tikka + 4 Seekh Kabab + 10 Malai Boti + 4 Naan", Price = 2900 });

        r.Faqs.Add(new Faq { Question = "Is the food halal?", Answer = "Yes, all our meat is 100% halal." });
        r.Faqs.Add(new Faq { Question = "Do you take table bookings?", Answer = "Yes, just send your name, time and number of people on WhatsApp." });
        r.Faqs.Add(new Faq { Question = "Can you make it less spicy?", Answer = "Yes, tell us when ordering and we'll adjust the spice level." });

        // Every restaurant belongs to an organization (the client). The demo gets its own.
        var org = new Organization { Name = r.Name };
        org.Restaurants.Add(r);
        db.Organizations.Add(org);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo restaurant {Name} for phone number id {Id}", r.Name, phoneNumberId);
    }

    private const string DemoDeliveryNotes = "Delivery usually takes 40 to 50 minutes.";

    private static List<DeliveryZone> DemoZones() =>
    [
        new() { Name = "Johar Town", Charge = 100 },
        new() { Name = "Wapda Town", Charge = 150 },
        new() { Name = "Model Town", Charge = 200 },
        new() { Name = "DHA Phase 1-6", Charge = 250 },
        new() { Name = "Gulberg", Charge = 250 },
    ];
}
