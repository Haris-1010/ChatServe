using ChatServe.Api.Ai;
using ChatServe.Api.Configuration;
using ChatServe.Api.Data;
using ChatServe.Api.Messaging;
using ChatServe.Api.Orders;
using ChatServe.Api.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static ChatServe.Api.Tests.ClaudeReplyGeneratorTests;

namespace ChatServe.Api.Tests;

/// <summary>Tap instead of type: staff run orders with buttons; the AI shows choices (menu, order type...) as buttons/lists.</summary>
public class ButtonTests
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
        public ReplyButtons Buttons { get; } = new();

        public ClaudeReplyGenerator Generator() =>
            new(Db, Ai, new OrderService(Db, new StaticOptions<OrderOptions>(new OrderOptions())), Staff,
                new StaticOptions<AiOptions>(new AiOptions()), TimeProvider.System, NullLogger<ClaudeReplyGenerator>.Instance,
                new StaffCommandHandler(Db, WhatsApp, Staff, TimeProvider.System, NullLogger<StaffCommandHandler>.Instance, Buttons),
                buttons: Buttons);

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
        var customer = await c.Db.Customers.FirstOrDefaultAsync(x => x.Phone == CustomerPhone);
        if (customer is null)
        {
            customer = new Customer { RestaurantId = r.Id, Phone = CustomerPhone, Name = "Ali", FirstSeenAt = DateTimeOffset.UtcNow };
            c.Db.Customers.Add(customer);
            await c.Db.SaveChangesAsync();
        }
        var order = new Order
        {
            RestaurantId = r.Id, CustomerId = customer.Id, Status = status, Type = type,
            CustomerName = "Ali", Phone = CustomerPhone, Total = 700,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        c.Db.Orders.Add(order);
        await c.Db.SaveChangesAsync();
        return order;
    }

    private static int _msg;
    private static IncomingMessage Tap(string from, string replyId, string title = "tapped") =>
        new("b" + Interlocked.Increment(ref _msg), "PNID", from, "X", "interactive", title, DateTimeOffset.UtcNow, ReplyId: replyId);
    private static IncomingMessage Text(string from, string text) =>
        new("b" + Interlocked.Increment(ref _msg), "PNID", from, "X", "text", text, DateTimeOffset.UtcNow);

    // ---------- staff ----------

    [Fact]
    public void Order_buttons_offer_only_the_next_steps()
    {
        var delivery = new Order { Id = 6, Status = OrderStatus.Confirmed, Type = OrderType.Delivery, CustomerName = "A", Phone = "1" };
        Assert.Equal(["staff:prep:6", "staff:out:6", "staff:done:6"], StaffButtons.ForOrder(delivery)!.Options.Select(o => o.Id));

        delivery.Status = OrderStatus.OutForDelivery;
        Assert.Equal(["staff:done:6", "staff:cancel:6"], StaffButtons.ForOrder(delivery)!.Options.Select(o => o.Id));

        var takeaway = new Order { Id = 1234, Status = OrderStatus.Preparing, Type = OrderType.Takeaway, CustomerName = "A", Phone = "1" };
        var b = StaffButtons.ForOrder(takeaway)!;
        Assert.Equal(["staff:done:1234", "staff:cancel:1234"], b.Options.Select(o => o.Id));
        Assert.All(b.Options, o => Assert.True(o.Title.Length <= 20, o.Title)); // WhatsApp button limit
        Assert.False(b.IsList);

        takeaway.Status = OrderStatus.Completed;
        Assert.Null(StaffButtons.ForOrder(takeaway));
    }

    [Fact]
    public async Task Confirmed_order_goes_to_the_kitchen_with_buttons_not_typing_instructions()
    {
        await using var c = await NewAsync();
        var draft = await OrderAsync(c, OrderStatus.PendingConfirmation);
        var gen = c.Generator();

        await gen.GenerateReplyAsync(Tap(CustomerPhone, ClaudeReplyGenerator.ConfirmId, "✅ Confirm karein"), default);

        var (ticket, buttons) = Assert.Single(c.Staff.WithButtons);
        Assert.Contains($"NEW ORDER #{draft.Id}", ticket);
        Assert.DoesNotContain("Reply:", ticket);
        Assert.Equal([$"staff:prep:{draft.Id}", $"staff:out:{draft.Id}", $"staff:done:{draft.Id}"], buttons!.Options.Select(o => o.Id));
    }

    [Fact]
    public async Task Staff_tap_moves_the_order_tells_the_customer_and_offers_the_next_buttons()
    {
        await using var c = await NewAsync();
        var order = await OrderAsync(c, OrderStatus.Confirmed);

        var reply = await c.Generator().GenerateReplyAsync(Tap(Kitchen, $"staff:prep:{order.Id}", $"👨‍🍳 Prep #{order.Id}"), default);

        Assert.StartsWith($"✅ Order #{order.Id}: Preparing", reply);
        Assert.Equal(OrderStatus.Preparing, (await c.Db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status);
        Assert.Contains(c.WhatsApp.Sent, s => s.To == CustomerPhone && s.Body.Contains($"#{order.Id}*") && s.Body.Contains("👨‍🍳"));
        Assert.Equal([$"staff:out:{order.Id}", $"staff:done:{order.Id}", $"staff:cancel:{order.Id}"], c.Buttons.Value!.Options.Select(o => o.Id));
        // the other staff number hears about it, with the same buttons
        var (note, next) = Assert.Single(c.Staff.WithButtons);
        Assert.Contains("Preparing", note);
        Assert.Equal(c.Buttons.Value.Options, next!.Options);
        Assert.Empty(c.Ai.Calls); // no AI for staff taps
    }

    [Fact]
    public async Task A_customer_cannot_use_staff_button_ids()
    {
        await using var c = await NewAsync();
        var order = await OrderAsync(c, OrderStatus.Confirmed);

        await c.Generator().GenerateReplyAsync(Tap(CustomerPhone, $"staff:done:{order.Id}", "done"), default);

        Assert.Equal(OrderStatus.Confirmed, (await c.Db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status);
    }

    [Fact]
    public async Task Orders_command_returns_a_list_and_tapping_an_order_shows_its_buttons()
    {
        await using var c = await NewAsync();
        var a = await OrderAsync(c, OrderStatus.Confirmed);
        var b = await OrderAsync(c, OrderStatus.OutForDelivery);
        var gen = c.Generator();

        await gen.GenerateReplyAsync(Text(Kitchen, "orders"), default);
        Assert.True(c.Buttons.Value!.IsList);
        Assert.Equal([$"staff:show:{a.Id}", $"staff:show:{b.Id}"], c.Buttons.Value.Options.Select(o => o.Id));

        var shown = await gen.GenerateReplyAsync(Tap(Kitchen, $"staff:show:{b.Id}", $"#{b.Id} Ali"), default);
        Assert.Contains($"ORDER #{b.Id}", shown);
        Assert.Contains("Out for delivery", shown);
        Assert.Equal([$"staff:done:{b.Id}", $"staff:cancel:{b.Id}"], c.Buttons.Value!.Options.Select(o => o.Id));
    }

    [Fact]
    public async Task Booking_request_has_buttons_and_staff_can_confirm_it_by_tapping()
    {
        await using var c = await NewAsync();
        var r = await c.Db.Restaurants.SingleAsync();
        var customer = new Customer { RestaurantId = r.Id, Phone = CustomerPhone, Name = "Ali" };
        c.Db.Customers.Add(customer);
        await c.Db.SaveChangesAsync();
        var booking = new TableBooking
        {
            RestaurantId = r.Id, CustomerId = customer.Id, CustomerName = "Ali", Phone = CustomerPhone, People = 4,
            BookingTime = DateTimeOffset.UtcNow.AddDays(1), Status = BookingStatus.Requested, CreatedAt = DateTimeOffset.UtcNow,
        };
        c.Db.TableBookings.Add(booking);
        await c.Db.SaveChangesAsync();
        Assert.Equal(["staff:bconfirm:" + booking.Id, "staff:bcancel:" + booking.Id], StaffButtons.ForBooking(booking).Options.Select(o => o.Id));

        var reply = await c.Generator().GenerateReplyAsync(Tap(Manager, "staff:bconfirm:" + booking.Id), default);

        Assert.Contains("confirmed", reply);
        Assert.Equal(BookingStatus.Confirmed, (await c.Db.TableBookings.AsNoTracking().SingleAsync()).Status);
        Assert.Contains(c.WhatsApp.Sent, s => s.To == CustomerPhone && s.Body.Contains("table booking is confirmed"));
    }

    // ---------- customer: choices from the AI ----------

    [Fact]
    public async Task Ai_choices_become_buttons_or_a_list()
    {
        await using var c = await NewAsync();
        var r = await c.Db.Restaurants.Include(x => x.DeliveryZones).SingleAsync();
        var customer = new Customer { RestaurantId = r.Id, Phone = CustomerPhone };
        var exec = new OrderToolExecutor(new OrderService(c.Db, new StaticOptions<OrderOptions>(new OrderOptions())), r, customer, DateTimeOffset.UtcNow);

        await exec.ExecuteAsync("show_options", FakeChatCompletionClient.Input(new
        {
            options = new[] { new { title = "🛵 Delivery", description = "" }, new { title = "🛍️ Takeaway", description = "" }, new { title = "🍽️ Dine-in", description = "" } },
            list_button = "",
        }), default);
        Assert.False(exec.Options!.IsList);
        Assert.Equal(["opt:1", "opt:2", "opt:3"], exec.Options.Options.Select(o => o.Id));

        await exec.ExecuteAsync("show_options", FakeChatCompletionClient.Input(new
        {
            options = Enumerable.Range(1, 5).Select(i => new { title = $"Dish {i}", description = $"Rs {i}00" }).ToArray(),
            list_button = "Menu dekhein",
        }), default);
        Assert.True(exec.Options!.IsList);
        Assert.Equal("Menu dekhein", exec.Options.ListButton);
        Assert.Equal("Rs 300", exec.Options.Options[2].Description);

        var tooMany = await exec.ExecuteAsync("show_options", FakeChatCompletionClient.Input(new
        {
            options = Enumerable.Range(1, 11).Select(i => new { title = $"Dish {i}", description = "" }).ToArray(),
            list_button = "",
        }), default);
        Assert.True(tooMany.IsError);
    }

    [Fact]
    public async Task Reply_carries_the_ai_choices_and_remembers_them_for_the_next_message()
    {
        await using var c = await NewAsync();
        c.Ai.ToolScript = async (_, exec) =>
        {
            await exec.ExecuteAsync("show_options", FakeChatCompletionClient.Input(new
            {
                options = new[] { new { title = "📋 Menu", description = "" }, new { title = "🔥 Deals", description = "" } },
                list_button = "",
            }), default);
            return new AiResult("Assalam o alaikum! Kya dekhna chahenge?", false, 100, 10, 0, 1);
        };

        var reply = await c.Generator().GenerateReplyAsync(Text(CustomerPhone, "hi"), default);

        Assert.Equal("Assalam o alaikum! Kya dekhna chahenge?", reply);
        Assert.Equal(["📋 Menu", "🔥 Deals"], c.Buttons.Value!.Options.Select(o => o.Title));
        var saved = await c.Db.ChatMessages.Where(m => m.Role == ChatRole.Bot).SingleAsync();
        Assert.EndsWith("[Choices shown: 📋 Menu | 🔥 Deals]", saved.Text);
    }

    [Fact]
    public void Prompt_tells_the_ai_to_prefer_taps()
    {
        var prompt = SystemPromptBuilder.Build(new Restaurant { Name = "X", WhatsAppPhoneNumberId = "1" });
        Assert.Contains("show_options", prompt);
        Assert.Contains("Customers prefer tapping to typing", prompt);
    }
}
