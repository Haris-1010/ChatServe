using System.Threading.Channels;

namespace ChatServe.Api.Messaging;

/// <summary>An old message from the owner's phone (coexistence history sync).</summary>
public sealed record HistoryMessage(
    string CustomerPhone,
    bool FromCustomer,
    string MessageId,
    string Type,
    string? Text,
    DateTimeOffset SentAt);

/// <summary>A contact saved in the owner's WhatsApp Business app was added/changed or removed.</summary>
public sealed record ContactChange(string Phone, string? Name, bool Removed);

/// <summary>One "history" or "smb_app_state_sync" webhook: imported in the background, no replies are sent.</summary>
public sealed record PhoneSyncBatch(
    string PhoneNumberId,
    IReadOnlyList<HistoryMessage> Messages,
    IReadOnlyList<ContactChange> Contacts,
    IReadOnlyList<string> Errors);

public interface IPhoneSyncQueue
{
    bool TryEnqueue(PhoneSyncBatch batch);
    IAsyncEnumerable<PhoneSyncBatch> ReadAllAsync(CancellationToken ct);
}

/// <summary>
/// History arrives in many large webhooks; it gets its own queue so it never fills the customer message queue.
/// </summary>
public sealed class PhoneSyncQueue : IPhoneSyncQueue
{
    private readonly Channel<PhoneSyncBatch> _channel = Channel.CreateBounded<PhoneSyncBatch>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public bool TryEnqueue(PhoneSyncBatch batch) => _channel.Writer.TryWrite(batch);

    public IAsyncEnumerable<PhoneSyncBatch> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

/// <summary>Imports history and contacts one batch at a time.</summary>
public sealed class PhoneSyncService(IPhoneSyncQueue queue, IServiceScopeFactory scopes, ILogger<PhoneSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var batch in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<PhoneAppSync>().ImportAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Phone sync batch for {PhoneNumberId} failed", batch.PhoneNumberId);
            }
        }
    }
}
