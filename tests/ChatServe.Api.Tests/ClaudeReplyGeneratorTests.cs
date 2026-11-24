using ChatServe.Api.Ai;
using ChatServe.Api.Configuration;
using ChatServe.Api.Data;
using ChatServe.Api.Messaging;
using ChatServe.Api.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Tests;

public class ClaudeReplyGeneratorTests
{
    internal sealed class StaticOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static async Task<AppDbContext> NewDbAsync(bool botEnabled = true)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await DemoSeeder.SeedAsync(db, Options.Create(new WhatsAppOptions { PhoneNumberId = "PNID" }), NullLogger.Instance);
        if (!botEnabled)
        {
            (await db.Restaurants.SingleAsync()).IsBotEnabled = false;
            await db.SaveChangesAsync();
        }
        return db;
    }

    private static ClaudeReplyGenerator NewGenerator(AppDbContext db, IChatCompletionClient ai, IStaffNotifier? staff = null) =>
        new(db, ai, new OrderService(db, new StaticOptions<OrderOptions>(new OrderOptions())), staff ?? new FakeStaffNotifier(),
            new StaticOptions<AiOptions>(new AiOptions { HistoryLimit = 20 }), TimeProvider.System,
            NullLogger<ClaudeReplyGenerator>.Instance);

    private static IncomingMessage Msg(string id, string? text, string type = "text", string pnid = "PNID") =>
        new(id, pnid, "923001234567", "Ali", type, text, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Replies_with_ai_text_and_stores_both_messages()
    {
        await using var db = await NewDbAsync();
        var ai = new FakeChatCompletionClient();

        var reply = await NewGenerator(db, ai).GenerateReplyAsync(Msg("w1", "Karahi kitne ki hai?"), default);

        Assert.Equal("AI: Karahi kitne ki hai?", reply);
        var (system, history, tools) = Assert.Single(ai.Calls);
        Assert.Contains(tools, t => t.Name == "place_order");
        Assert.Contains("Chicken Karahi (Full): Rs 2,600", system);
        Assert.Single(history);

        var stored = await db.ChatMessages.OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(new[] { ChatRole.Customer, ChatRole.Bot }, stored.Select(m => m.Role));
        Assert.Equal(1000, stored[1].InputTokens);
        Assert.Equal("Ali", (await db.Customers.SingleAsync()).Name);
    }

    [Fact]
    public async Task Sends_previous_conversation_as_memory()
    {
        await using var db = await NewDbAsync();
        var ai = new FakeChatCompletionClient();
        var gen = NewGenerator(db, ai);

        await gen.GenerateReplyAsync(Msg("w1", "Family deal mein kya hai?"), default);
        await gen.GenerateReplyAsync(Msg("w2", "Theek hai wohi chahiye"), default);

        var history = ai.Calls.Last().History;
        Assert.Equal(3, history.Count);
        Assert.Equal("Family deal mein kya hai?", history[0].Text);
        Assert.Equal("AI: Family deal mein kya hai?", history[1].Text);
        Assert.StartsWith("Theek hai wohi chahiye", history[2].Text);
    }

    [Fact]
    public async Task Duplicate_whatsapp_message_is_ignored()
    {
        await using var db = await NewDbAsync();
        var ai = new FakeChatCompletionClient();
        var gen = NewGenerator(db, ai);

        await gen.GenerateReplyAsync(Msg("w1", "salam"), default);
        var second = await gen.GenerateReplyAsync(Msg("w1", "salam"), default);

        Assert.Null(second);
        Assert.Single(ai.Calls);
    }

    [Fact]
    public async Task Non_text_message_gets_fixed_reply_without_ai_call()
    {
        await using var db = await NewDbAsync();
        var ai = new FakeChatCompletionClient();

        var reply = await NewGenerator(db, ai).GenerateReplyAsync(Msg("w1", null, "image"), default);

        Assert.Equal(ClaudeReplyGenerator.NonTextReply, reply);
        Assert.Empty(ai.Calls);
    }

    [Fact]
    public async Task Refusal_or_ai_error_returns_fallback()
    {
        await using var db = await NewDbAsync();
        var refusing = new FakeChatCompletionClient { Responder = _ => new AiResult(null, true, 10, 0, 0) };
        Assert.Equal(ClaudeReplyGenerator.FallbackReply,
            await NewGenerator(db, refusing).GenerateReplyAsync(Msg("w1", "x"), default));

        var failing = new FakeChatCompletionClient { Responder = _ => throw new HttpRequestException("down") };
        Assert.Equal(ClaudeReplyGenerator.FallbackReply,
            await NewGenerator(db, failing).GenerateReplyAsync(Msg("w2", "y"), default));
    }

    [Fact]
    public async Task No_reply_when_bot_disabled_or_restaurant_unknown()
    {
        await using var db = await NewDbAsync(botEnabled: false);
        var ai = new FakeChatCompletionClient();
        var gen = NewGenerator(db, ai);

        Assert.Null(await gen.GenerateReplyAsync(Msg("w1", "salam"), default));
        Assert.Equal(1, await db.ChatMessages.CountAsync()); // the customer's message is still saved
        Assert.Null(await gen.GenerateReplyAsync(Msg("w2", "salam", pnid: "UNKNOWN"), default));
        Assert.Empty(ai.Calls);
    }
}
