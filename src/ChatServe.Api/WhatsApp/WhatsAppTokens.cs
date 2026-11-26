using ChatServe.Api.Configuration;
using ChatServe.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.WhatsApp;

/// <summary>
/// Which token to use for a number. A number connected with Embedded Signup (e.g. coexistence) has its own
/// business token; every other number uses the app-wide token from Settings (the classic setup).
/// </summary>
public sealed class WhatsAppTokens(IServiceScopeFactory scopes, IMemoryCache cache, IOptionsMonitor<WhatsAppOptions> options)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    public async Task<string> ForPhoneNumberAsync(string? phoneNumberId, CancellationToken ct = default)
    {
        var appWide = options.CurrentValue.AccessToken;
        if (string.IsNullOrWhiteSpace(phoneNumberId))
            return appWide;

        var own = await cache.GetOrCreateAsync(Key(phoneNumberId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await db.Restaurants.AsNoTracking()
                .Where(r => r.WhatsAppPhoneNumberId == phoneNumberId)
                .Select(r => r.WhatsAppAccessToken)
                .FirstOrDefaultAsync(ct);
        });
        return string.IsNullOrWhiteSpace(own) ? appWide : own;
    }

    /// <summary>Call after a restaurant's token or Phone number ID changes.</summary>
    public void Forget(string? phoneNumberId)
    {
        if (!string.IsNullOrWhiteSpace(phoneNumberId))
            cache.Remove(Key(phoneNumberId));
    }

    private static string Key(string phoneNumberId) => "wa-token:" + phoneNumberId;
}
