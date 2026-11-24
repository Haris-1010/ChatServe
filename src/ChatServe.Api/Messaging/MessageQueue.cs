using System.Threading.Channels;

namespace ChatServe.Api.Messaging;

public interface IMessageQueue
{
    bool TryEnqueue(IncomingMessage message);
    IAsyncEnumerable<IncomingMessage> ReadAllAsync(CancellationToken ct);
}

/// <summary>
/// In-memory queue taake webhook foran 200 OK de sake (Meta ko 200 jaldi na mile to woh dobara bhejta hai).
/// Note: app restart par queue mein pade messages khatam ho jayenge. Phase 6 mein
/// zaroorat pade to DB-backed queue (ya Hangfire) par shift kar sakte hain.
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
