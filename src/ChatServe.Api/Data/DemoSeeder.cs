using ChatServe.Api.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Data;

/// <summary>
/// Demo restaurant "Talha's Karahi" banata hai (agar pehle se na ho), configured PhoneNumberId ke saath.
/// Isi par restaurants ko demo dikhana hai. Dashboard (Phase 5) aane tak menu yahin se aata hai.
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
            // Phase 2 mein bana demo restaurant: Phase 3 wali cheezein (zones, staff number) add kar do.
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
            BotTone = "Dostana, izzat se, mukhtasar. Lahori mehmaan nawazi wala andaaz.",
            Address = "Main Boulevard, Johar Town, Lahore",
            Timings = "Rozana dopehar 12 baje se raat 2 baje tak. Jumma ko 2 baje se.",
            DeliveryAreas = DemoDeliveryNotes,
            DeliveryZones = DemoZones(),
            StaffWhatsAppNumber = string.IsNullOrWhiteSpace(staffWhatsAppNumber) ? null : staffWhatsAppNumber,
            MinimumOrder = 1000,
            PaymentMethods = "Cash on delivery, JazzCash, Easypaisa, bank transfer",
            ExtraInfo = "Dine-in aur takeaway dono available hain. Family hall maujood hai.",
        };

        var sort = 0;
        void Add(string cat, string name, decimal price, string? desc = null, bool available = true) =>
            r.MenuItems.Add(new MenuItem { Category = cat, Name = name, Price = price, Description = desc, IsAvailable = available, SortOrder = sort++ });

        Add("Karahi", "Chicken Karahi (Half)", 1400, "Desi ghee, tamatar wali");
        Add("Karahi", "Chicken Karahi (Full)", 2600, "Desi ghee, tamatar wali");
        Add("Karahi", "Mutton Karahi (Half)", 2800);
        Add("Karahi", "Mutton Karahi (Full)", 5400);
        Add("Karahi", "Chicken White Karahi (Full)", 2900, "Dahi aur cream wali");
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

        r.Faqs.Add(new Faq { Question = "Kya khana halal hai?", Answer = "Ji, saara gosht 100% halal hai." });
        r.Faqs.Add(new Faq { Question = "Table booking hoti hai?", Answer = "Ji, WhatsApp par naam, waqt aur afraad ki tadaad bata dein." });
        r.Faqs.Add(new Faq { Question = "Spicy kam ho sakta hai?", Answer = "Ji, order ke waqt bata dein, mirch kam ya zyada kar denge." });

        db.Restaurants.Add(r);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo restaurant {Name} for phone number id {Id}", r.Name, phoneNumberId);
    }

    private const string DemoDeliveryNotes = "Delivery mein aam tor par 40 se 50 minute lagte hain.";

    private static List<DeliveryZone> DemoZones() =>
    [
        new() { Name = "Johar Town", Charge = 100 },
        new() { Name = "Wapda Town", Charge = 150 },
        new() { Name = "Model Town", Charge = 200 },
        new() { Name = "DHA Phase 1-6", Charge = 250 },
        new() { Name = "Gulberg", Charge = 250 },
    ];
}
