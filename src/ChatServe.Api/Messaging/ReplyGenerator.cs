using ChatServe.Api.Configuration;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Messaging;

/// <summary>
/// Builds the reply to a customer message. Phase 1 uses a simple "hello" reply;
/// Phase 2 adds the Claude implementation of this interface (menu + chat memory + tools).
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
                "Sorry, I can only read text messages for now. Please type your question. 🙏");
        }

        var greeting = string.IsNullOrWhiteSpace(message.CustomerName) ? "" : $"{message.CustomerName}, ";
        var reply = greeting + string.Format(options.CurrentValue.WelcomeMessage, message.Text.Trim());
        return Task.FromResult<string?>(reply);
    }
}

/// <summary>Checks Bot:UseAi on every message, so turning AI ON/OFF from the dashboard applies immediately.</summary>
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
