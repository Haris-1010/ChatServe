using System.Threading.Channels;

namespace ChatServe.Api.Messaging;

public interface IMessageQueue
{
    bool TryEnqueue(IncomingMessage message);
    IAsyncEnumerable<IncomingMessage> ReadAllAsync(CancellationToken ct);
}

/// <summary>
/// In-memory queue so the webhook can return 200 OK right away (Meta resends if it doesn't get a quick 200).
/// Note: messages still in the queue are lost on app restart. In Phase 6,
/// we can move to a DB-backed queue (or Hangfire) if needed.
/// </summary>
public sealed class MessageQueue : IMessageQueue
{
    private readonly Channel<IncomingMessage> _channel = Channel.CreateBounded<IncomingMessage>(
        new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = false,
            SingleWriter = false
        });

    public bool TryEnqueue(IncomingMessage message) => _channel.Writer.TryWrite(message);

    public IAsyncEnumerable<IncomingMessage> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
