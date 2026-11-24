using ChatServe.Api.Messaging;

namespace ChatServe.Api.Tests;

public class KeyedLockTests
{
    [Fact]
    public async Task Same_key_runs_one_at_a_time_different_keys_in_parallel()
    {
        var locks = new KeyedLock();
        var concurrentSameKey = 0;
        var maxSameKey = 0;

        async Task Work(string key, bool track)
        {
            using var _ = await locks.AcquireAsync(key, default);
            if (track) maxSameKey = Math.Max(maxSameKey, Interlocked.Increment(ref concurrentSameKey));
            await Task.Delay(200);
            if (track) Interlocked.Decrement(ref concurrentSameKey);
        }

        var a = Enumerable.Range(0, 5).Select(_ => Work("customer-1", true));
        var other = Work("customer-2", false);
        var otherDone = await Task.WhenAny(other, Task.Delay(600)) == other;
        await Task.WhenAll(a);

        Assert.Equal(1, maxSameKey);
        Assert.True(otherDone, "doosra customer pehle wale ka intezar na kare");
    }
}
