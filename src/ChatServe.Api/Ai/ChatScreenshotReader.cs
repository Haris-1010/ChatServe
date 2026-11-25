using Anthropic.Models.Messages;
using ChatServe.Api.Configuration;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Ai;

/// <summary>Reads the text of a WhatsApp chat screenshot (for bot training), so owners can teach the bot from old chats.</summary>
public interface IChatScreenshotReader
{
    /// <summary>The conversation as "Customer: ..." / "Restaurant: ..." lines, or null if no chat was found.</summary>
    Task<string?> ReadAsync(byte[] image, string mediaType, CancellationToken ct);
}

public sealed class ClaudeChatScreenshotReader(AnthropicClientProvider clients, IOptionsMonitor<AiOptions> options) : IChatScreenshotReader
{
    public static readonly string[] MediaTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];

    private const string Instructions =
        "This is a screenshot of a WhatsApp chat between a restaurant and a customer. " +
        "Write out the conversation exactly as it appears, one message per line, oldest first. " +
        "Start each line with \"Restaurant:\" for messages sent by the restaurant (the green/right-side bubbles) " +
        "or \"Customer:\" for messages from the customer (the left-side bubbles). " +
        "Keep the original wording, spelling and language (Roman Urdu, Urdu, English). " +
        "Leave out times, dates, ticks, the contact name bar, phone numbers and the keyboard. " +
        "Reply with only the conversation lines. If there is no chat in the image, reply with exactly: NO_CHAT";

    public async Task<string?> ReadAsync(byte[] image, string mediaType, CancellationToken ct)
    {
        var opts = options.CurrentValue;
        var response = await clients.Get().Messages.Create(new MessageCreateParams
        {
            Model = opts.Model,
            MaxTokens = 2000,
            Messages =
            [
                new UserMessageParam
                {
                    Content = new List<ContentBlockParam>
                    {
                        new ImageBlockParam
                        {
                            Source = new Base64ImageSource { Data = Convert.ToBase64String(image), MediaType = mediaType },
                        },
                        new TextBlockParam { Text = Instructions },
                    },
                },
            ],
        }, cancellationToken: ct);

        var text = string.Join("\n", response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();
        return text.Length == 0 || text.Contains("NO_CHAT", StringComparison.Ordinal) ? null : text;
    }
}
