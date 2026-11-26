using System.Collections.Concurrent;
using System.Net;
using System.Text;
using ChatServe.Api.Ai;
using ChatServe.Api.Data;
using ChatServe.Api.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ChatServe.Api.Tests;

public sealed class FakeWhatsAppClient : IWhatsAppClient
{
    public ConcurrentQueue<(string PhoneNumberId, string To, string Body)> Sent { get; } = new();
    public ConcurrentQueue<string> Read { get; } = new();

    public Task<string?> SendTextAsync(string phoneNumberId, string to, string body, string? replyToMessageId = null, CancellationToken ct = default)
    {
        Sent.Enqueue((phoneNumberId, to, body));
        return Task.FromResult<string?>("wamid.OUT");
    }

    /// <summary>Messages sent with buttons (also counted in <see cref="Sent"/>).</summary>
    public ConcurrentQueue<(string To, string Body, InteractiveMessage Buttons)> Interactive { get; } = new();

    public Task<string?> SendInteractiveAsync(string phoneNumberId, string to, string body, InteractiveMessage interactive, CancellationToken ct = default)
    {
        Interactive.Enqueue((to, body, interactive));
        Sent.Enqueue((phoneNumberId, to, body));
        return Task.FromResult<string?>("wamid.OUT");
    }

    public Dictionary<string, MediaFile> Media { get; } = new();

    public Task<MediaFile> DownloadMediaAsync(string mediaId, string? phoneNumberId = null, CancellationToken ct = default) =>
        Media.TryGetValue(mediaId, out var f) ? Task.FromResult(f) : throw new WhatsAppApiException("not found", 404, null);

    public Task MarkAsReadAsync(string phoneNumberId, string messageId, bool showTyping = true, CancellationToken ct = default)
    {
        Read.Enqueue(messageId);
        return Task.CompletedTask;
    }
}

public class WebhookEndpointTests : IClassFixture<WebhookEndpointTests.Factory>
{
    private const string AppSecret = "test-secret";
    private readonly Factory _factory;

    public WebhookEndpointTests(Factory factory) => _factory = factory;

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public FakeWhatsAppClient WhatsApp { get; } = new();
        public FakeChatCompletionClient Ai { get; } = new();
        private readonly string _dbName = Guid.NewGuid().ToString();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("WhatsApp:VerifyToken", "my-verify-token");
            builder.UseSetting("WhatsApp:AccessToken", "fake");
            builder.UseSetting("WhatsApp:PhoneNumberId", "111222333");
            builder.UseSetting("WhatsApp:AppSecret", AppSecret);
            builder.UseSetting("WhatsApp:ValidateSignature", "true");
            builder.UseSetting("ConnectionStrings:Default", "Host=unused");
            builder.ConfigureServices(s =>
            {
                s.RemoveAll<IWhatsAppClient>();
                s.AddSingleton<IWhatsAppClient>(WhatsApp);
                s.RemoveAll<IChatCompletionClient>();
                s.AddSingleton<IChatCompletionClient>(Ai);
                // in-memory DB instead of Postgres
                s.RemoveAll<DbContextOptions<AppDbContext>>();
                s.RemoveAll<AppDbContext>();
                s.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
            });
        }
    }

    [Fact]
    public async Task Get_with_correct_token_returns_challenge()
    {
        var res = await _factory.CreateClient()
            .GetAsync("/webhook?hub.mode=subscribe&hub.verify_token=my-verify-token&hub.challenge=98765");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("98765", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_with_wrong_token_returns_403()
    {
        var res = await _factory.CreateClient()
            .GetAsync("/webhook?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=98765");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Post_without_valid_signature_returns_401()
    {
        var content = new StringContent(SamplePayloads.TextMessage("hi"), Encoding.UTF8, "application/json");
        var res = await _factory.CreateClient().PostAsync("/webhook", content);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Post_signed_message_returns_200_and_sends_one_reply_even_if_meta_retries()
    {
        var client = _factory.CreateClient();
        var json = SamplePayloads.TextMessage("Assalam o alaikum", messageId: "wamid.E2E");

        for (var i = 0; i < 2; i++) // Meta retry simulate
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            using var req = new HttpRequestMessage(HttpMethod.Post, "/webhook") { Content = new ByteArrayContent(bytes) };
            req.Content.Headers.ContentType = new("application/json");
            req.Headers.Add(WebhookSignatureValidator.HeaderName, WebhookSignatureValidator.ComputeHeader(bytes, AppSecret));

            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }

        // wait for the background worker
        for (var i = 0; i < 50 && !_factory.WhatsApp.Sent.Any(s => s.Body.Contains("Assalam o alaikum")); i++)
            await Task.Delay(100);
        await Task.Delay(300);

        var sent = _factory.WhatsApp.Sent.Where(s => s.Body.Contains("Assalam o alaikum")).ToList();
        var reply = Assert.Single(sent);
        Assert.Equal("923001234567", reply.To);
        Assert.Equal("111222333", reply.PhoneNumberId);
        Assert.Equal("AI: Assalam o alaikum", reply.Body);
        Assert.Contains("wamid.E2E", _factory.WhatsApp.Read);
    }
}
