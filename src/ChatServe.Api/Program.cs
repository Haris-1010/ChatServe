using ChatServe.Api.Ai;
using ChatServe.Api.Configuration;
using ChatServe.Api.Dashboard;
using ChatServe.Api.Data;
using ChatServe.Api.Infrastructure;
using ChatServe.Api.Messaging;
using ChatServe.Api.Orders;
using ChatServe.Api.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Railway/Render provide a "PORT" env var.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ---- Dashboard settings (DB) ----
// Added last: settings saved from the dashboard take priority over appsettings/env vars.
var dashboardSettings = new DashboardSettingsProvider();
((IConfigurationBuilder)builder.Configuration).Add(dashboardSettings);
builder.Services.AddSingleton(dashboardSettings);
builder.Services.AddScoped<SettingsStore>();

// ---- WhatsApp ----
// The app runs even with empty tokens (so they can be set from the dashboard first). Missing values show on the dashboard status.
builder.Services.AddOptions<WhatsAppOptions>()
    .Bind(builder.Configuration.GetSection(WhatsAppOptions.SectionName))
    .ValidateDataAnnotations();

builder.Services.AddOptions<BotOptions>()
    .Bind(builder.Configuration.GetSection(BotOptions.SectionName));

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IWhatsAppClient, WhatsAppClient>(c => c.Timeout = TimeSpan.FromSeconds(20));

// ---- Database (PostgreSQL) ----
// ConnectionStrings:Default, or Railway/Render's DATABASE_URL.
// appsettings.json ships an empty ConnectionStrings:Default, so treat blank as "not set".
var connectionString = new[] { builder.Configuration.GetConnectionString("Default"), builder.Configuration["DATABASE_URL"] }
                           .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                       ?? throw new InvalidOperationException("Set ConnectionStrings:Default (or DATABASE_URL).");
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(DatabaseUrl.ToConnectionString(connectionString)));

// ---- AI (Claude) ----
builder.Services.AddOptions<AiOptions>().Bind(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.AddOptions<UsageOptions>().Bind(builder.Configuration.GetSection(UsageOptions.SectionName));
builder.Services.AddSingleton<AnthropicClientProvider>(); // new client when the key changes from the dashboard
builder.Services.AddSingleton<IChatCompletionClient, ClaudeChatCompletionClient>();
builder.Services.AddSingleton<IChatScreenshotReader, ClaudeChatScreenshotReader>();
builder.Services.AddOptions<VoiceOptions>().Bind(builder.Configuration.GetSection(VoiceOptions.SectionName));
builder.Services.AddHttpClient<IVoiceTranscriber, WhisperVoiceTranscriber>(c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddSingleton(TimeProvider.System);

// ---- Orders (Phase 3) ----
builder.Services.AddOptions<OrderOptions>().Bind(builder.Configuration.GetSection(OrderOptions.SectionName));
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<IStaffNotifier, WhatsAppStaffNotifier>();
builder.Services.AddScoped<StaffCommandHandler>();
builder.Services.AddScoped<SimulationContext>();

// ---- Message pipeline ----
builder.Services.AddSingleton<IMessageQueue, MessageQueue>();
builder.Services.AddScoped<ClaudeReplyGenerator>();
builder.Services.AddScoped<HelloReplyGenerator>(); // the simple Phase 1 reply (Bot:UseAi = false)
builder.Services.AddScoped<IReplyGenerator, SwitchingReplyGenerator>();
builder.Services.AddHostedService<MessageProcessingService>();

builder.Services.AddControllers();
builder.AddDashboard();

var app = builder.Build();

// On startup, run migrations and seed the demo restaurant.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational())
        await db.Database.MigrateAsync();
    else
        await db.Database.EnsureCreatedAsync();

    // Load dashboard settings into configuration (so the seeder also gets the dashboard PhoneNumberId).
    await scope.ServiceProvider.GetRequiredService<SettingsStore>().LoadAsync();

    if (app.Configuration.GetValue("Bot:SeedDemoRestaurant", true))
        await DemoSeeder.SeedAsync(db, Options.Create(scope.ServiceProvider.GetRequiredService<IOptionsMonitor<WhatsAppOptions>>().CurrentValue), app.Logger,
            app.Configuration["Bot:DemoStaffWhatsAppNumber"]);
}

app.UseDashboardStaticFiles(); // before routing, so JS/CSS files are served directly
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (!DashboardSetup.HasBuiltDashboard(app.Environment))
    app.MapGet("/", () => Results.Ok(new { service = "ChatServe API", status = "running", dashboard = "run 'npm run build' in the dashboard/ folder" }));
app.MapGet("/health", () => Results.Ok("healthy"));
app.MapControllers();
app.MapDashboard();

app.Run();

public partial class Program; // for integration tests (WebApplicationFactory)
