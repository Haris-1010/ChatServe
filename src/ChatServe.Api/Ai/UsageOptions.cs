namespace ChatServe.Api.Ai;

/// <summary>
/// Prices used to turn stored token counts into money for the usage page. They are settings (not hard-coded)
/// because they depend on the model and change over time. Defaults: claude-haiku-5-5.
/// </summary>
public sealed class UsageOptions
{
    public const string SectionName = "Usage";

    public decimal InputUsdPerMillion { get; set; } = 0.10m;
    public decimal OutputUsdPerMillion { get; set; } = 0.50m;
    public decimal CacheReadUsdPerMillion { get; set; } = 0.01m;
    public decimal CacheWriteUsdPerMillion { get; set; } = 0.125m;
    public decimal PkrPerUsd { get; set; } = 280m;
    public int WarnAtPercent { get; set; } = 80;
    public string AdminWhatsAppNumber { get; set; } = string.Empty;

    /// <summary>The Anthropic credit balance the admin last typed in (from console.anthropic.com → Billing), in $.</summary>
    public decimal? CreditBalanceUsd { get; set; }

    /// <summary>When that balance was entered: AI use after this time is subtracted from it.</summary>
    public DateTimeOffset? CreditBalanceSetAt { get; set; }

    public decimal CostUsd(long input, long output, long cacheRead, long cacheWrite) =>
        (input * InputUsdPerMillion + output * OutputUsdPerMillion +
         cacheRead * CacheReadUsdPerMillion + cacheWrite * CacheWriteUsdPerMillion) / 1_000_000m;
}
