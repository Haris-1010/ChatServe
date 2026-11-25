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

public class StaffCommandTests
{
    private const string Kitchen = "923339998877";
    private const string Manager = "923004445566";
    private const string CustomerPhone = "923001234567";

    private sealed class Ctx : IAsyncDisposable
    {
        public required AppDbContext Db { get; init; }
        public FakeChatCompletionClient Ai { get; } = new();
        public FakeStaffNotifier Staff { get; } = new();
        public FakeWhatsAppClient WhatsApp { get; } = new();

        public ClaudeReplyGenerator Generator() =>
            new(Db, Ai, new OrderService(Db, new StaticOptions<OrderOptions>(new OrderOptions())), Staff,
                new StaticOptions<AiOptions>(new AiOptions()), TimeProvider.System, NullLogger<ClaudeReplyGenerator>.Instance,
                new StaffCommandHandler(Db, WhatsApp, Staff, TimeProvider.System, NullLogger<StaffCommandHandler>.Instance));

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static async Task<Ctx> NewAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await DemoSeeder.SeedAsync(db, Options.Create(new WhatsAppOptions { PhoneNumberId = "PNID" }), NullLogger.Instance, Kitchen);
        var r = await db.Restaurants.SingleAsync();
        r.StaffWhatsAppNumber = $"{Kitchen},{Manager}";
        await db.SaveChangesAsync();
        return new Ctx { Db = db };
    }

    private static async Task<Order> OrderAsync(Ctx c, OrderStatus status, OrderType type = OrderType.Delivery)
    {
        var r = await c.Db.Restaurants.SingleAsync();
        var customer = new Customer { RestaurantId = r.Id, Phone = CustomerPhone, Name = "Ali", FirstSeenAt = DateTimeOffset.UtcNow };
        c.Db.Customers.Add(customer);
        await c.Db.SaveChangesAsync();
        var order = new Order
        {
            RestaurantId = r.Id, CustomerId = customer.Id, Status = status, Type = type,
            CustomerName = "Ali", Phone = CustomerPhone, Total = 1250,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        c.Db.Orders.Add(order);
        await c.Db.SaveChangesAsync();
        return order;
    }

    private static int _msg;
    private static IncomingMessage Msg(string from, string text) =>
        new("s" + Interlocked.Increment(ref _msg), "PNID", from, "Staff", "text", text, DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("out 12", StaffCommandKind.SetStatus, 12, OrderStatus.OutForDelivery)]
    [InlineData("OUT #12", StaffCommandKind.SetStatus, 12, OrderStatus.OutForDelivery)]
    [InlineData("12 out", StaffCommandKind.SetStatus, 12, OrderStatus.OutForDelivery)]
    [InlineData("prep 7", StaffCommandKind.SetStatus, 7, OrderStatus.Preparing)]
    [InlineData("done 7.", StaffCommandKind.SetStatus, 7, OrderStatus.Completed)]
    [InlineData("ready 7", StaffCommandKind.SetStatus, 7, OrderStatus.Completed)]
    [InlineData("cancel 7", StaffCommandKind.SetStatus, 7, OrderStatus.Cancelled)]
    [InlineData("orders", StaffCommandKind.List, 0, OrderStatus.Confirmed)]
    [InlineData("Start", StaffCommandKind.Help, 0, OrderStatus.Confirmed)]
    public void Parses_commands(string text, StaffCommandKind kind, int id, OrderStatus status)
    {
        var c = StaffCommand.Parse(text);
        Assert.NotNull(c);
        Assert.Equal(kind, c.Kind);
        Assert.Equal(id, c.OrderId);
        if (kind == StaffCommandKind.SetStatus) Assert.Equal(status, c.Status);
    }

    [Theory]
    [InlineData("hi")]
    [InlineData("menu dikhao")]
    [InlineData("cancel")]
    [InlineData("2 biryani")]
    [InlineData("out 12 please")]
    public void Normal_text_is_not_a_command(string text) => Assert.Null(StaffCommand.Parse(text));

    [Fact]
    public void Normalizes_staff_numbers()
    {
        Assert.Equal(("923001234567,923331234567", null), StaffNumbers.Normalize("+92 300 1234567, 0092333-1234567\n+92 300 1234567"));
        Assert.Equal((null, null), StaffNumbers.Normalize("  "));
        Assert.NotNull(StaffNumbers.Normalize("923001234567, 03001234567").Error); // no country code
        Assert.True(StaffNumbers.Contains("923001234567,923331234567", "923331234567"));
        Assert.False(StaffNumbers.Contains("923001234567", "92300"));
    }

    [Fact]
    public async Task Staff_moves_order_on_customer_is_told_and_other_staff_too()
    {
        await using var c = await NewAsync();
        var order = await OrderAsync(c, OrderStatus.Preparing);

        var reply = await c.Generator().GenerateReplyAsync(Msg(Manager, $"out {order.Id}"), default);

        Assert.StartsWith($"✅ Order #{order.Id}: Out for delivery", reply);
        Assert.Equal(OrderStatus.OutForDelivery, (await c.Db.Orders.SingleAsync()).Status);
        var sent = Assert.Single(c.WhatsApp.Sent);
        Assert.Equal(CustomerPhone, sent.To);
        Assert.Contains("out for delivery", sent.Body);
        Assert.Contains("Updated by +" + Manager, Assert.Single(c.Staff.Alerts));
        Assert.Empty(c.Ai.Calls);
        Assert.Equal(1, await c.Db.Customers.CountAsync()); // no customer record for the staff member
    }

    [Fact]
    public async Task Same_command_twice_does_not_message_the_customer_twice()
    {
        await using var c = await NewAsync();
        var order = await OrderAsync(c, OrderStatus.Confirmed);
        var gen = c.Generator();

        await gen.GenerateReplyAsync(Msg(Kitchen, $"done {order.Id}"), default);
        var again = await gen.GenerateReplyAsync(Msg(Kitchen, $"done {order.Id}"), default);

        Assert.Contains("already", again);
        Assert.Single(c.WhatsApp.Sent);
    }

    [Fact]
    public async Task Out_on_takeaway_and_unconfirmed_orders_is_refused()
    {
        await using var c = await NewAsync();
        var takeaway = await OrderAsync(c, OrderStatus.Confirmed, OrderType.Takeaway);
        var gen = c.Generator();

        Assert.Contains($"done {takeaway.Id}", await gen.GenerateReplyAsync(Msg(Kitchen, $"out {takeaway.Id}"), default));
        Assert.Contains("not found", await gen.GenerateReplyAsync(Msg(Kitchen, "prep 999"), default));
        Assert.Empty(c.WhatsApp.Sent);
        Assert.Equal(OrderStatus.Confirmed, (await c.Db.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Orders_lists_only_active_orders()
    {
        await using var c = await NewAsync();
        var active = await OrderAsync(c, OrderStatus.Preparing);
        var r = await c.Db.Restaurants.SingleAsync();
        c.Db.Orders.Add(new Order
        {
            RestaurantId = r.Id, CustomerId = active.CustomerId, Status = OrderStatus.Completed, Type = OrderType.Delivery,
            CustomerName = "Old", Phone = CustomerPhone, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await c.Db.SaveChangesAsync();

        var reply = await c.Generator().GenerateReplyAsync(Msg(Kitchen, "orders"), default);

        Assert.Contains($"#{active.Id} Delivery", reply);
        Assert.DoesNotContain("Old", reply);
    }

    [Fact]
    public async Task Commands_from_customers_and_normal_staff_text_go_to_the_bot()
    {
        await using var c = await NewAsync();
        var order = await OrderAsync(c, OrderStatus.Preparing);
        var gen = c.Generator();

        await gen.GenerateReplyAsync(Msg(CustomerPhone, $"out {order.Id}"), default);
        await gen.GenerateReplyAsync(Msg(Kitchen, "hi, menu dikhao"), default);

        Assert.Equal(2, c.Ai.Calls.Count);
        Assert.Equal(OrderStatus.Preparing, (await c.Db.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Notifier_sends_to_every_staff_number_except_the_sender()
    {
        var wa = new FakeWhatsAppClient();
        var notifier = new WhatsAppStaffNotifier(wa, new SimulationContext(), NullLogger<WhatsAppStaffNotifier>.Instance);
        var r = new Restaurant { Name = "R", WhatsAppPhoneNumberId = "PNID", StaffWhatsAppNumber = $"{Kitchen},{Manager}" };

        await notifier.NotifyAsync(r, "ticket", default);
        await notifier.NotifyAsync(r, "update", default, exceptPhone: Manager);

        Assert.Equal([(Kitchen, "ticket"), (Manager, "ticket"), (Kitchen, "update")], wa.Sent.Select(s => (s.To, s.Body)).ToArray());
    }
}
