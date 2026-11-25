using ChatServe.Api.WhatsApp;
using Microsoft.Extensions.Caching.Memory;

namespace ChatServe.Api.Messaging;

/// <summary>
/// Background worker: takes a message from the queue, marks it read, builds a reply and sends it.
/// </summary>
public sealed class MessageProcessingService(
    IMessageQueue queue,
    IServiceScopeFactory scopeFactory,
    IMemoryCache cache,
    ILogger<MessageProcessingService> logger) : BackgroundService
{
    // How many messages to process in parallel. Claude calls are slow (Phase 2), so >1.
    private const int MaxParallelism = 4;

    // Meta sometimes resends the same message (retry), so we remember ids.
    private static readonly TimeSpan DedupWindow = TimeSpan.FromHours(24);

    // One customer's messages are processed one at a time (otherwise history/memory can get mixed up).
    // Different customers run in parallel.
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
                    // One failed message must not stop the worker.
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

        // Blue ticks + typing indicator. Send the reply even if this fails.
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

        var buttons = scope.ServiceProvider.GetService<ReplyButtons>()?.Value;
        if (buttons is not null)
            await whatsApp.SendInteractiveAsync(message.PhoneNumberId, message.From, reply, buttons, ct);
        else
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
