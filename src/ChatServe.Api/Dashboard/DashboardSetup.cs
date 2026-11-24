using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ChatServe.Api.Configuration;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.Extensions.FileProviders;

namespace ChatServe.Api.Dashboard;

/// <summary>
/// For the Angular dashboard: login (bearer token), CORS, JSON, and serving the built Angular files (wwwroot).
/// All admin endpoints live under /api and require login.
/// </summary>
public static class DashboardSetup
{
    public const string CorsPolicy = "dashboard";

    /// <summary>Used in Development when no password is set (the dashboard shows a warning).</summary>
    public const string DevDefaultPassword = "admin";

    public static void AddDashboard(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(BearerTokenDefaults.AuthenticationScheme)
            .AddBearerToken(o => o.BearerTokenExpiration = TimeSpan.FromHours(12));
        builder.Services.AddAuthorization();

        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // If the dashboard is on another domain (e.g. Vercel): Dashboard:AllowedOrigins = "https://chatserve.vercel.app"
        var origins = (builder.Configuration["Dashboard:AllowedOrigins"] ?? "")
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p =>
        {
            if (origins.Length > 0)
                p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        }));

        builder.Services.AddHttpClient("graph", c => c.Timeout = TimeSpan.FromSeconds(15));
    }

    /// <summary>The effective dashboard password. null = no password set (login is disabled in Production).</summary>
    public static string? AdminPassword(IConfiguration config, IHostEnvironment env)
    {
        var configured = config["Dashboard:AdminPassword"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;
        return env.IsDevelopment() ? DevDefaultPassword : null;
    }

    public static bool UsingDefaultPassword(IConfiguration config, IHostEnvironment env) =>
        string.IsNullOrWhiteSpace(config["Dashboard:AdminPassword"]) && env.IsDevelopment();

    public static void UseDashboardStaticFiles(this WebApplication app)
    {
        // The Angular build (npm run build) puts files here. Without them only the API runs.
        if (!HasBuiltDashboard(app.Environment))
            return;
        app.UseDefaultFiles();
        app.UseStaticFiles();
    }

    public static bool HasBuiltDashboard(IWebHostEnvironment env) =>
        env.WebRootFileProvider is not NullFileProvider && env.WebRootFileProvider.GetFileInfo("index.html").Exists;

    public static void MapDashboard(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireCors(CorsPolicy);

        api.MapPost("/auth/login", async (LoginRequest req, IConfiguration config, IHostEnvironment env) =>
        {
            var expected = AdminPassword(config, env);
            if (expected is null)
                return Results.Problem(
                    "The dashboard password is not set. Set the Dashboard__AdminPassword env var on the server.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            if (!FixedTimeEquals(req.Password ?? "", expected))
            {
                await Task.Delay(500); // make password guessing slow
                return Results.Problem("Wrong password.", statusCode: StatusCodes.Status401Unauthorized);
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "admin")], BearerTokenDefaults.AuthenticationScheme));
            return Results.SignIn(principal, authenticationScheme: BearerTokenDefaults.AuthenticationScheme);
        }).AllowAnonymous();

        var admin = api.MapGroup("").RequireAuthorization();
        admin.MapSettingsEndpoints();
        admin.MapRestaurantEndpoints();
        admin.MapOperationsEndpoints();

        // Refreshing on Angular routes (/orders, /menu ...) returns index.html. /api and /webhook are excluded.
        if (HasBuiltDashboard(app.Environment))
            app.MapFallbackToFile("{*path:nonfile:regex(^(?!api/|webhook).*$)}", "index.html");
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));

    public sealed record LoginRequest(string? Password);
}
