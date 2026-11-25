using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ChatServe.Api.Ai;
using ChatServe.Api.Data;
using ChatServe.Api.WhatsApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChatServe.Api.Tests;

/// <summary>The dashboard admin API: login, settings (from the DB, applied immediately), menu CRUD, simulator, orders.</summary>
public class DashboardApiTests
{
    internal const string Password = "test-pass";

    internal sealed class Factory : WebApplicationFactory<Program>
    {
        public FakeWhatsAppClient WhatsApp { get; } = new();
        public FakeChatCompletionClient Ai { get; } = new();
        public FakeScreenshotReader Screenshots { get; } = new();
        private readonly string _dbName = Guid.NewGuid().ToString();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("WhatsApp:PhoneNumberId", "111222333");
            builder.UseSetting("WhatsApp:AccessToken", "secret-access-token-1234");
            builder.UseSetting("Dashboard:AdminPassword", Password);
            builder.UseSetting("ConnectionStrings:Default", "Host=unused");
            builder.ConfigureServices(s =>
            {
                s.RemoveAll<IWhatsAppClient>();
                s.AddSingleton<IWhatsAppClient>(WhatsApp);
                s.RemoveAll<IChatCompletionClient>();
                s.AddSingleton<IChatCompletionClient>(Ai);
                s.RemoveAll<IChatScreenshotReader>();
                s.AddSingleton<IChatScreenshotReader>(Screenshots);
                s.RemoveAll<DbContextOptions<AppDbContext>>();
                s.RemoveAll<AppDbContext>();
                s.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
            });
        }
    }

    internal static async Task<(Factory Factory, HttpClient Client)> LoggedInAsync()
    {
        var factory = new Factory();
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        res.EnsureSuccessStatusCode();
        var token = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (factory, client);
    }

    internal static async Task<int> RestaurantIdAsync(HttpClient c) =>
        (await c.GetFromJsonAsync<JsonElement>("/api/restaurants"))[0].GetProperty("id").GetInt32();

    [Fact]
    public async Task Admin_api_requires_login_and_rejects_wrong_password()
    {
        await using var factory = new Factory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/restaurants")).StatusCode);
        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        // the webhook works without login (for Meta)
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/webhook?hub.mode=subscribe&hub.verify_token=x&hub.challenge=1")).StatusCode);
    }

    [Fact]
    public async Task Settings_hide_secrets_and_saved_values_apply_without_restart()
    {
        var (factory, c) = await LoggedInAsync();
        await using var _ = factory;

        var settings = await c.GetFromJsonAsync<JsonElement>("/api/settings");
        var token = settings.GetProperty("settings").EnumerateArray().Single(s => s.GetProperty("key").GetString() == "WhatsApp:AccessToken");
        Assert.Equal(JsonValueKind.Null, token.GetProperty("value").ValueKind); // secrets are never returned
        Assert.Equal("••••1234", token.GetProperty("hint").GetString());
        Assert.DoesNotContain("secret-access-token", settings.GetRawText());

        // Verify token from the dashboard: the webhook accepts the new token immediately
        var save = await c.PutAsJsonAsync("/api/settings", new { values = new Dictionary<string, string?> { ["WhatsApp:VerifyToken"] = "from-dashboard" } });
        Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);
        var verify = await c.GetAsync("/webhook?hub.mode=subscribe&hub.verify_token=from-dashboard&hub.challenge=42");
        Assert.Equal("42", await verify.Content.ReadAsStringAsync());

        // Unknown setting / invalid value
        var unknown = await c.PutAsJsonAsync("/api/settings", new { values = new Dictionary<string, string?> { ["ConnectionStrings:Default"] = "x" } });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        var badNumber = await c.PutAsJsonAsync("/api/settings", new { values = new Dictionary<string, string?> { ["Anthropic:HistoryLimit"] = "abc" } });
        Assert.Equal(HttpStatusCode.BadRequest, badNumber.StatusCode);

        // null = remove the dashboard value
        await c.PutAsJsonAsync("/api/settings", new { values = new Dictionary<string, string?> { ["WhatsApp:VerifyToken"] = null } });
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/webhook?hub.mode=subscribe&hub.verify_token=from-dashboard&hub.challenge=1")).StatusCode);
    }

    [Fact]
    public async Task Simulator_switches_between_ai_and_welcome_reply_at_runtime()
    {
        var (factory, c) = await LoggedInAsync();
        await using var _ = factory;
        var rid = await RestaurantIdAsync(c);

        var ai = await (await c.PostAsJsonAsync($"/api/restaurants/{rid}/simulator", new { phone = "000123", name = "Sara", text = "Karahi kitne ki hai?" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AI: Karahi kitne ki hai?", ai.GetProperty("reply").GetString());
        Assert.Equal("000123", ai.GetProperty("phone").GetString());

        await c.PutAsJsonAsync("/api/settings", new { values = new Dictionary<string, string?> { ["Bot:UseAi"] = "false" } });
        var hello = await (await c.PostAsJsonAsync($"/api/restaurants/{rid}/simulator", new { phone = "000123", name = "Sara", text = "salam" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("salam", hello.GetProperty("reply").GetString());
        Assert.False(hello.GetProperty("usedAi").GetBoolean());

        // The simulator never sends real WhatsApp
        Assert.Empty(factory.WhatsApp.Sent);

        // The chat shows as "Test" in the dashboard
        var convs = await c.GetFromJsonAsync<JsonElement>($"/api/restaurants/{rid}/conversations");
        Assert.True(convs.EnumerateArray().Single(x => x.GetProperty("phone").GetString() == "000123").GetProperty("isSimulated").GetBoolean());
    }

    [Fact]
    public async Task Simulator_order_goes_to_orders_page_and_staff_alert_stays_in_simulator()
    {
        var (factory, c) = await LoggedInAsync();
        await using var _ = factory;
        var rid = await RestaurantIdAsync(c);
        var menu = await c.GetFromJsonAsync<JsonElement>($"/api/restaurants/{rid}/menu");
        var karahiId = menu.EnumerateArray().Single(m => m.GetProperty("name").GetString() == "Chicken Karahi (Full)").GetProperty("id").GetInt32();

        factory.Ai.ToolScript = async (_, exec) =>
        {
            var outcome = await exec.ExecuteAsync("place_order", FakeChatCompletionClient.Input(new
            {
                items = new[] { new { code = "M" + karahiId, quantity = 1, note = "" } },
                order_type = "takeaway", customer_name = "Sara", delivery_address = "", delivery_area = "",
                payment_method = "Cash", notes = "",
            }), default);
            return new AiResult(outcome.IsError ? outcome.Content : "Theek hai:", false, 100, 10, 0, 1);
        };

        var draft = await (await c.PostAsJsonAsync($"/api/restaurants/{rid}/simulator", new { phone = "000555", name = "Sara", text = "1 karahi full takeaway" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Rs 2,600", draft.GetProperty("reply").GetString());

        var confirmed = await (await c.PostAsJsonAsync($"/api/restaurants/{rid}/simulator", new { phone = "000555", name = "Sara", text = "confirm" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("is confirmed", confirmed.GetProperty("reply").GetString());
        Assert.Contains("NEW ORDER", confirmed.GetProperty("staffAlerts")[0].GetString());
        Assert.Empty(factory.WhatsApp.Sent); // the kitchen ticket stayed in the simulator

        var active = await c.GetFromJsonAsync<JsonElement>($"/api/restaurants/{rid}/orders?status=active");
        var order = active.EnumerateArray().Single();
        Assert.Equal("Confirmed", order.GetProperty("status").GetString());

        // Staff change the status: no WhatsApp for a simulator customer, only the chat log
        var id = order.GetProperty("id").GetInt32();
        var res = await c.PutAsJsonAsync($"/api/restaurants/{rid}/orders/{id}/status", new { status = "Preparing", notifyCustomer = true });
        res.EnsureSuccessStatusCode();
        Assert.Empty(factory.WhatsApp.Sent);
        var customerId = order.GetProperty("customerId").GetInt32();
        var msgs = await c.GetFromJsonAsync<JsonElement>($"/api/restaurants/{rid}/customers/{customerId}/messages");
        Assert.Contains("is being prepared", msgs.EnumerateArray().Last().GetProperty("text").GetString());

        var overview = await c.GetFromJsonAsync<JsonElement>($"/api/restaurants/{rid}/overview");
        Assert.Equal(1, overview.GetProperty("ordersToday").GetInt32());
        Assert.Equal(2600m, overview.GetProperty("revenueToday").GetDecimal());
    }

    [Fact]
    public async Task Staff_reply_goes_to_real_customer_on_whatsapp_and_pauses_bot()
    {
        var (factory, c) = await LoggedInAsync();
        await using var _ = factory;
        var rid = await RestaurantIdAsync(c);

        int customerId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customer = new Customer { RestaurantId = rid, Phone = "923001112233", Name = "Bilal" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
        }

        var res = await c.PostAsJsonAsync($"/api/restaurants/{rid}/customers/{customerId}/messages", new { text = "Ji main staff se", pauseBot = true });
        res.EnsureSuccessStatusCode();
        Assert.Contains(factory.WhatsApp.Sent, m => m.To == "923001112233" && m.Body == "Ji main staff se");
        Assert.NotEqual(JsonValueKind.Null, (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("botPausedUntil").ValueKind);

        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"/api/restaurants/{rid}/customers/{customerId}/resume", null)).StatusCode);
        var customers = await c.GetFromJsonAsync<JsonElement>($"/api/restaurants/{rid}/customers");
        Assert.Equal(JsonValueKind.Null, customers.EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == customerId).GetProperty("botPausedUntil").ValueKind);
    }

    [Fact]
    public async Task Menu_crud_with_validation()
    {
        var (factory, c) = await LoggedInAsync();
        await using var _ = factory;
        var rid = await RestaurantIdAsync(c);

        var bad = await c.PostAsJsonAsync($"/api/restaurants/{rid}/menu", new { category = "", name = "X", price = 10, isAvailable = true });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var created = await (await c.PostAsJsonAsync($"/api/restaurants/{rid}/menu",
            new { category = "BBQ", name = "Reshmi Kabab", price = 850, isAvailable = true })).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        (await c.PutAsJsonAsync($"/api/restaurants/{rid}/menu/{id}",
            new { category = "BBQ", name = "Reshmi Kabab", price = 900, isAvailable = false })).EnsureSuccessStatusCode();
        var item = (await c.GetFromJsonAsync<JsonElement>($"/api/restaurants/{rid}/menu")).EnumerateArray().Single(m => m.GetProperty("id").GetInt32() == id);
        Assert.Equal(900m, item.GetProperty("price").GetDecimal());
        Assert.False(item.GetProperty("isAvailable").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/restaurants/{rid}/menu/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/restaurants/{rid}/menu/{id}")).StatusCode);

        // Another restaurant's item can't be changed through this restaurant's URL
        var other = await (await c.PostAsJsonAsync("/api/restaurants", new { name = "Other", whatsAppPhoneNumberId = "999" })).Content.ReadFromJsonAsync<JsonElement>();
        var firstItem = (await c.GetFromJsonAsync<JsonElement>($"/api/restaurants/{rid}/menu"))[0].GetProperty("id").GetInt32();
        var cross = await c.DeleteAsync($"/api/restaurants/{other.GetProperty("id").GetInt32()}/menu/{firstItem}");
        Assert.Equal(HttpStatusCode.NotFound, cross.StatusCode);
    }
}
