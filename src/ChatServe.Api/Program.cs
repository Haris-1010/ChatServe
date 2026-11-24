using Anthropic;
using ChatServe.Api.Ai;
using ChatServe.Api.Configuration;
using ChatServe.Api.Data;
using ChatServe.Api.Infrastructure;
using ChatServe.Api.Messaging;
using ChatServe.Api.Orders;
using ChatServe.Api.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Railway/Render "PORT" env var dete hain.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ---- WhatsApp ----
builder.Services.AddOptions<WhatsAppOptions>()
    .Bind(builder.Configuration.GetSection(WhatsAppOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(o => !o.ValidateSignature || !string.IsNullOrEmpty(o.AppSecret),
        "WhatsApp:AppSecret is required when WhatsApp:ValidateSignature is true.")
    .ValidateOnStart();

builder.Services.AddOptions<BotOptions>()
    .Bind(builder.Configuration.GetSection(BotOptions.SectionName));

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IWhatsAppClient, WhatsAppClient>(c => c.Timeout = TimeSpan.FromSeconds(20));

// ---- Database (PostgreSQL) ----
// ConnectionStrings:Default, ya Railway/Render ka DATABASE_URL.
var connectionString = builder.Configuration.GetConnectionString("Default")
                       ?? builder.Configuration["DATABASE_URL"]
                       ?? throw new InvalidOperationException("ConnectionStrings:Default (ya DATABASE_URL) set karein.");
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(DatabaseUrl.ToConnectionString(connectionString)));

// ---- AI (Claude) ----
builder.Services.AddOptions<AiOptions>().Bind(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.AddSingleton(sp =>
{
    var key = sp.GetRequiredService<IOptions<AiOptions>>().Value.ApiKey;
    // Key khali ho to SDK khud ANTHROPIC_API_KEY env var parhta hai.
    return string.IsNullOrWhiteSpace(key) ? new AnthropicClient() : new AnthropicClient { ApiKey = key };
});
builder.Services.AddSingleton<IChatCompletionClient, ClaudeChatCompletionClient>();
builder.Services.AddSingleton(TimeProvider.System);

// ---- Orders (Phase 3) ----
builder.Services.AddOptions<OrderOptions>().Bind(builder.Configuration.GetSection(OrderOptions.SectionName));
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<IStaffNotifier, WhatsAppStaffNotifier>();

// ---- Message pipeline ----
builder.Services.AddSingleton<IMessageQueue, MessageQueue>();
if (builder.Configuration.GetValue("Bot:UseAi", true))
    builder.Services.AddScoped<IReplyGenerator, ClaudeReplyGenerator>();
else
    builder.Services.AddScoped<IReplyGenerator, HelloReplyGenerator>(); // Phase 1 wala simple reply
builder.Services.AddHostedService<MessageProcessingService>();

builder.Services.AddControllers();

var app = builder.Build();

// Startup par migrations chalao aur demo restaurant seed karo.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational())
        await db.Database.MigrateAsync();
    else
        await db.Database.EnsureCreatedAsync();

    if (app.Configuration.GetValue("Bot:SeedDemoRestaurant", true))
        await DemoSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IOptions<WhatsAppOptions>>(), app.Logger,
            app.Configuration["Bot:DemoStaffWhatsAppNumber"]);
}

app.MapGet("/", () => Results.Ok(new { service = "ChatServe API", status = "running" }));
app.MapGet("/health", () => Results.Ok("healthy"));
app.MapControllers();

app.Run();

public partial class Program; // integration tests (WebApplicationFactory) ke liye
