using Microsoft.Extensions.Caching.Memory;

namespace ChatServe.Api.WhatsApp;

/// <summary>
/// Remembers the ids of messages we sent through the API for a day. Coexistence echoes are only for messages the
/// owner sent from the phone, but if Meta ever echoes one of ours, it must not pause the bot.
/// </summary>
public sealed class SentMessages(IMemoryCache cache)
{
    private static readonly TimeSpan Keep = TimeSpan.FromHours(24);

    public void Remember(string? messageId)
    {
        if (!string.IsNullOrEmpty(messageId))
            cache.Set("wa-sent:" + messageId, true, Keep);
    }

    public bool WasSentByUs(string messageId) => cache.TryGetValue("wa-sent:" + messageId, out _);
}
