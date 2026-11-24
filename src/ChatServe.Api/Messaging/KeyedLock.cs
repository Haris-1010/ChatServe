namespace ChatServe.Api.Messaging;

/// <summary>Har key (customer) ke liye alag async lock. Istemal na ho to entry hat jati hai.</summary>
public sealed class KeyedLock
{
    private readonly Dictionary<string, (SemaphoreSlim Semaphore, int RefCount)> _locks = new();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken ct)
    {
        SemaphoreSlim semaphore;
        lock (_locks)
        {
            if (_locks.TryGetValue(key, out var entry))
            {
                semaphore = entry.Semaphore;
                _locks[key] = (semaphore, entry.RefCount + 1);
            }
            else
            {
                semaphore = new SemaphoreSlim(1, 1);
                _locks[key] = (semaphore, 1);
            }
        }

        try
        {
            await semaphore.WaitAsync(ct);
        }
        catch
        {
            Release(key, waitSucceeded: false);
            throw;
        }

        return new Releaser(this, key);
    }

    private void Release(string key, bool waitSucceeded)
    {
        lock (_locks)
        {
            var (semaphore, refCount) = _locks[key];
            if (waitSucceeded) semaphore.Release();
            if (refCount == 1)
            {
                _locks.Remove(key);
                semaphore.Dispose();
            }
            else
            {
                _locks[key] = (semaphore, refCount - 1);
            }
        }
    }

    private sealed class Releaser(KeyedLock owner, string key) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Release(key, waitSucceeded: true);
        }
    }
}
