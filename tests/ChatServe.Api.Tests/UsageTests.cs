using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ChatServe.Api.Ai;
using ChatServe.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace ChatServe.Api.Tests;

public class UsageTests
{
    private const string Pass = "owner-pass-123";

    private static async Task AddReplyAsync(DashboardApiTests.Factory f, int rid, int input, int output, int cacheRead = 0, int cacheWrite = 0)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customer = new Customer { RestaurantId = rid, Phone = "92300" + Random.Shared.Next(1000000, 9999999), FirstSeenAt = DateTimeOffset.UtcNow };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        db.ChatMessages.Add(new ChatMessage
        {
            RestaurantId = rid, CustomerId = customer.Id, Role = ChatRole.Bot, Text = "hi",
            InputTokens = input, OutputTokens = output, CacheReadTokens = cacheRead, CacheWriteTokens = cacheWrite,
        });
        // A staff message without tokens doesn't count
        db.ChatMessages.Add(new ChatMessage { RestaurantId = rid, CustomerId = customer.Id, Role = ChatRole.Staff, Text = "ok" });
        await db.SaveChangesAsync();
    }

    private static JsonElement Client(JsonElement report, string name) =>
        report.GetProperty("clients").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);

    [Fact]
    public void Cost_uses_all_four_token_prices()
    {
        var p = new UsageOptions(); // claude-haiku-5-5 defaults
        // 1M in ($0.10) + 1M out ($0.50) + 1M cache read ($0.01) + 1M cache write ($0.125)
        Assert.Equal(0.735m, p.CostUsd(1_000_000, 1_000_000, 1_000_000, 1_000_000));
    }

    [Fact]
    public async Task Admin_sees_every_client_owner_only_their_own_with_limit_warning()
    {
        var (factory, admin) = await DashboardApiTests.LoggedInAsync();
        await using var _ = factory;
        var demo = await DashboardApiTests.RestaurantIdAsync(admin);

        var created = await (await admin.PostAsJsonAsync("/api/clients", new
        {
            name = "Butt Foods", restaurantName = "Butt Karahi", whatsAppPhoneNumberId = "800001", username = "butt", password = Pass,
        })).Content.ReadFromJsonAsync<JsonElement>();
        var oid = created.GetProperty("id").GetInt32();
        var butt = created.GetProperty("restaurantId").GetInt32();

        // Butt: 2M input + 400K output = $0.20 + $0.20 = $0.40 = Rs 112 at 280
        await AddReplyAsync(factory, butt, 1_000_000, 200_000);
        await AddReplyAsync(factory, butt, 1_000_000, 200_000);
        await AddReplyAsync(factory, demo, 500_000, 0);

        // Package: 2 replies a month → 2 used = 100%
        (await admin.PutAsJsonAsync($"/api/clients/{oid}/limit", new { monthlyReplyLimit = 2 })).EnsureSuccessStatusCode();

        var all = await admin.GetFromJsonAsync<JsonElement>("/api/usage");
        Assert.Equal(2, all.GetProperty("clients").GetArrayLength());
        var b = Client(all, "Butt Foods");
        Assert.Equal(2, b.GetProperty("usage").GetProperty("replies").GetInt32());
        Assert.Equal(112m, b.GetProperty("usage").GetProperty("costPkr").GetDecimal());
        Assert.Equal(100, b.GetProperty("percent").GetInt32());
        Assert.True(b.GetProperty("overLimit").GetBoolean());
        Assert.Equal(0, b.GetProperty("remainingReplies").GetInt32());

        (await admin.PutAsJsonAsync($"/api/clients/{oid}/limit", new { monthlyReplyLimit = 10 })).EnsureSuccessStatusCode();
        b = Client(await admin.GetFromJsonAsync<JsonElement>("/api/usage"), "Butt Foods");
        Assert.Equal(20, b.GetProperty("percent").GetInt32());
        Assert.False(b.GetProperty("warning").GetBoolean());
        Assert.Equal(8, b.GetProperty("remainingReplies").GetInt32());

        // The owner sees only their own client, and can't change the limit
        var owner = factory.CreateClient();
        var login = await owner.PostAsJsonAsync("/api/auth/login", new { username = "butt", password = Pass });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var mine = await owner.GetFromJsonAsync<JsonElement>("/api/usage");
        var mineClient = Assert.Single(mine.GetProperty("clients").EnumerateArray());
        Assert.Equal("Butt Foods", mineClient.GetProperty("name").GetString());
        // The owner sees replies used and left, but not our tokens and cost
        Assert.False(mine.GetProperty("showCost").GetBoolean());
        Assert.Equal(2, mineClient.GetProperty("usage").GetProperty("replies").GetInt32());
        Assert.Equal(8, mineClient.GetProperty("remainingReplies").GetInt32());
        Assert.Equal(0m, mineClient.GetProperty("usage").GetProperty("costPkr").GetDecimal());
        Assert.Equal(0, mineClient.GetProperty("totalTokens").GetInt64());
        Assert.Equal(0, mine.GetProperty("total").GetProperty("inputTokens").GetInt64());

        // The admin gets the detail: tokens per reply, share of the cost, and day by day
        var detail = Client(await admin.GetFromJsonAsync<JsonElement>("/api/usage"), "Butt Foods");
        Assert.Equal(1_200_000, detail.GetProperty("avgTokensPerReply").GetInt64());
        Assert.Equal(89, detail.GetProperty("sharePercent").GetInt32()); // $0.40 of $0.45
        var today = detail.GetProperty("daily").EnumerateArray().Last();
        Assert.Equal(2, today.GetProperty("usage").GetProperty("replies").GetInt32());

        // The owner's Home page shows the replies left too
        var home = await owner.GetFromJsonAsync<JsonElement>($"/api/restaurants/{butt}/overview");
        Assert.Equal(2, home.GetProperty("repliesThisMonth").GetInt32());
        Assert.Equal(8, home.GetProperty("repliesLeft").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PutAsJsonAsync($"/api/clients/{oid}/limit", new { monthlyReplyLimit = 99999 })).StatusCode);

        // Another month has nothing
        var old = await admin.GetFromJsonAsync<JsonElement>("/api/usage?month=2020-01");
        Assert.Equal(0, old.GetProperty("total").GetProperty("replies").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/usage?month=nov")).StatusCode);
    }

    [Fact]
    public async Task Reading_a_training_screenshot_counts_as_usage()
    {
        var (factory, admin) = await DashboardApiTests.LoggedInAsync();
        await using var _ = factory;
        var rid = await DashboardApiTests.RestaurantIdAsync(admin);

        (await admin.PostAsJsonAsync($"/api/restaurants/{rid}/training/import",
            new { fileName = "chat.png", imageBase64 = Convert.ToBase64String([1, 2, 3]), mediaType = "image/png" })).EnsureSuccessStatusCode();

        var total = (await admin.GetFromJsonAsync<JsonElement>("/api/usage")).GetProperty("total");
        Assert.Equal(1500, total.GetProperty("inputTokens").GetInt64());
        Assert.Equal(0, total.GetProperty("replies").GetInt32()); // not a customer reply
    }

    [Fact]
    public async Task Credit_left_is_the_entered_balance_minus_use_since_then()
    {
        var (factory, admin) = await DashboardApiTests.LoggedInAsync();
        await using var _ = factory;
        var demo = await DashboardApiTests.RestaurantIdAsync(admin);

        // Use before the balance was entered doesn't count against it
        await AddReplyAsync(factory, demo, 1_000_000, 0);
        Assert.Equal(JsonValueKind.Null, (await admin.GetFromJsonAsync<JsonElement>("/api/usage")).GetProperty("credit").ValueKind);

        (await admin.PutAsJsonAsync("/api/usage/credit", new { balanceUsd = 5m })).EnsureSuccessStatusCode();
        // 2 replies: 2M input + 400K output = $0.40
        await AddReplyAsync(factory, demo, 1_000_000, 200_000);
        await AddReplyAsync(factory, demo, 1_000_000, 200_000);

        var c = (await admin.GetFromJsonAsync<JsonElement>("/api/usage")).GetProperty("credit");
        Assert.Equal(5m, c.GetProperty("balanceUsd").GetDecimal());
        Assert.Equal(0.40m, c.GetProperty("spentUsd").GetDecimal());
        Assert.Equal(4.60m, c.GetProperty("remainingUsd").GetDecimal());
        Assert.Equal(92, c.GetProperty("percent").GetInt32());
        Assert.Equal(23, c.GetProperty("repliesLeft").GetInt64()); // $0.20 a reply
        Assert.Equal(27_600_000, c.GetProperty("tokensLeft").GetInt64()); // 2.4M tokens for $0.40

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/usage/credit", new { balanceUsd = -1m })).StatusCode);
        (await admin.PutAsJsonAsync("/api/usage/credit", new { balanceUsd = (decimal?)null })).EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await admin.GetFromJsonAsync<JsonElement>("/api/usage")).GetProperty("credit").ValueKind);
    }

    [Fact]
    public async Task Client_requests_more_replies_and_admin_approves_or_tops_up()
    {
        var (factory, admin) = await DashboardApiTests.LoggedInAsync();
        await using var _ = factory;
        var created = await (await admin.PostAsJsonAsync("/api/clients", new
        {
            name = "Butt Foods", restaurantName = "Butt Karahi", whatsAppPhoneNumberId = "800002", username = "butt2", password = Pass,
        })).Content.ReadFromJsonAsync<JsonElement>();
        var oid = created.GetProperty("id").GetInt32();
        var rid = created.GetProperty("restaurantId").GetInt32();
        (await admin.PutAsJsonAsync($"/api/clients/{oid}/limit", new { monthlyReplyLimit = 100 })).EnsureSuccessStatusCode();

        var owner = factory.CreateClient();
        var login = await owner.PostAsJsonAsync("/api/auth/login", new { username = "butt2", password = Pass });
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());

        // A top-up for this month: one open request at a time
        (await owner.PostAsJsonAsync("/api/reply-requests", new { replies = 50, kind = "TopUp", note = "Eid rush" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/reply-requests", new { replies = 10, kind = "TopUp" })).StatusCode);
        var pending = (await admin.GetFromJsonAsync<JsonElement>("/api/reply-requests")).EnumerateArray().Single();
        Assert.Equal("Pending", pending.GetProperty("status").GetString());
        Assert.Equal("Butt Foods", pending.GetProperty("clientName").GetString());
        var reqId = pending.GetProperty("id").GetInt32();

        // Owners can't approve their own request
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync($"/api/reply-requests/{reqId}/approve", new { })).StatusCode);

        // The admin approves 40 instead of 50: 100 package + 40 this month
        (await admin.PostAsJsonAsync($"/api/reply-requests/{reqId}/approve", new { replies = 40, adminNote = "40 for now" })).EnsureSuccessStatusCode();
        var mine = Assert.Single((await owner.GetFromJsonAsync<JsonElement>("/api/usage")).GetProperty("clients").EnumerateArray());
        Assert.Equal(140, mine.GetProperty("monthlyReplyLimit").GetInt32());
        Assert.Equal(100, mine.GetProperty("packageReplies").GetInt32());
        Assert.Equal(40, mine.GetProperty("extraReplies").GetInt32());
        Assert.Equal(140, (await owner.GetFromJsonAsync<JsonElement>($"/api/restaurants/{rid}/overview")).GetProperty("monthlyReplyLimit").GetInt32());
        var answered = (await owner.GetFromJsonAsync<JsonElement>("/api/reply-requests")).EnumerateArray().Single();
        Assert.Equal("Approved", answered.GetProperty("status").GetString());
        Assert.Equal(40, answered.GetProperty("approvedReplies").GetInt32());

        // A bigger package, rejected; then the admin adds replies directly (and takes some back)
        (await owner.PostAsJsonAsync("/api/reply-requests", new { replies = 500, kind = "Package" })).EnsureSuccessStatusCode();
        var pkgId = (await admin.GetFromJsonAsync<JsonElement>("/api/reply-requests")).EnumerateArray().First().GetProperty("id").GetInt32();
        (await admin.PostAsJsonAsync($"/api/reply-requests/{pkgId}/reject", new { adminNote = "Talk to us first" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/clients/{oid}/topup", new { replies = 100 })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/clients/{oid}/topup", new { replies = -500 })).EnsureSuccessStatusCode(); // never below 0
        mine = Assert.Single((await owner.GetFromJsonAsync<JsonElement>("/api/usage")).GetProperty("clients").EnumerateArray());
        Assert.Equal(100, mine.GetProperty("monthlyReplyLimit").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync($"/api/clients/{oid}/topup", new { replies = 1000 })).StatusCode);
    }
}
