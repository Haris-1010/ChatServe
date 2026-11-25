using ChatServe.Api.Ai;
using ChatServe.Api.Configuration;
using ChatServe.Api.Data;
using ChatServe.Api.Messaging;
using ChatServe.Api.Orders;
using ChatServe.Api.WhatsApp;
using ChatServe.Api.WhatsApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static ChatServe.Api.Tests.ClaudeReplyGeneratorTests;

namespace ChatServe.Api.Tests;

public sealed class FakeTranscriber : IVoiceTranscriber
{
    public bool IsConfigured { get; set; } = true;
    public string? Text { get; set; } = "menu mein kya kya hai";
    public bool Throw { get; set; }
    public List<MediaFile> Files { get; } = [];

    public Task<string?> TranscribeAsync(MediaFile audio, CancellationToken ct)
    {
        Files.Add(audio);
        if (Throw) throw new HttpRequestException("whisper down");
        return Task.FromResult(Text);
    }
}

public class VoiceAndLanguageTests
{
    private static async Task<AppDbContext> NewDbAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await DemoSeeder.SeedAsync(db, Options.Create(new WhatsAppOptions { PhoneNumberId = "PNID" }), NullLogger.Instance);
        return db;
    }

    private static ClaudeReplyGenerator Gen(AppDbContext db, FakeChatCompletionClient ai, FakeTranscriber? voice, FakeWhatsAppClient wa) =>
        new(db, ai, new OrderService(db, new StaticOptions<OrderOptions>(new OrderOptions())), new FakeStaffNotifier(),
            new StaticOptions<AiOptions>(new AiOptions()), TimeProvider.System, NullLogger<ClaudeReplyGenerator>.Instance,
            null, voice, wa);

    private static IncomingMessage Voice(string id, string? mediaId = "media-1") =>
        new(id, "PNID", "923001234567", "Ali", "audio", null, DateTimeOffset.UtcNow, mediaId);

    [Theory]
    [InlineData("out of delivery ho gea?", true)]
    [InlineData("han done kr do", true)]
    [InlineData("mujhe 2 biryani chahiye", true)]
    [InlineData("menu bhej dein", true)]
    [InlineData("مینو بھیج دیں", true)]
    [InlineData("Can I get the menu please?", false)]
    [InlineData("1", false)]
    [InlineData("Is delivery available in DHA?", false)]
    public void Detects_roman_urdu(string text, bool expected) => Assert.Equal(expected, CustomerText.IsRomanUrdu(text));

    [Fact]
    public void Webhook_audio_message_carries_the_media_id()
    {
        var payload = new WebhookPayload
        {
            Object = "whatsapp_business_account",
            Entry = [new() { Changes = [new() { Field = "messages", Value = new()
            {
                Messages = [new WebhookMessage { Id = "wamid.V", From = "923001234567", Type = "audio", Audio = new WebhookMedia { Id = "MEDIA42", MimeType = "audio/ogg; codecs=opus", Voice = true } }],
            } }] }],
        };

        var msg = Assert.Single(WebhookParser.ExtractMessages(payload, "PNID"));
        Assert.Equal("audio", msg.Type);
        Assert.Equal("MEDIA42", msg.MediaId);
        Assert.Null(msg.Text);
    }

    [Fact]
    public async Task Voice_note_is_transcribed_and_answered_like_text()
    {
        await using var db = await NewDbAsync();
        var ai = new FakeChatCompletionClient { Responder = _ => new AiResult("Ji zaroor! Hamare paas karahi, biryani aur BBQ hai.", false, 10, 5, 0) };
        var wa = new FakeWhatsAppClient();
        wa.Media["media-1"] = new MediaFile([1, 2, 3], "audio/ogg");
        var voice = new FakeTranscriber();

        var reply = await Gen(db, ai, voice, wa).GenerateReplyAsync(Voice("v1"), default);

        Assert.Equal("Ji zaroor! Hamare paas karahi, biryani aur BBQ hai.", reply);
        Assert.Equal(3, Assert.Single(voice.Files).Data.Length);
        Assert.Contains("menu mein kya kya hai", Assert.Single(ai.Calls).History[^1].Text);
        Assert.Equal("🎤 menu mein kya kya hai", (await db.ChatMessages.FirstAsync(m => m.Role == ChatRole.Customer)).Text);
    }

    [Fact]
    public async Task Voice_without_key_or_failing_gets_a_roman_urdu_please_type_reply()
    {
        await using var db = await NewDbAsync();
        var ai = new FakeChatCompletionClient();
        var wa = new FakeWhatsAppClient();
        wa.Media["media-1"] = new MediaFile([1], "audio/ogg");

        var noKey = await Gen(db, ai, new FakeTranscriber { IsConfigured = false }, wa).GenerateReplyAsync(Voice("v1"), default);
        var broken = await Gen(db, ai, new FakeTranscriber { Throw = true }, wa).GenerateReplyAsync(Voice("v2"), default);

        Assert.Equal(CustomerText.CannotRead(ru: true, voice: true), noKey);
        Assert.Equal(CustomerText.CannotRead(ru: true, voice: true), broken);
        Assert.Contains("voice message nahi sun sakte", noKey);
        Assert.Empty(ai.Calls);
    }

    [Fact]
    public async Task English_speaking_customer_gets_english_fixed_messages()
    {
        await using var db = await NewDbAsync();
        var ai = new FakeChatCompletionClient();
        var gen = Gen(db, ai, null, new FakeWhatsAppClient());
        await gen.GenerateReplyAsync(new IncomingMessage("e1", "PNID", "923001234567", "Ali", "text", "Can I see the menu please?", DateTimeOffset.UtcNow), default);

        var reply = await gen.GenerateReplyAsync(new IncomingMessage("e2", "PNID", "923001234567", "Ali", "image", null, DateTimeOffset.UtcNow), default);

        Assert.Equal(ClaudeReplyGenerator.NonTextReply, reply);
    }

    [Fact]
    public void Status_messages_have_a_roman_urdu_version()
    {
        var o = new Order { Id = 7, CustomerName = "Ali", Phone = "1", Status = OrderStatus.OutForDelivery, Type = OrderType.Delivery, Total = 900 };
        Assert.Contains("rider nikal gaya", CustomerText.Status(o, "Rs", ru: true));
        Assert.Contains("out for delivery", CustomerText.Status(o, "Rs", ru: false));
    }
}
