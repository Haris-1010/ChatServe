using ChatServe.Api.Ai;
using ChatServe.Api.Data;

namespace ChatServe.Api.Tests;

public class SystemPromptBuilderTests
{
    private static Restaurant Sample()
    {
        var r = new Restaurant
        {
            Name = "Test Karahi", WhatsAppPhoneNumberId = "1", Timings = "12 PM - 2 AM",
            DeliveryAreas = "40 minute lagte hain", MinimumOrder = 1000, PaymentMethods = "Cash",
        };
        r.MenuItems.Add(new MenuItem { Id = 1, Category = "Karahi", Name = "Chicken Karahi (Full)", Price = 2600, SortOrder = 0 });
        r.MenuItems.Add(new MenuItem { Id = 2, Category = "BBQ", Name = "Mutton Chops", Price = 1800, IsAvailable = false, SortOrder = 1 });
        r.Deals.Add(new Deal { Id = 1, Name = "Family Deal", Details = "Karahi + Naan", Price = 3300 });
        r.Deals.Add(new Deal { Id = 2, Name = "Old Deal", Details = "x", Price = 1, IsActive = false });
        r.Faqs.Add(new Faq { Id = 1, Question = "Halal?", Answer = "Ji haan" });
        r.DeliveryZones.Add(new DeliveryZone { Id = 1, Name = "Johar Town", Charge = 100 });
        r.DeliveryZones.Add(new DeliveryZone { Id = 2, Name = "Bahria", Charge = 500, IsActive = false });
        return r;
    }

    [Fact]
    public void Contains_menu_prices_deals_faqs_and_settings()
    {
        var prompt = SystemPromptBuilder.Build(Sample());

        Assert.Contains("\"Test Karahi\"", prompt);
        Assert.Contains("- [M1] Chicken Karahi (Full): Rs 2,600", prompt);
        Assert.Contains("- [M2] Mutton Chops: Rs 1,800 [NOT AVAILABLE]", prompt);
        Assert.Contains("- [D1] Family Deal: Rs 3,300 (Karahi + Naan)", prompt);
        Assert.DoesNotContain("Old Deal", prompt);
        Assert.Contains("Q: Halal?", prompt);
        Assert.Contains("Minimum order for delivery: Rs 1,000", prompt);
        Assert.Contains("- Delivery areas: Johar Town (Rs 100)", prompt);
        Assert.DoesNotContain("Bahria", prompt);
        Assert.Contains("- Delivery notes: 40 minute lagte hain", prompt);
    }

    [Fact]
    public void Is_deterministic_for_prompt_caching() =>
        Assert.Equal(SystemPromptBuilder.Build(Sample()), SystemPromptBuilder.Build(Sample()));
}
