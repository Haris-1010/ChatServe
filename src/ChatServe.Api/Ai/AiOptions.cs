namespace ChatServe.Api.Ai;

public sealed class AiOptions
{
    public const string SectionName = "Anthropic";

    /// <summary>API key from console.anthropic.com. If empty, the SDK reads the ANTHROPIC_API_KEY env var.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>A cheap, fast model. Use claude-sonnet-5-5 for a smarter (more expensive) one.</summary>
    public string Model { get; set; } = "claude-haiku-5-5";

    /// <summary>Max reply length (tokens). WhatsApp replies should be short.</summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>low / medium / high. low is enough for chat: cheaper and faster.</summary>
    public string Effort { get; set; } = "low";

    /// <summary>How many of each customer's latest messages are sent to the AI (memory).</summary>
    public int HistoryLimit { get; set; } = 20;

    /// <summary>Max number of tool rounds per customer message (prevents loops).</summary>
    public int MaxToolRounds { get; set; } = 5;
}
