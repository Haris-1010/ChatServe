using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ChatServe.Api.Tests;

/// <summary>Clients (organizations): one login sees all of its own branches and nothing of other clients; the admin sees everything.</summary>
public class ClientTests
{
    private const string Pass = "client-pass-123";

    private static async Task<JsonElement> CreateClientAsync(HttpClient admin, object body)
    {
        var res = await admin.PostAsJsonAsync("/api/clients", body);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpClient> LoginAsync(DashboardApiTests.Factory factory, string username, string password = Pass)
    {
        var client = factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        res.EnsureSuccessStatusCode();
        var token = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Client_login_sees_all_its_branches_but_no_other_client()
    {
        var (factory, admin) = await DashboardApiTests.LoggedInAsync();
        await using var _ = factory;
        var demo = await DashboardApiTests.RestaurantIdAsync(admin); // belongs to the demo client

        // New client in one step: business + first branch + login
        var created = await CreateClientAsync(admin, new
        {
            name = "Butt Foods", restaurantName = "Butt Karahi Gulberg", whatsAppPhoneNumberId = "700001",
            username = "butt", password = Pass,
        });
        var oid = created.GetProperty("id").GetInt32();
        var gulberg = created.GetProperty("restaurantId").GetInt32();

        // Second branch for the same client
        var res = await admin.PostAsJsonAsync("/api/restaurants", new { name = "Butt Karahi DHA", whatsAppPhoneNumberId = "700002", organizationId = oid });
        res.EnsureSuccessStatusCode();
        var dha = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var owner = await LoginAsync(factory, "butt");
        var me = await owner.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("Butt Foods", me.GetProperty("organizationName").GetString());

        var mine = (await owner.GetFromJsonAsync<JsonElement>("/api/restaurants")).EnumerateArray()
            .Select(r => r.GetProperty("id").GetInt32()).Order().ToArray();
        Assert.Equal(new[] { gulberg, dha }.Order().ToArray(), mine);

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/restaurants/{gulberg}/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/restaurants/{dha}/conversations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"/api/restaurants/{demo}/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"/api/restaurants/{demo}/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/restaurants/99999/orders")).StatusCode); // unknown = same answer
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/clients")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PutAsJsonAsync($"/api/restaurants/{demo}/client", new { organizationId = oid })).StatusCode);

        // The admin sees every client with its branches and logins
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/clients");
        Assert.Equal(2, all.GetProperty("totals").GetProperty("clientCount").GetInt32());
        Assert.Equal(3, all.GetProperty("totals").GetProperty("restaurants").GetInt32());
        var butt = all.GetProperty("clients").EnumerateArray().Single(c => c.GetProperty("id").GetInt32() == oid);
        Assert.Equal(2, butt.GetProperty("restaurants").GetArrayLength());
        Assert.Equal("butt", butt.GetProperty("users")[0].GetProperty("username").GetString());
    }

    [Fact]
    public async Task Suspended_client_is_logged_out_and_cannot_log_in()
    {
        var (factory, admin) = await DashboardApiTests.LoggedInAsync();
        await using var _ = factory;
        var created = await CreateClientAsync(admin, new { name = "Late Payer", whatsAppPhoneNumberId = "800001", username = "late", password = Pass });
        var oid = created.GetProperty("id").GetInt32();
        var owner = await LoginAsync(factory, "late");
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/restaurants")).StatusCode);

        (await admin.PutAsJsonAsync($"/api/clients/{oid}", new { name = "Late Payer", isActive = false })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/restaurants")).StatusCode);
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "late", password = Pass });
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);

        (await admin.PutAsJsonAsync($"/api/clients/{oid}", new { name = "Late Payer", isActive = true })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await (await LoginAsync(factory, "late")).GetAsync("/api/restaurants")).StatusCode);
    }

    [Fact]
    public async Task New_client_is_validated_as_a_whole_and_only_empty_clients_can_be_deleted()
    {
        var (factory, admin) = await DashboardApiTests.LoggedInAsync();
        await using var _ = factory;

        // Duplicate phone number id or weak password: nothing is created
        var dupPhone = await admin.PostAsJsonAsync("/api/clients", new { name = "Dup", whatsAppPhoneNumberId = "111222333" });
        Assert.Equal(HttpStatusCode.BadRequest, dupPhone.StatusCode);
        var weak = await admin.PostAsJsonAsync("/api/clients", new { name = "Weak", whatsAppPhoneNumberId = "900001", username = "weak", password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/clients");
        Assert.Equal(1, all.GetProperty("totals").GetProperty("clientCount").GetInt32());

        // A client with a restaurant can't be deleted; an empty one can
        var withShop = (await CreateClientAsync(admin, new { name = "Has shop", whatsAppPhoneNumberId = "900002" })).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"/api/clients/{withShop}")).StatusCode);
        var empty = (await CreateClientAsync(admin, new { name = "Just a name" })).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/clients/{empty}")).StatusCode);
    }
}
