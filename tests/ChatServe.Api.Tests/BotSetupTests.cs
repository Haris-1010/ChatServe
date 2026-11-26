using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChatServe.Api.Ai;
using ChatServe.Api.Data;
using ChatServe.Api.Orders;

namespace ChatServe.Api.Tests;

/// <summary>Bot setup from the portal: business type, the owner's rules, welcome message, corrections.</summary>
public class BotSetupTests
{
    private static Restaurant Shop() => new()
    {
        Name = "Zara Lawn", WhatsAppPhoneNumberId = "1", BusinessType = BusinessType.Shop,
        BusinessDescription = "Ladies clothing brand, Lahore",
    };

    [Fact]
    public void A_shop_talks_about_products_not_dishes_or_tables()
    {
        var r = Shop();
        r.MenuItems.Add(new MenuItem { Id = 1, Category = "Lawn", Name = "3-piece suit", Price = 4500 });
        var prompt = SystemPromptBuilder.Build(r);

        Assert.Contains("\"Zara Lawn\" (Ladies clothing brand, Lahore)", prompt);
        Assert.Contains("# Products", prompt);
        Assert.DoesNotContain("dish", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kitchen", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Table booking", prompt);
        Assert.DoesNotContain("dine_in", string.Concat(RestaurantTools.Build(r).Select(t => t.JsonSchema)));
    }

    [Fact]
    public void A_restaurant_keeps_menu_table_bookings_and_dine_in()
    {
        var r = new Restaurant { Name = "R", WhatsAppPhoneNumberId = "1" };
        var prompt = SystemPromptBuilder.Build(r);
        Assert.Contains("# Menu", prompt);
        Assert.Contains("Table booking", prompt);
        Assert.Contains("dine_in", RestaurantTools.Build(r).Single(t => t.Name == RestaurantTools.PlaceOrder).JsonSchema);
    }

    [Fact]
    public void Orders_and_bookings_can_be_switched_off()
    {
        var r = Shop();
        r.TakesOrders = false;
        r.TakesBookings = false;
        var tools = RestaurantTools.Build(r).Select(t => t.Name).ToList();

        Assert.DoesNotContain(RestaurantTools.PlaceOrder, tools);
        Assert.DoesNotContain(RestaurantTools.BookTable, tools);
        Assert.Contains(RestaurantTools.HandoverToHuman, tools);
        Assert.Contains("You don't take orders in this chat", SystemPromptBuilder.Build(r));
    }

    [Fact]
    public void Owner_rules_and_welcome_go_into_the_prompt_before_the_default_style()
    {
        var r = Shop();
        r.BotInstructions = "- Always ask for size and colour";
        r.WelcomeMessage = "Assalam o alaikum! Zara Lawn mein khush aamdeed";
        var prompt = SystemPromptBuilder.Build(r);

        Assert.Contains("<owner_rules>\n- Always ask for size and colour\n</owner_rules>", prompt);
        Assert.True(prompt.IndexOf("<owner_rules>", StringComparison.Ordinal) < prompt.IndexOf("# How to reply", StringComparison.Ordinal));
        Assert.Contains("Zara Lawn mein khush aamdeed", prompt);
        Assert.DoesNotContain("<owner_rules>", SystemPromptBuilder.Build(Shop()));
    }

    [Fact]
    public void A_correction_becomes_one_rule_line()
    {
        var rule = BotRules.Correction("order kro bhai", "Sorry, we're having\ntrouble.\n\n[Choices shown: 1 | 2]", "Zaroor! Kitne plate chahiye?");
        Assert.Equal("- When a customer says something like \"order kro bhai\", reply like: \"Zaroor! Kitne plate chahiye?\" (not: \"Sorry, we're having trouble.\")", rule);
        Assert.Null(BotRules.Correction("x", "y", "  "));
    }

    [Fact]
    public async Task Owner_saves_rules_and_corrections_and_business_type_from_the_dashboard()
    {
        var (factory, c) = await DashboardApiTests.LoggedInAsync();
        await using var _ = factory;
        var rid = await DashboardApiTests.RestaurantIdAsync(c);

        (await c.PutAsJsonAsync($"/api/restaurants/{rid}/bot-rules",
            new { instructions = "- Keep it short\n\n\n\n- Say sir", welcomeMessage = " Hi! " })).EnsureSuccessStatusCode();
        var rules = await c.GetFromJsonAsync<JsonElement>($"/api/restaurants/{rid}/bot-rules");
        Assert.Equal("- Keep it short\n\n- Say sir", rules.GetProperty("instructions").GetString());
        Assert.Equal("Hi!", rules.GetProperty("welcomeMessage").GetString());

        var fixedOne = await c.PostAsJsonAsync($"/api/restaurants/{rid}/bot-rules/correction",
            new { customerMessage = "menu", botReply = "Sorry", betterReply = "Ye raha menu 👇" });
        fixedOne.EnsureSuccessStatusCode();
        var after = await fixedOne.Content.ReadFromJsonAsync<JsonElement>();
        Assert.EndsWith("reply like: \"Ye raha menu 👇\" (not: \"Sorry\")", after.GetProperty("instructions").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/restaurants/{rid}/bot-rules/correction", new { betterReply = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync($"/api/restaurants/{rid}/bot-rules",
            new { instructions = new string('a', BotRules.MaxLength + 1) })).StatusCode);

        // Business type: an older dashboard that doesn't send it keeps orders on.
        var profile = await c.GetFromJsonAsync<JsonElement>($"/api/restaurants/{rid}");
        Assert.Equal("Restaurant", profile.GetProperty("businessType").GetString());
        Assert.True(profile.GetProperty("takesOrders").GetBoolean());

        var update = JsonSerializer.Deserialize<Dictionary<string, object?>>(profile.GetRawText())!;
        update["businessType"] = "Services";
        update["takesOrders"] = false;
        (await c.PutAsJsonAsync($"/api/restaurants/{rid}", update)).EnsureSuccessStatusCode();
        var saved = await c.GetFromJsonAsync<JsonElement>($"/api/restaurants/{rid}");
        Assert.Equal("Services", saved.GetProperty("businessType").GetString());
        Assert.False(saved.GetProperty("takesOrders").GetBoolean());
        Assert.True(saved.GetProperty("takesBookings").GetBoolean());
    }
}
