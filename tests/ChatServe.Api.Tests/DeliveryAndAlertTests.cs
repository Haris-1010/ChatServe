using ChatServe.Api.Ai;
using ChatServe.Api.Configuration;
using ChatServe.Api.Data;
using ChatServe.Api.Messaging;
using ChatServe.Api.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static ChatServe.Api.Tests.ClaudeReplyGeneratorTests;

namespace ChatServe.Api.Tests;

public class DeliveryAndAlertTests
{
    private static readonly List<DeliveryZone> Zones =
    [
        new() { Id = 1, Name = "Johar Town", Charge = 150, IsActive = true },
        new() { Id = 2, Name = "DHA", Charge = 200, IsActive = true },
        new() { Id = 3, Name = "Model Town", Charge = 150, IsActive = true },
    ];

    [Theory]
    [InlineData("House 34, Street 2, Paragon City, Johar Town", "Paragon City")]
    [InlineData("House 5, DHA Phase 5, Johar Town", "\"DHA\"")]
    [InlineData("12 Wapda Society, Lahore", "Wapda Society")]
    public void Address_naming_another_place_is_flagged(string address, string mentioned)
    {
        var problem = AddressCheck.Problem(address, Zones[0], Zones);
        Assert.NotNull(problem);
        Assert.Contains(mentioned, problem);
        Assert.Contains("Johar Town, DHA, Model Town", problem); // tells the customer where we deliver
        Assert.Contains("address_confirmed", problem);
    }

    [Theory]
    [InlineData("House 34, Street 2, Johar Town, Lahore")]
    [InlineData("House 12, Block J, johar town")]
    [InlineData("Flat 3, Al-Rehman Plaza near Emporium")]
    public void Normal_address_passes(string address) => Assert.Null(AddressCheck.Problem(address, Zones[0], Zones));

    [Fact]
    public void Model_Town_address_in_Model_Town_passes() =>
        Assert.Null(AddressCheck.Problem("88-B, Model Town, Lahore", Zones[2], Zones));

    [Fact]
    public void Delivered_message_asks_if_the_order_was_received()
    {
        var o = new Order { Id = 2, CustomerName = "Haris", Phone = "1", Status = OrderStatus.Completed, Type = OrderType.Delivery, Total = 1000 };
        Assert.Contains("Kya aap ko order mil gaya?", CustomerText.Status(o, "Rs", ru: true));
        Assert.Contains("Did you receive it?", CustomerText.Status(o, "Rs", ru: false));
    }

    private sealed class SeqOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    [Fact]
    public async Task Admin_gets_one_whatsapp_alert_per_level_per_month()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await DemoSeeder.SeedAsync(db, Options.Create(new WhatsAppOptions { PhoneNumberId = "PNID" }), NullLogger.Instance);
        var r = await db.Restaurants.SingleAsync();
        var org = await db.Organizations.SingleAsync();
        org.MonthlyReplyLimit = 5;
        var customer = new Customer { RestaurantId = r.Id, Phone = "923001112222", FirstSeenAt = DateTimeOffset.UtcNow };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var wa = new FakeWhatsAppClient();
        var alerts = new UsageAlerts(db, wa,
            new SeqOptions<UsageOptions>(new UsageOptions { WarnAtPercent = 80, AdminWhatsAppNumber = "+92 300 9998877" }),
            new SeqOptions<WhatsAppOptions>(new WhatsAppOptions { PhoneNumberId = "OPERATOR" }),
            TimeProvider.System, NullLogger<UsageAlerts>.Instance);

        async Task ReplyAsync()
        {
            db.ChatMessages.Add(new ChatMessage { RestaurantId = r.Id, CustomerId = customer.Id, Role = ChatRole.Bot, Text = "x", InputTokens = 100, OutputTokens = 10 });
            await db.SaveChangesAsync();
            await alerts.CheckAsync(org.Id, default);
        }

        for (var i = 0; i < 3; i++) await ReplyAsync();   // 60%: nothing
        Assert.Empty(wa.Sent);
        await ReplyAsync();                                // 80%: warning
        await ReplyAsync();                                // 100%: limit
        await ReplyAsync();                                // 120%: nothing new

        var sent = wa.Sent.ToArray();
        Assert.Equal(2, sent.Length);
        Assert.All(sent, s => Assert.Equal(("OPERATOR", "923009998877"), (s.PhoneNumberId, s.To)));
        Assert.Contains("80%", sent[0].Body);
        Assert.Contains("used all 5 AI replies", sent[1].Body);
    }
}
