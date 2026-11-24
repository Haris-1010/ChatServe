using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using ChatServe.Api.Configuration;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.Extensions.FileProviders;

namespace ChatServe.Api.Dashboard;

/// <summary>
/// Dashboard (Angular) ke liye: login (bearer token), CORS, JSON, aur built Angular files (wwwroot) serve karna.
/// Saare admin endpoints /api ke neeche hain aur login ke baghair nahi chalte.
/// </summary>
public static class DashboardSetup
{
    public const string CorsPolicy = "dashboard";

    /// <summary>Development mein password set na ho to yahi chalta hai (dashboard par warning dikhti hai).</summary>
    public const string DevDefaultPassword = "admin";

    public static void AddDashboard(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(BearerTokenDefaults.AuthenticationScheme)
            .AddBearerToken(o => o.BearerTokenExpiration = TimeSpan.FromHours(12));
        builder.Services.AddAuthorization();

        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // Dashboard alag domain (maslan Vercel) par ho to: Dashboard:AllowedOrigins = "https://chatserve.vercel.app"
        var origins = (builder.Configuration["Dashboard:AllowedOrigins"] ?? "")
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p =>
        {
            if (origins.Length > 0)
                p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        }));

        builder.Services.AddHttpClient("graph", c => c.Timeout = TimeSpan.FromSeconds(15));
    }

    /// <summary>Effective dashboard password. null = koi password set nahi (Production mein login band).</summary>
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
        // Angular build (npm run build) yahan files rakhta hai. Na hon to sirf API chalti hai.
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
                    "Dashboard password set nahi hai. Server par env var Dashboard__AdminPassword set karein.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            if (!FixedTimeEquals(req.Password ?? "", expected))
            {
                await Task.Delay(500); // password guess karna mehenga ho
                return Results.Problem("Password ghalat hai.", statusCode: StatusCodes.Status401Unauthorized);
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "admin")], BearerTokenDefaults.AuthenticationScheme));
            return Results.SignIn(principal, authenticationScheme: BearerTokenDefaults.AuthenticationScheme);
        }).AllowAnonymous();

        var admin = api.MapGroup("").RequireAuthorization();
        admin.MapSettingsEndpoints();
        admin.MapRestaurantEndpoints();
        admin.MapOperationsEndpoints();

        // Angular routes (/orders, /menu ...) par refresh karein to index.html mile. /api aur /webhook is se bahar.
        if (HasBuiltDashboard(app.Environment))
            app.MapFallbackToFile("{*path:nonfile:regex(^(?!api/|webhook).*$)}", "index.html");
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));

    public sealed record LoginRequest(string? Password);
}
