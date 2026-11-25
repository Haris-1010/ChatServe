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

public class OrderFlowTests
{
    private sealed class Ctx : IAsyncDisposable
    {
        public required AppDbContext Db { get; init; }
        public FakeChatCompletionClient Ai { get; } = new();
        public FakeStaffNotifier Staff { get; } = new();
        public OrderService Orders => new(Db, new StaticOptions<OrderOptions>(new OrderOptions()));

        public ClaudeReplyGenerator Generator() =>
            new(Db, Ai, Orders, Staff, new StaticOptions<AiOptions>(new AiOptions()), TimeProvider.System,
                NullLogger<ClaudeReplyGenerator>.Instance);

        public Task<Restaurant> RestaurantAsync() => Db.Restaurants
            .Include(r => r.MenuItems).Include(r => r.Deals).Include(r => r.DeliveryZones).SingleAsync();

        public async Task<string> CodeAsync(string name) => "M" + (await Db.MenuItems.SingleAsync(m => m.Name == name)).Id;

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static async Task<Ctx> NewAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await DemoSeeder.SeedAsync(db, Options.Create(new WhatsAppOptions { PhoneNumberId = "PNID" }), NullLogger.Instance, "923339998877");
        return new Ctx { Db = db };
    }

    private static int _msg;
    private static IncomingMessage Msg(string text) =>
        new("w" + Interlocked.Increment(ref _msg), "PNID", "923001234567", "Ali", "text", text, DateTimeOffset.UtcNow);

    private static object DeliveryOrder(string code, int qty, string area = "Johar Town") => new
    {
        items = new[] { new { code, quantity = qty, note = "" } },
        order_type = "delivery",
        customer_name = "Ali",
        delivery_address = "House 12, Block J, Johar Town",
        delivery_area = area,
        payment_method = "Cash on delivery",
        notes = "",
    };

    /// <summary>The AI calls place_order and gives a short reply (it doesn't write the summary itself).</summary>
    private static void AiPlacesOrder(Ctx c, object order, string aiText = "Ye raha aap ka order:")
    {
        c.Ai.ToolScript = async (_, exec) =>
        {
            var outcome = await exec.ExecuteAsync("place_order", FakeChatCompletionClient.Input(order), default);
            return new AiResult(outcome.IsError ? outcome.Content : aiText, false, 100, 10, 0, 1);
        };
    }

    [Fact]
    public async Task Draft_is_priced_by_server_and_confirm_sends_it_to_kitchen()
    {
        await using var c = await NewAsync();
        var gen = c.Generator();
        AiPlacesOrder(c, DeliveryOrder(await c.CodeAsync("Chicken Karahi (Full)"), 2));

        var summary = await gen.GenerateReplyAsync(Msg("2 full chicken karahi bhej dein, House 12 Block J Johar Town, Ali"), default);

        // 2 x 2600 + 100 delivery: the server's math, not the AI's
        Assert.Contains("Total: Rs 5,300", summary);
        Assert.Contains("*haan* likh dein", summary); // the customer writes Roman Urdu: a natural yes, no "confirm (or 1)"
        var draft = await c.Db.Orders.Include(o => o.Items).SingleAsync();
        Assert.Equal(OrderStatus.PendingConfirmation, draft.Status);
        Assert.Equal(5300, draft.Total);
        Assert.Empty(c.Staff.Alerts); // nothing reaches the kitchen before confirm

        c.Ai.ToolScript = null;
        var callsBefore = c.Ai.Calls.Count;
        var confirmed = await gen.GenerateReplyAsync(Msg("Confirm kar dein"), default);

        Assert.Contains($"#{draft.Id}", confirmed);
        Assert.Equal(callsBefore, c.Ai.Calls.Count); // no AI call on confirm
        Assert.Equal(OrderStatus.Confirmed, (await c.Db.Orders.SingleAsync()).Status);
        var ticket = Assert.Single(c.Staff.Alerts);
        Assert.Contains("NEW ORDER", ticket);
        Assert.Contains("2 x Chicken Karahi (Full)", ticket);
        Assert.Contains("House 12, Block J", ticket);
    }

    [Fact]
    public async Task One_confirms_only_right_after_summary()
    {
        await using var c = await NewAsync();
        var gen = c.Generator();
        AiPlacesOrder(c, DeliveryOrder(await c.CodeAsync("Chicken Karahi (Full)"), 1));
        await gen.GenerateReplyAsync(Msg("ek full karahi"), default);

        // Another exchange happened after the summary: "1" is no longer a confirm (it may answer another question)
        c.Ai.ToolScript = null;
        c.Ai.Responder = _ => new AiResult("Ji, kitne log aayenge?", false, 1, 1, 0);
        await gen.GenerateReplyAsync(Msg("table bhi book ho sakta hai?"), default);
        await gen.GenerateReplyAsync(Msg("1"), default);
        Assert.Equal(OrderStatus.PendingConfirmation, (await c.Db.Orders.SingleAsync()).Status);

        // A new summary was sent, then "1" = confirm
        AiPlacesOrder(c, DeliveryOrder(await c.CodeAsync("Chicken Karahi (Full)"), 1));
        await gen.GenerateReplyAsync(Msg("theek hai order wahi rakhein"), default);
        await gen.GenerateReplyAsync(Msg("1"), default);
        Assert.Equal(OrderStatus.Confirmed, (await c.Db.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Cancel_word_cancels_draft_and_nothing_reaches_kitchen()
    {
        await using var c = await NewAsync();
        var gen = c.Generator();
        AiPlacesOrder(c, DeliveryOrder(await c.CodeAsync("Chicken Biryani"), 2));
        await gen.GenerateReplyAsync(Msg("2 biryani"), default);

        var reply = await gen.GenerateReplyAsync(Msg("cancel kar dein"), default);

        Assert.Equal(CustomerText.DraftCancelled(ru: true), reply);
        Assert.Equal(OrderStatus.Cancelled, (await c.Db.Orders.SingleAsync()).Status);
        Assert.Empty(c.Staff.Alerts);
    }

    [Fact]
    public async Task Pending_draft_is_shown_to_ai_as_note()
    {
        await using var c = await NewAsync();
        var gen = c.Generator();
        AiPlacesOrder(c, DeliveryOrder(await c.CodeAsync("Chicken Biryani"), 2));
        await gen.GenerateReplyAsync(Msg("2 biryani"), default);

        c.Ai.ToolScript = null;
        await gen.GenerateReplyAsync(Msg("ek raita bhi add kar dein"), default);

        var last = c.Ai.Calls.Last().History[^1].Text;
        Assert.Contains("UNCONFIRMED order draft", last);
        Assert.Contains("2 x Chicken Biryani", last);
    }

    [Fact]
    public async Task Invalid_orders_are_rejected_with_reason_for_ai()
    {
        await using var c = await NewAsync();
        var r = await c.RestaurantAsync();
        var customer = new Customer { RestaurantId = r.Id, Phone = "923001112222" };
        c.Db.Customers.Add(customer);
        await c.Db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;

        async Task<string?> Try(object order) =>
            (await new OrderToolExecutor(c.Orders, r, customer, now).ExecuteAsync("place_order", FakeChatCompletionClient.Input(order), default)) is
                { IsError: true } o ? o.Content : null;

        Assert.Contains("NOT AVAILABLE", await Try(DeliveryOrder(await c.CodeAsync("Mutton Chops (4 pcs)"), 1)));
        Assert.Contains("Unknown item code", await Try(DeliveryOrder("M9999", 1)));
        Assert.Contains("don't deliver", await Try(DeliveryOrder(await c.CodeAsync("Chicken Karahi (Full)"), 1, area: "Bahria Town")));
        Assert.Contains("minimum", await Try(DeliveryOrder(await c.CodeAsync("Naan"), 2)));
        Assert.Contains("Quantity", await Try(DeliveryOrder(await c.CodeAsync("Naan"), 500)));
        Assert.Empty(c.Db.Orders);

        // No minimum order or delivery charge for takeaway
        var takeaway = new
        {
            items = new[] { new { code = await c.CodeAsync("Naan"), quantity = 2, note = "" }, new { code = "D" + (await c.Db.Deals.FirstAsync()).Id, quantity = 1, note = "" } },
            order_type = "takeaway", customer_name = "Ali", delivery_address = "", delivery_area = "none", payment_method = "", notes = "",
        };
        Assert.Null(await Try(takeaway));
        var order = await c.Db.Orders.SingleAsync();
        Assert.Equal(120 + 3300, order.Total);
        Assert.Equal(0, order.DeliveryCharge);
    }

    [Fact]
    public async Task Handover_pauses_bot_and_alerts_staff()
    {
        await using var c = await NewAsync();
        var gen = c.Generator();
        c.Ai.ToolScript = async (_, exec) =>
        {
            await exec.ExecuteAsync("handover_to_human", FakeChatCompletionClient.Input(new { reason = "Complaint: khana thanda tha" }), default);
            return new AiResult("Maazrat! Hamari team jald rabta karegi.", false, 1, 1, 0, 1);
        };

        await gen.GenerateReplyAsync(Msg("khana bilkul thanda tha!"), default);
        var callsAfterHandover = c.Ai.Calls.Count;

        Assert.Contains("Complaint: khana thanda tha", Assert.Single(c.Staff.Alerts));
        Assert.True((await c.Db.Customers.SingleAsync()).BotPausedUntil > DateTimeOffset.UtcNow);
        Assert.Null(await gen.GenerateReplyAsync(Msg("hello?"), default)); // staff take over now
        Assert.Equal(callsAfterHandover, c.Ai.Calls.Count);
    }

    [Fact]
    public async Task Table_booking_is_saved_and_staff_alerted()
    {
        await using var c = await NewAsync();
        var gen = c.Generator();
        var tomorrow = DateTimeOffset.UtcNow.ToOffset(Money.PakistanOffset).AddDays(1);
        string? toolText = null;
        c.Ai.ToolScript = async (_, exec) =>
        {
            var o = await exec.ExecuteAsync("book_table", FakeChatCompletionClient.Input(new
            {
                customer_name = "Ali", date = tomorrow.ToString("yyyy-MM-dd"), time = "20:30", people = 6, notes = "family hall",
            }), default);
            toolText = o.Content;
            return new AiResult("Booking request mil gayi.", false, 1, 1, 0, 1);
        };

        await gen.GenerateReplyAsync(Msg("kal raat 8:30 baje 6 logon ka table"), default);

        var booking = await c.Db.TableBookings.SingleAsync();
        Assert.Equal(6, booking.People);
        Assert.Equal(new TimeSpan(20, 30, 0), booking.BookingTime.TimeOfDay);
        Assert.Contains("request", toolText);
        Assert.Contains("TABLE BOOKING", Assert.Single(c.Staff.Alerts));
    }

    [Fact]
    public async Task Booking_in_the_past_is_rejected()
    {
        await using var c = await NewAsync();
        var r = await c.RestaurantAsync();
        var customer = new Customer { RestaurantId = r.Id, Phone = "92300" };
        c.Db.Customers.Add(customer);
        await c.Db.SaveChangesAsync();

        var res = await c.Orders.BookTableAsync(r, customer, "Ali", "2020-01-01", "20:00", 2, null, DateTimeOffset.UtcNow, default);

        Assert.Null(res.Value);
        Assert.Contains("passed", res.Error);
    }

    [Fact]
    public async Task Order_status_tool_lists_recent_orders()
    {
        await using var c = await NewAsync();
        var gen = c.Generator();
        AiPlacesOrder(c, DeliveryOrder(await c.CodeAsync("Chicken Karahi (Full)"), 1));
        await gen.GenerateReplyAsync(Msg("ek karahi"), default);
        await gen.GenerateReplyAsync(Msg("confirm"), default);

        string? status = null;
        c.Ai.ToolScript = async (_, exec) =>
        {
            status = (await exec.ExecuteAsync("get_order_status", FakeChatCompletionClient.Input(new { }), default)).Content;
            return new AiResult("Aap ka order kitchen mein hai.", false, 1, 1, 0, 1);
        };
        await gen.GenerateReplyAsync(Msg("mera order kahan hai?"), default);

        Assert.Contains("confirmed, sent to kitchen", status);
        Assert.Contains("Rs 2,700", status);
    }

    [Theory]
    [InlineData("confirm", true, true)]
    [InlineData("Confirm kar do", true, true)]
    [InlineData("ok confirm!", true, true)]
    [InlineData("1", true, true)]
    [InlineData("1", false, false)]
    [InlineData("abhi confirm na karo", true, false)]
    [InlineData("confirm nahi karna", true, false)]
    [InlineData("pehle batayen confirm karne ke baad kitni der lagegi aur rider kab aayega", true, false)]
    [InlineData("haan", true, true)]
    [InlineData("han done kr do", true, true)]
    [InlineData("ji theek hai bhej dein", true, true)]
    [InlineData("ok", true, true)]
    [InlineData("haan", false, false)]
    [InlineData("han done kr do", false, false)]
    [InlineData("haan aur ek coke bhi", true, false)]
    [InlineData("ok 2 biryani kar do", true, false)]
    [InlineData("ok delivery kab tak?", true, false)]
    [InlineData("nahi abhi nahi", true, false)]
    public void Confirmation_words(string text, bool numeric, bool expected) =>
        Assert.Equal(expected, OrderFormatter.IsConfirmation(text, numeric));

    [Fact]
    public async Task Tools_and_prompt_use_item_codes_and_zones()
    {
        await using var c = await NewAsync();
        var r = await c.RestaurantAsync();

        var prompt = SystemPromptBuilder.Build(r);
        Assert.Contains($"[{await c.CodeAsync("Chicken Karahi (Full)")}] Chicken Karahi (Full): Rs 2,600", prompt);
        Assert.Contains("Johar Town (Rs 100)", prompt);

        var tools = RestaurantTools.Build(r);
        Assert.Equal(["place_order", "get_order_status", "book_table", "handover_to_human"], tools.Select(t => t.Name));
        Assert.Contains("\"Johar Town\"", tools[0].JsonSchema);
        Assert.Contains("\"additionalProperties\":false", tools[0].JsonSchema);
        Assert.Equal(tools[0].JsonSchema, RestaurantTools.Build(r)[0].JsonSchema); // deterministic = cache hit
    }
}
