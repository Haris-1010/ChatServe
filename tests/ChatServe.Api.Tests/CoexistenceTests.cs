using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ChatServe.Api.Ai;
using ChatServe.Api.Data;
using ChatServe.Api.Messaging;
using ChatServe.Api.WhatsApp;
using ChatServe.Api.WhatsApp.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChatServe.Api.Tests;

/// <summary>
/// Coexistence: the number stays in the owner's WhatsApp Business app and on the API. Owner replies from the phone
/// (echoes) pause the bot; old chats and contacts are imported. The classic Cloud API setup must keep working.
/// </summary>
public class CoexistenceTests
{
    private const string PhoneId = "111222333";
    private const string Customer = "923001234567";

    // ---------- payloads ----------

    private static string Echo(string text, string id = "wamid.ECHO1", string to = Customer) => $$"""
    {
      "object": "whatsapp_business_account",
      "entry": [{ "id": "WABA_ID", "changes": [{
        "field": "smb_message_echoes",
        "value": {
          "messaging_product": "whatsapp",
          "metadata": { "display_phone_number": "15551234567", "phone_number_id": "{{PhoneId}}" },
          "message_echoes": [{ "from": "15551234567", "to": "{{to}}", "id": "{{id}}", "timestamp": "1764000100",
                               "type": "text", "text": { "body": "{{text}}" } }]
        }
      }]}]
    }
    """;

    private const string History = $$"""
    {
      "object": "whatsapp_business_account",
      "entry": [{ "id": "WABA_ID", "changes": [{
        "field": "history",
        "value": {
          "messaging_product": "whatsapp",
          "metadata": { "display_phone_number": "15551234567", "phone_number_id": "{{PhoneId}}" },
          "history": [{
            "metadata": { "phase": 0, "chunk_order": 1, "progress": 100 },
            "threads": [
              { "id": "923331112222", "messages": [
                { "from": "923331112222", "id": "wamid.H1", "timestamp": "1760000000", "type": "text", "text": { "body": "Biryani hai?" } },
                { "from": "15551234567", "to": "923331112222", "id": "wamid.H2", "timestamp": "1760000060", "type": "text", "text": { "body": "Ji haan, 450 ki" } },
                { "from": "923331112222", "id": "wamid.H3", "timestamp": "1760000120", "type": "image" }
              ]},
              { "id": "923009998888", "messages": [
                { "from": "923009998888", "id": "wamid.H4", "timestamp": "1760000200", "type": "text", "text": { "body": "staff chat" } }
              ]}
            ]
          }]
        }
      }]}]
    }
    """;

    private const string Contacts = $$"""
    {
      "object": "whatsapp_business_account",
      "entry": [{ "id": "WABA_ID", "changes": [{
        "field": "smb_app_state_sync",
        "value": {
          "messaging_product": "whatsapp",
          "metadata": { "display_phone_number": "15551234567", "phone_number_id": "{{PhoneId}}" },
          "state_sync": [
            { "type": "contact", "contact": { "full_name": "Usman Johar Town", "first_name": "Usman", "phone_number": "+92 333 1112222" },
              "action": "add", "metadata": { "timestamp": "1760000000" } },
            { "type": "contact", "contact": { "full_name": "Old", "phone_number": "923004445555" }, "action": "remove" }
          ]
        }
      }]}]
    }
    """;

    // ---------- parser ----------

    [Fact]
    public void Echo_becomes_a_phone_reply_addressed_by_the_customer_number()
    {
        var payload = JsonSerializer.Deserialize<WebhookPayload>(Echo("Aap ka order ready hai"))!;

        var reply = Assert.Single(WebhookParser.ExtractPhoneReplies(payload, "fallback"));
        Assert.Equal(MessageKind.PhoneReply, reply.Kind);
        Assert.Equal(Customer, reply.From);
        Assert.Equal(PhoneId, reply.PhoneNumberId);
        Assert.Equal("Aap ka order ready hai", reply.Text);
        Assert.Empty(WebhookParser.ExtractMessages(payload, "fallback")); // never answered by the bot
    }

    [Fact]
    public void History_and_contacts_are_parsed_with_direction()
    {
        var history = Assert.Single(WebhookParser.ExtractPhoneSync(JsonSerializer.Deserialize<WebhookPayload>(History)!, "x"));
        Assert.Equal(4, history.Messages.Count);
        Assert.True(history.Messages.Single(m => m.MessageId == "wamid.H1").FromCustomer);
        Assert.False(history.Messages.Single(m => m.MessageId == "wamid.H2").FromCustomer);

        var contacts = Assert.Single(WebhookParser.ExtractPhoneSync(JsonSerializer.Deserialize<WebhookPayload>(Contacts)!, "x")).Contacts;
        Assert.Equal(new ContactChange("923331112222", "Usman Johar Town", false), contacts[0]);
        Assert.True(contacts[1].Removed);
    }

    // ---------- end to end ----------

    private sealed class Factory : WebApplicationFactory<Program>
    {
        public FakeWhatsAppClient WhatsApp { get; } = new();
        public FakeChatCompletionClient Ai { get; } = new();
        public FakeGraph Graph { get; } = new();
        private readonly string _dbName = Guid.NewGuid().ToString();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("WhatsApp:PhoneNumberId", PhoneId);
            builder.UseSetting("WhatsApp:AccessToken", "app-wide-token");
            builder.UseSetting("WhatsApp:AppId", "12345");
            builder.UseSetting("WhatsApp:AppSecret", "app-secret");
            builder.UseSetting("WhatsApp:ValidateSignature", "false");
            builder.UseSetting("Dashboard:AdminPassword", DashboardApiTests.Password);
            builder.UseSetting("ConnectionStrings:Default", "Host=unused");
            builder.ConfigureServices(s =>
            {
                s.RemoveAll<IWhatsAppClient>();
                s.AddSingleton<IWhatsAppClient>(WhatsApp);
                s.RemoveAll<IChatCompletionClient>();
                s.AddSingleton<IChatCompletionClient>(Ai);
                s.AddHttpClient<MetaGraph>().ConfigurePrimaryHttpMessageHandler(() => Graph);
                s.RemoveAll<DbContextOptions<AppDbContext>>();
                s.RemoveAll<AppDbContext>();
                s.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
            });
        }

        public async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> f)
        {
            await using var scope = Services.CreateAsyncScope();
            return await f(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }
    }

    /// <summary>Answers Meta Graph calls and records them.</summary>
    private sealed class FakeGraph : HttpMessageHandler
    {
        public ConcurrentQueue<(string Method, string Path, string? Auth, string? Body)> Calls { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            var path = request.RequestUri!.AbsolutePath;
            Calls.Enqueue((request.Method.Method, path, request.Headers.Authorization?.Parameter, body));
            var json = path.EndsWith("/oauth/access_token") ? """{"access_token":"client-business-token"}"""
                : path.EndsWith("/phone_numbers") ? """{"data":[{"id":"777888999"}]}"""
                : """{"success":true}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static async Task<HttpClient> AdminAsync(Factory f)
    {
        var client = f.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { password = DashboardApiTests.Password });
        res.EnsureSuccessStatusCode();
        var token = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> PostWebhook(HttpClient c, string json) =>
        c.PostAsync("/webhook", new StringContent(json, Encoding.UTF8, "application/json"));

    private static async Task WaitUntil(Func<Task<bool>> done)
    {
        for (var i = 0; i < 60 && !await done(); i++)
            await Task.Delay(100);
    }

    [Fact]
    public async Task Owner_reply_from_phone_is_saved_and_pauses_the_bot_for_that_customer()
    {
        await using var f = new Factory();
        var client = f.CreateClient();

        // The customer writes first: the bot answers (as today).
        await PostWebhook(client, SamplePayloads.TextMessage("menu?", "wamid.C1"));
        await WaitUntil(() => Task.FromResult(f.WhatsApp.Sent.Any(s => s.Body.Contains("menu?"))));
        Assert.Single(f.WhatsApp.Sent);

        // The owner answers from the WhatsApp Business app.
        Assert.Equal(HttpStatusCode.OK, (await PostWebhook(client, Echo("Main khud bata deta hun"))).StatusCode);
        await WaitUntil(() => f.DbAsync(db => db.ChatMessages.AnyAsync(m => m.WhatsAppMessageId == "wamid.ECHO1")));

        var (saved, customer) = await f.DbAsync(async db => (
            await db.ChatMessages.SingleAsync(m => m.WhatsAppMessageId == "wamid.ECHO1"),
            await db.Customers.SingleAsync(c => c.Phone == Customer)));
        Assert.Equal(ChatRole.Staff, saved.Role);
        Assert.Equal(PhoneAppSync.PhonePrefix + "Main khud bata deta hun", saved.Text);
        Assert.True(customer.BotPausedUntil > DateTimeOffset.UtcNow.AddHours(1));

        // The customer's next message is saved, but the bot stays quiet: the owner is talking.
        await PostWebhook(client, SamplePayloads.TextMessage("theek hai", "wamid.C2"));
        await WaitUntil(() => f.DbAsync(db => db.ChatMessages.AnyAsync(m => m.WhatsAppMessageId == "wamid.C2")));
        await Task.Delay(200);
        Assert.Single(f.WhatsApp.Sent); // still only the first reply; nothing was sent for the echo either
    }

    [Fact]
    public async Task Echo_to_a_staff_number_or_of_our_own_message_is_ignored()
    {
        await using var f = new Factory();
        var client = f.CreateClient();
        await f.DbAsync(async db =>
        {
            var r = await db.Restaurants.SingleAsync(x => x.WhatsAppPhoneNumberId == PhoneId);
            r.StaffWhatsAppNumber = "923117776666";
            return await db.SaveChangesAsync();
        });
        f.Services.GetRequiredService<SentMessages>().Remember("wamid.OURS");

        await PostWebhook(client, Echo("prep 12", "wamid.STAFF", to: "923117776666"));
        await PostWebhook(client, Echo("bot text", "wamid.OURS"));
        await PostWebhook(client, Echo("real reply", "wamid.REAL"));
        await WaitUntil(() => f.DbAsync(db => db.ChatMessages.AnyAsync(m => m.WhatsAppMessageId == "wamid.REAL")));

        Assert.False(await f.DbAsync(db => db.ChatMessages.AnyAsync(m => m.WhatsAppMessageId == "wamid.STAFF" || m.WhatsAppMessageId == "wamid.OURS")));
        Assert.False(await f.DbAsync(db => db.Customers.AnyAsync(c => c.Phone == "923117776666")));
    }

    [Fact]
    public async Task Pause_on_phone_reply_can_be_turned_off()
    {
        await using var f = new Factory();
        var admin = await AdminAsync(f);
        var rid = await DashboardApiTests.RestaurantIdAsync(admin);
        var put = await admin.PutAsJsonAsync($"/api/restaurants/{rid}/whatsapp",
            new { mode = "Coexistence", whatsAppBusinessAccountId = "WABA", accessToken = (string?)null, pauseBotOnPhoneReply = false });
        put.EnsureSuccessStatusCode();

        await PostWebhook(f.CreateClient(), Echo("hello from phone", "wamid.NOPAUSE"));
        await WaitUntil(() => f.DbAsync(db => db.ChatMessages.AnyAsync(m => m.WhatsAppMessageId == "wamid.NOPAUSE")));
        Assert.Null(await f.DbAsync(db => db.Customers.Where(c => c.Phone == Customer).Select(c => c.BotPausedUntil).SingleAsync()));
    }

    [Fact]
    public async Task History_and_contacts_are_imported_once_without_sending_anything()
    {
        await using var f = new Factory();
        var client = f.CreateClient();
        await f.DbAsync(async db =>
        {
            var r = await db.Restaurants.SingleAsync(x => x.WhatsAppPhoneNumberId == PhoneId);
            r.StaffWhatsAppNumber = "923009998888";
            return await db.SaveChangesAsync();
        });

        await PostWebhook(client, Contacts);
        await WaitUntil(() => f.DbAsync(db => db.PhoneContacts.AnyAsync()));
        await PostWebhook(client, History);
        await PostWebhook(client, History); // Meta resends a chunk
        await WaitUntil(() => f.DbAsync(db => db.ChatMessages.CountAsync(m => m.WhatsAppMessageId!.StartsWith("wamid.H")).ContinueWith(t => t.Result >= 3)));
        await Task.Delay(300);

        var (messages, customer, imported) = await f.DbAsync(async db => (
            await db.ChatMessages.Where(m => m.WhatsAppMessageId!.StartsWith("wamid.H")).OrderBy(m => m.CreatedAt).ToListAsync(),
            await db.Customers.SingleAsync(c => c.Phone == "923331112222"),
            await db.Restaurants.Where(r => r.WhatsAppPhoneNumberId == PhoneId).Select(r => r.HistoryMessagesImported).SingleAsync()));

        Assert.Equal(["wamid.H1", "wamid.H2", "wamid.H3"], messages.Select(m => m.WhatsAppMessageId)); // staff chat skipped, no duplicates
        Assert.Equal([ChatRole.Customer, ChatRole.Staff, ChatRole.Customer], messages.Select(m => m.Role));
        Assert.Equal("[image]", messages[2].Text);
        Assert.Equal("Usman Johar Town", customer.Name); // from the phone's contacts
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1760000120), customer.LastMessageAt);
        Assert.Equal(3, imported);
        Assert.Empty(f.WhatsApp.Sent);
        Assert.Null(customer.BotPausedUntil); // old chats don't pause the bot
    }

    [Fact]
    public async Task Embedded_signup_coexistence_saves_token_subscribes_and_requests_history()
    {
        await using var f = new Factory();
        var admin = await AdminAsync(f);
        var rid = await DashboardApiTests.RestaurantIdAsync(admin);

        var cfg = await admin.GetFromJsonAsync<JsonElement>("/api/whatsapp/signup-config");
        Assert.False(cfg.GetProperty("ready").GetBoolean()); // no configuration ID yet
        Assert.Contains("Embedded Signup configuration ID", cfg.GetProperty("missing").EnumerateArray().Select(x => x.GetString()));

        var res = await admin.PostAsJsonAsync($"/api/restaurants/{rid}/whatsapp/connect",
            new { code = "CODE", wabaId = "WABA9", phoneNumberId = (string?)null, mode = "Coexistence" });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("ok").GetBoolean());
        var conn = body.GetProperty("connection");
        Assert.Equal("777888999", conn.GetProperty("phoneNumberId").GetString()); // looked up from the WABA
        Assert.Equal("Coexistence", conn.GetProperty("mode").GetString());
        Assert.True(conn.GetProperty("hasOwnToken").GetBoolean());
        Assert.DoesNotContain("client-business-token", await res.Content.ReadAsStringAsync()); // never sent to the browser

        var calls = f.Graph.Calls.ToList();
        Assert.Contains(calls, c => c.Path.EndsWith("/WABA9/subscribed_apps") && c.Auth == "client-business-token");
        Assert.Contains(calls, c => c.Path.EndsWith("/777888999/smb_app_data") && c.Body!.Contains("\"smb_app_state_sync\""));
        Assert.Contains(calls, c => c.Path.EndsWith("/777888999/smb_app_data") && c.Body!.Contains("\"history\""));
        Assert.DoesNotContain(calls, c => c.Path.EndsWith("/register")); // a coexistence number is already registered on the app

        // Messages for this number now use the client's own token; other numbers keep the app-wide one.
        var tokens = f.Services.GetRequiredService<WhatsAppTokens>();
        Assert.Equal("client-business-token", await tokens.ForPhoneNumberAsync("777888999"));
        Assert.Equal("app-wide-token", await tokens.ForPhoneNumberAsync("555"));
    }

    [Fact]
    public async Task Embedded_signup_for_a_new_api_only_number_registers_it()
    {
        await using var f = new Factory();
        var admin = await AdminAsync(f);
        var rid = await DashboardApiTests.RestaurantIdAsync(admin);

        var res = await admin.PostAsJsonAsync($"/api/restaurants/{rid}/whatsapp/connect",
            new { code = "CODE", wabaId = "WABA9", phoneNumberId = "444555666", mode = "CloudApi", pin = "123456" });
        res.EnsureSuccessStatusCode();

        var register = Assert.Single(f.Graph.Calls, c => c.Path.EndsWith("/444555666/register"));
        Assert.Contains("\"pin\":\"123456\"", register.Body);
        Assert.DoesNotContain(f.Graph.Calls, c => c.Path.EndsWith("/smb_app_data"));
    }

    [Fact]
    public async Task Owner_logins_cannot_use_the_connection_endpoints()
    {
        await using var f = new Factory();
        var admin = await AdminAsync(f);
        var rid = await DashboardApiTests.RestaurantIdAsync(admin);
        var orgId = (await admin.GetFromJsonAsync<JsonElement>("/api/restaurants"))[0].GetProperty("organizationId").GetInt32();
        (await admin.PostAsJsonAsync("/api/users", new { username = "owner1", password = "owner-pass-1", organizationId = orgId })).EnsureSuccessStatusCode();

        var owner = f.CreateClient();
        var login = await owner.PostAsJsonAsync("/api/auth/login", new { username = "owner1", password = "owner-pass-1" });
        login.EnsureSuccessStatusCode();
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"/api/restaurants/{rid}/whatsapp")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync($"/api/restaurants/{rid}/whatsapp/connect", new { code = "x", wabaId = "y" })).StatusCode);
    }
}
