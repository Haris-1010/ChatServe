namespace ChatServe.Api.Ai;

public sealed class AiOptions
{
    public const string SectionName = "Anthropic";

    /// <summary>console.anthropic.com ki API key. Khali ho to SDK ANTHROPIC_API_KEY env var parhta hai.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Sasta aur tez model. Mehenga/behtar chahiye to claude-sonnet-5-5.</summary>
    public string Model { get; set; } = "claude-haiku-5-5";

    /// <summary>Jawab ki max lambai (tokens). WhatsApp replies chhote hone chahiye.</summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>low / medium / high. Chat ke liye low kaafi hai: sasta aur tez.</summary>
    public string Effort { get; set; } = "low";

    /// <summary>Har customer ki aakhri kitni messages AI ko bheji jayen (memory).</summary>
    public int HistoryLimit { get; set; } = 20;

    /// <summary>Ek customer message par zyada se zyada kitni dafa AI tools chala sakta hai (loop se bachao).</summary>
    public int MaxToolRounds { get; set; } = 5;
}
