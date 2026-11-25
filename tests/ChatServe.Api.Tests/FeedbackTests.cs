using System.Text.Json;
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

public class FeedbackTests
{
    private const string Kitchen = "923339998877";
    private const string CustomerPhone = "923001234567";

    private sealed class Ctx : IAsyncDisposable
    {
        public required AppDbContext Db { get; init; }
        public FakeChatCompletionClient Ai { get; } = new();
        public FakeStaffNotifier Staff { get; } = new();
        public FakeWhatsAppClient WhatsApp { get; } = new();
        public ReplyButtons Buttons { get; } = new();

        public ClaudeReplyGenerator Generator()
        {
            var orders = new OrderService(Db, new StaticOptions<OrderOptions>(new OrderOptions()));
            return new(Db, Ai, orders, Staff, new StaticOptions<AiOptions>(new AiOptions()), TimeProvider.System,
                NullLogger<ClaudeReplyGenerator>.Instance,
                new StaffCommandHandler(Db, WhatsApp, Staff, TimeProvider.System, NullLogger<StaffCommandHandler>.Instance),
                feedback: new FeedbackFlow(Db, orders), buttons: Buttons);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static async Task<Ctx> NewAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await DemoSeeder.SeedAsync(db, Options.Create(new WhatsAppOptions { PhoneNumberId = "PNID" }), NullLogger.Instance, Kitchen);
        return new Ctx { Db = db };
    }

    private static async Task<Order> OrderAsync(Ctx c, OrderStatus status)
    {
        var r = await c.Db.Restaurants.SingleAsync();
        var customer = new Customer { RestaurantId = r.Id, Phone = CustomerPhone, Name = "Ali", FirstSeenAt = DateTimeOffset.UtcNow };
        c.Db.Customers.Add(customer);
        await c.Db.SaveChangesAsync();
        var order = new Order
        {
            RestaurantId = r.Id, CustomerId = customer.Id, Status = status, Type = OrderType.Delivery,
            CustomerName = "Ali", Phone = CustomerPhone, Total = 1250, DeliveryArea = "Johar Town",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, ConfirmedAt = DateTimeOffset.UtcNow,
        };
        c.Db.Orders.Add(order);
        await c.Db.SaveChangesAsync();
        return order;
    }

    private static int _msg;
    private static IncomingMessage Msg(string text, string? replyId = null, string from = CustomerPhone) =>
        new("f" + Interlocked.Increment(ref _msg), "PNID", from, "Ali", replyId is null ? "text" : "interactive", text,
            DateTimeOffset.UtcNow, ReplyId: replyId);

    [Fact]
    public async Task Customer_says_received_then_rates_then_comments()
    {
        await using var c = await NewAsync();
        var gen = c.Generator();
        var order = await OrderAsync(c, OrderStatus.OutForDelivery);

        // The rider hasn't been marked done yet: "mil gaya" completes the order and asks for a rating (no AI)
        var ask = await gen.GenerateReplyAsync(Msg("order mil gaya shukriya"), default);
        await c.Db.Entry(order).ReloadAsync();
        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.NotNull(order.ReceivedAt);
        Assert.Contains($"#{order.Id}", ask);
        var list = Assert.IsType<InteractiveMessage>(c.Buttons.Value);
        Assert.True(list.IsList);
        Assert.Equal(5, list.Options.Count);
        Assert.Equal($"rate:{order.Id}:5", list.Options[0].Id);
        Assert.Contains("received order", Assert.Single(c.Staff.Alerts));
        Assert.Empty(c.Ai.Calls);

        // Tapping 2 stars: saved, the staff hear about it, and the bot asks what went wrong
        var sorry = await gen.GenerateReplyAsync(Msg("⭐⭐", $"rate:{order.Id}:2"), default);
        await c.Db.Entry(order).ReloadAsync();
        Assert.Equal(2, order.Rating);
        Assert.Contains("Afsos", sorry);
        Assert.Contains("Low rating", c.Staff.Alerts.Last());
        Assert.Null(c.Buttons.Value);

        // The next message is the comment: saved and sent to staff; the AI still answers it
        c.Ai.ToolScript = (_, _) => Task.FromResult(new AiResult("Maazrat, restaurant ko bata diya hai.", false, 1, 1, 0, 0));
        var answer = await gen.GenerateReplyAsync(Msg("raita nahi tha aur khana thanda tha"), default);
        await c.Db.Entry(order).ReloadAsync();
        Assert.Equal("raita nahi tha aur khana thanda tha", order.FeedbackComment);
        Assert.Null(order.FeedbackPromptMessageId);
        Assert.Contains("raita nahi tha", c.Staff.Alerts.Last());
        Assert.Equal("Maazrat, restaurant ko bata diya hai.", answer);

        // After that, messages are normal again (not saved as feedback)
        await gen.GenerateReplyAsync(Msg("menu bhej dein"), default);
        await c.Db.Entry(order).ReloadAsync();
        Assert.Equal("raita nahi tha aur khana thanda tha", order.FeedbackComment);
    }

    [Fact]
    public async Task Typed_star_rating_counts_only_after_the_rating_question()
    {
        await using var c = await NewAsync();
        var gen = c.Generator();
        var order = await OrderAsync(c, OrderStatus.Completed);

        await gen.GenerateReplyAsync(Msg("✅ Got it", $"rcv:{order.Id}"), default);
        var thanks = await gen.GenerateReplyAsync(Msg("5"), default);
        await c.Db.Entry(order).ReloadAsync();
        Assert.Equal(5, order.Rating);
        Assert.Contains("⭐⭐⭐⭐⭐", thanks);
        Assert.DoesNotContain(c.Staff.Alerts, a => a.Contains("Low rating"));

        // "ok" after a good rating: fixed thanks, no comment saved, no AI
        await gen.GenerateReplyAsync(Msg("ok thanks"), default);
        await c.Db.Entry(order).ReloadAsync();
        Assert.Null(order.FeedbackComment);
        Assert.Empty(c.Ai.Calls);

        // Now a "4" is just a message for the AI
        await gen.GenerateReplyAsync(Msg("4"), default);
        await c.Db.Entry(order).ReloadAsync();
        Assert.Equal(5, order.Rating);
        Assert.Single(c.Ai.Calls);
    }

    [Fact]
    public async Task Problem_button_hands_over_to_staff()
    {
        await using var c = await NewAsync();
        var gen = c.Generator();
        var order = await OrderAsync(c, OrderStatus.Completed);

        var reply = await gen.GenerateReplyAsync(Msg("⚠️ Problem", $"prob:{order.Id}"), default);

        Assert.Contains($"#{order.Id}", reply);
        Assert.Contains("problem", Assert.Single(c.Staff.Alerts));
        Assert.True((await c.Db.Customers.SingleAsync()).BotPausedUntil > DateTimeOffset.UtcNow);
        Assert.Null((await c.Db.Orders.SingleAsync()).ReceivedAt);
    }

    [Fact]
    public async Task Received_without_a_recent_delivery_goes_to_the_ai()
    {
        await using var c = await NewAsync();
        var gen = c.Generator();
        await OrderAsync(c, OrderStatus.Preparing);

        await gen.GenerateReplyAsync(Msg("mil gaya"), default);

        Assert.Single(c.Ai.Calls);
        Assert.Null((await c.Db.Orders.SingleAsync()).ReceivedAt);
    }

    [Fact]
    public async Task Staff_out_and_done_send_delivery_buttons()
    {
        await using var c = await NewAsync();
        var gen = c.Generator();
        var order = await OrderAsync(c, OrderStatus.Preparing);

        await gen.GenerateReplyAsync(Msg($"out {order.Id}", from: Kitchen), default);
        var outMsg = Assert.Single(c.WhatsApp.Interactive);
        Assert.Equal([$"rcv:{order.Id}"], outMsg.Buttons.Options.Select(o => o.Id));

        await gen.GenerateReplyAsync(Msg($"done {order.Id}", from: Kitchen), default);
        Assert.Equal([$"rcv:{order.Id}", $"prob:{order.Id}"], c.WhatsApp.Interactive.Last().Buttons.Options.Select(o => o.Id));
    }

    [Fact]
    public async Task Order_summary_has_confirm_buttons_and_the_button_confirms()
    {
        await using var c = await NewAsync();
        var gen = c.Generator();
        var code = "M" + (await c.Db.MenuItems.FirstAsync(m => m.Name == "Chicken Karahi (Full)")).Id;
        c.Ai.ToolScript = async (_, exec) =>
        {
            var o = await exec.ExecuteAsync("place_order", FakeChatCompletionClient.Input(new
            {
                items = new[] { new { code, quantity = 1, note = "" } }, order_type = "takeaway", customer_name = "Ali",
                delivery_address = "", delivery_area = "", payment_method = "Cash", notes = "",
            }), default);
            return new AiResult(o.IsError ? o.Content : "Ye raha order:", false, 1, 1, 0, 1);
        };

        await gen.GenerateReplyAsync(Msg("ek karahi takeaway"), default);
        Assert.Equal([ClaudeReplyGenerator.ConfirmId, ClaudeReplyGenerator.ChangeId, ClaudeReplyGenerator.CancelId],
            c.Buttons.Value!.Options.Select(o => o.Id));

        await gen.GenerateReplyAsync(Msg("✅ Confirm karein", ClaudeReplyGenerator.ConfirmId), default);
        Assert.Equal(OrderStatus.Confirmed, (await c.Db.Orders.SingleAsync()).Status);
    }

    [Theory]
    [InlineData("mil gaya", true)]
    [InlineData("Order mil gaya, shukriya!", true)]
    [InlineData("received", true)]
    [InlineData("abhi mil gaya", true)]
    [InlineData("aa gaya bhai", true)]
    [InlineData("got it thanks", true)]
    [InlineData("nahi mila", false)]
    [InlineData("order abhi tak nahi aaya", false)]
    [InlineData("mil gaya?", false)]
    [InlineData("menu mil sakta hai", false)]
    public void Recognises_received(string text, bool expected) => Assert.Equal(expected, FeedbackFlow.IsReceived(text));

    [Theory]
    [InlineData("5", 5)]
    [InlineData("4/5", 4)]
    [InlineData("3 stars", 3)]
    [InlineData("⭐⭐⭐⭐", 4)]
    [InlineData("10", null)]
    [InlineData("5 karahi", null)]
    public void Parses_ratings(string text, int? expected) => Assert.Equal(expected, FeedbackFlow.ParseRating(text));

    [Fact]
    public void Interactive_payloads_match_the_whatsapp_format()
    {
        var buttons = JsonSerializer.SerializeToElement(WhatsAppClient.BuildInteractive("92300", "Hi",
            new InteractiveMessage([new("a", "A very long button title here")])));
        var i = buttons.GetProperty("interactive");
        Assert.Equal("button", i.GetProperty("type").GetString());
        Assert.Equal(20, i.GetProperty("action").GetProperty("buttons")[0].GetProperty("reply").GetProperty("title").GetString()!.Length);

        var list = JsonSerializer.SerializeToElement(WhatsAppClient.BuildInteractive("92300", "Rate", FeedbackFlow.RatingList(7, false)));
        var l = list.GetProperty("interactive");
        Assert.Equal("list", l.GetProperty("type").GetString());
        Assert.Equal("rate:7:5", l.GetProperty("action").GetProperty("sections")[0].GetProperty("rows")[0].GetProperty("id").GetString());
    }
}
