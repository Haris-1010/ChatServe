using ChatServe.Api.WhatsApp;
using Microsoft.Extensions.Caching.Memory;

namespace ChatServe.Api.Messaging;

/// <summary>
/// Background worker: queue se message uthata hai, read mark karta hai, reply banata hai aur bhejta hai.
/// </summary>
public sealed class MessageProcessingService(
    IMessageQueue queue,
    IServiceScopeFactory scopeFactory,
    IMemoryCache cache,
    ILogger<MessageProcessingService> logger) : BackgroundService
{
    // Ek waqt mein kitne messages parallel process hon. Phase 2 mein Claude call slow hogi, is liye >1.
    private const int MaxParallelism = 4;

    // Meta kabhi kabhi same message dobara bhejta hai (retry). Is ke liye id yaad rakhte hain.
    private static readonly TimeSpan DedupWindow = TimeSpan.FromHours(24);

    // Ek customer ke messages ek ke baad ek (warna history/memory gadbad ho sakti hai).
    // Mukhtalif customers parallel chalte hain.
    private readonly KeyedLock _customerLock = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Message processor started");

        await Parallel.ForEachAsync(
            queue.ReadAllAsync(stoppingToken),
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = stoppingToken },
            async (message, ct) =>
            {
                try
                {
                    await ProcessAsync(message, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // shutdown
                }
                catch (Exception ex)
                {
                    // Ek message fail ho to worker band nahi hona chahiye.
                    logger.LogError(ex, "Failed to process message {MessageId}", message.MessageId);
                }
            });
    }

    private async Task ProcessAsync(IncomingMessage message, CancellationToken ct)
    {
        if (!TryMarkSeen(message.MessageId))
        {
            logger.LogDebug("Duplicate message {MessageId} skipped", message.MessageId);
            return;
        }

        logger.LogInformation("Processing {Type} message {MessageId} from {From}",
            message.Type, message.MessageId, WhatsAppClient.Mask(message.From));

        using var _ = await _customerLock.AcquireAsync(message.PhoneNumberId + ":" + message.From, ct);

        await using var scope = scopeFactory.CreateAsyncScope();
        var whatsApp = scope.ServiceProvider.GetRequiredService<IWhatsAppClient>();
        var replyGenerator = scope.ServiceProvider.GetRequiredService<IReplyGenerator>();

        // Blue ticks + typing indicator. Ye fail ho jaye to bhi reply bhejna hai.
        try
        {
            await whatsApp.MarkAsReadAsync(message.PhoneNumberId, message.MessageId, showTyping: true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not mark message {MessageId} as read", message.MessageId);
        }

        var reply = await replyGenerator.GenerateReplyAsync(message, ct);
        if (string.IsNullOrWhiteSpace(reply))
            return;

        await whatsApp.SendTextAsync(message.PhoneNumberId, message.From, reply, replyToMessageId: null, ct);
    }

    private bool TryMarkSeen(string messageId)
    {
        var key = "wa-msg:" + messageId;
        lock (cache)
        {
            if (cache.TryGetValue(key, out _))
                return false;
            cache.Set(key, true, DedupWindow);
            return true;
        }
    }
}
