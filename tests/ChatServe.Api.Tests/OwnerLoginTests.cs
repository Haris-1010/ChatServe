using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ChatServe.Api.Tests;

/// <summary>Restaurant owner logins: an owner only sees and manages their own restaurant.</summary>
public class OwnerLoginTests
{
    private const string OwnerPassword = "owner-pass-123";

    private static async Task<int> CreateRestaurantAsync(HttpClient admin, string name, string phoneId)
    {
        var res = await admin.PostAsJsonAsync("/api/restaurants", new { name, whatsAppPhoneNumberId = phoneId });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private static async Task<int> CreateOwnerAsync(HttpClient admin, string username, int restaurantId, string password = OwnerPassword)
    {
        var res = await admin.PostAsJsonAsync("/api/users", new { username, password, restaurantId });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password) =>
        await client.PostAsJsonAsync("/api/auth/login", new { username, password });

    private static async Task<HttpClient> OwnerClientAsync(DashboardApiTests.Factory factory, string username, string password = OwnerPassword)
    {
        var client = factory.CreateClient();
        var res = await LoginAsync(client, username, password);
        res.EnsureSuccessStatusCode();
        var token = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Owner_sees_and_manages_only_their_own_restaurant()
    {
        var (factory, admin) = await DashboardApiTests.LoggedInAsync();
        await using var _ = factory;
        var mine = await DashboardApiTests.RestaurantIdAsync(admin);
        var other = await CreateRestaurantAsync(admin, "Other Grill", "999888777");
        await CreateOwnerAsync(admin, "Ali.Owner", mine);

        var owner = await OwnerClientAsync(factory, "ali.owner"); // usernames are case-insensitive

        var me = await owner.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("owner", me.GetProperty("role").GetString());
        Assert.Equal(mine, me.GetProperty("restaurantId").GetInt32());

        var list = await owner.GetFromJsonAsync<JsonElement>("/api/restaurants");
        Assert.Equal(mine, list.EnumerateArray().Single().GetProperty("id").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/restaurants/{mine}/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/restaurants/{mine}/menu")).StatusCode);

        // Another restaurant's data, menu edits and chats are off limits
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"/api/restaurants/{other}/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"/api/restaurants/{other}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync($"/api/restaurants/{other}/menu",
            new { category = "X", name = "Hack", price = 1, isAvailable = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"/api/restaurants/{other}/conversations")).StatusCode);

        // Admin-only parts of the app
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/restaurants",
            new { name = "Mine too", whatsAppPhoneNumberId = "555" })).StatusCode);
    }

    [Fact]
    public async Task Owner_can_edit_their_restaurant_but_not_its_whatsapp_number()
    {
        var (factory, admin) = await DashboardApiTests.LoggedInAsync();
        await using var _ = factory;
        var mine = await DashboardApiTests.RestaurantIdAsync(admin);
        await CreateOwnerAsync(admin, "sara", mine);
        var owner = await OwnerClientAsync(factory, "sara");

        var profile = await owner.GetFromJsonAsync<JsonElement>($"/api/restaurants/{mine}");
        var originalPhoneId = profile.GetProperty("whatsAppPhoneNumberId").GetString();
        var edited = JsonSerializer.Deserialize<Dictionary<string, object?>>(profile.GetRawText())!;
        edited["name"] = "Sara's Kitchen";
        edited["whatsAppPhoneNumberId"] = "123123123";

        var res = await owner.PutAsJsonAsync($"/api/restaurants/{mine}", edited);
        res.EnsureSuccessStatusCode();
        var saved = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sara's Kitchen", saved.GetProperty("name").GetString());
        Assert.Equal(originalPhoneId, saved.GetProperty("whatsAppPhoneNumberId").GetString());
    }

    [Fact]
    public async Task Reset_or_deleted_login_stops_working_immediately()
    {
        var (factory, admin) = await DashboardApiTests.LoggedInAsync();
        await using var _ = factory;
        var mine = await DashboardApiTests.RestaurantIdAsync(admin);
        var id = await CreateOwnerAsync(admin, "bilal", mine);
        var owner = await OwnerClientAsync(factory, "bilal");
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/restaurants")).StatusCode);

        // Admin resets the password: the old token is rejected, the new password works
        (await admin.PutAsJsonAsync($"/api/users/{id}/password", new { password = "brand-new-pass" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/restaurants")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(factory.CreateClient(), "bilal", OwnerPassword)).StatusCode);
        owner = await OwnerClientAsync(factory, "bilal", "brand-new-pass");

        // Admin deletes the login
        (await admin.DeleteAsync($"/api/users/{id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/restaurants")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(factory.CreateClient(), "bilal", "brand-new-pass")).StatusCode);
    }

    [Fact]
    public async Task Owner_changes_own_password()
    {
        var (factory, admin) = await DashboardApiTests.LoggedInAsync();
        await using var _ = factory;
        await CreateOwnerAsync(admin, "hina", await DashboardApiTests.RestaurantIdAsync(admin));
        var owner = await OwnerClientAsync(factory, "hina");

        var wrong = await owner.PostAsJsonAsync("/api/me/password", new { currentPassword = "nope-nope", newPassword = "another-pass" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        (await owner.PostAsJsonAsync("/api/me/password", new { currentPassword = OwnerPassword, newPassword = "another-pass" }))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/restaurants")).StatusCode); // old token logged out
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(factory.CreateClient(), "hina", "another-pass")).StatusCode);
    }

    [Fact]
    public async Task User_creation_rules_and_admin_login_by_username()
    {
        var (factory, admin) = await DashboardApiTests.LoggedInAsync();
        await using var _ = factory;
        var rid = await DashboardApiTests.RestaurantIdAsync(admin);

        async Task<HttpStatusCode> Create(string username, string password, int restaurantId) =>
            (await admin.PostAsJsonAsync("/api/users", new { username, password, restaurantId })).StatusCode;

        Assert.Equal(HttpStatusCode.BadRequest, await Create("ok-name", "short", rid));          // password too short
        Assert.Equal(HttpStatusCode.BadRequest, await Create("admin", OwnerPassword, rid));       // reserved
        Assert.Equal(HttpStatusCode.BadRequest, await Create("bad name!", OwnerPassword, rid));   // invalid characters
        Assert.Equal(HttpStatusCode.BadRequest, await Create("nobody", OwnerPassword, 9999));     // no such restaurant
        Assert.Equal(HttpStatusCode.OK, await Create("zain", OwnerPassword, rid));
        Assert.Equal(HttpStatusCode.BadRequest, await Create("ZAIN", OwnerPassword, rid));        // duplicate

        var users = await admin.GetFromJsonAsync<JsonElement>("/api/users");
        Assert.Equal("zain", users.EnumerateArray().Single().GetProperty("username").GetString());

        // The admin can also log in with the username "admin"
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, "admin", DashboardApiTests.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, "zain", "wrong-password")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, "ghost", OwnerPassword)).StatusCode);
    }
}
