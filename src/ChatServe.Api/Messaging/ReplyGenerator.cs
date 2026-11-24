using ChatServe.Api.Configuration;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Messaging;

/// <summary>
/// Customer message ka jawab banata hai. Phase 1 mein simple "hello" reply,
/// Phase 2 mein isi interface ka Claude wala implementation aayega (menu + chat memory + tools).
/// </summary>
public interface IReplyGenerator
{
    Task<string?> GenerateReplyAsync(IncomingMessage message, CancellationToken ct);
}

public sealed class HelloReplyGenerator(IOptionsMonitor<BotOptions> options) : IReplyGenerator
{
    public Task<string?> GenerateReplyAsync(IncomingMessage message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.Text))
        {
            return Task.FromResult<string?>(
                "Maazrat, abhi main sirf text messages samajh sakta hoon. Meharbani kar ke apna sawal likh kar bhejein. 🙏");
        }

        var greeting = string.IsNullOrWhiteSpace(message.CustomerName) ? "" : $"{message.CustomerName}, ";
        var reply = greeting + string.Format(options.CurrentValue.WelcomeMessage, message.Text.Trim());
        return Task.FromResult<string?>(reply);
    }
}

/// <summary>Bot:UseAi har message par dekhta hai, taake dashboard se AI ON/OFF foran lagu ho.</summary>
public sealed class SwitchingReplyGenerator(IOptionsMonitor<BotOptions> options, IServiceProvider services) : IReplyGenerator
{
    public Task<string?> GenerateReplyAsync(IncomingMessage message, CancellationToken ct)
    {
        IReplyGenerator inner = options.CurrentValue.UseAi
            ? services.GetRequiredService<ClaudeReplyGenerator>()
            : services.GetRequiredService<HelloReplyGenerator>();
        return inner.GenerateReplyAsync(message, ct);
    }
}
