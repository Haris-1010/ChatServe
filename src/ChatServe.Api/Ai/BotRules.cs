using System.Text.RegularExpressions;

namespace ChatServe.Api.Ai;

/// <summary>
/// The owner's own rules for the bot, written in the dashboard (no code change per client).
/// Corrections from "Test the bot" / Chats ("it should have said ...") are added here as rules.
/// </summary>
public static partial class BotRules
{
    /// <summary>Rules go into every (cached) prompt, so keep them reasonably short.</summary>
    public const int MaxLength = 6000;

    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd());
        var cleaned = ManyBlankLines().Replace(string.Join("\n", lines), "\n\n").Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>A correction as one rule line, e.g. <c>- When a customer says "menu", reply like: "..."</c>. Null without a better reply.</summary>
    public static string? Correction(string? customerMessage, string? botReply, string? betterReply)
    {
        var better = OneLine(betterReply, 600);
        if (better is null) return null;
        var asked = OneLine(customerMessage, 200);
        var wrong = OneLine(botReply, 200);
        var rule = asked is null
            ? $"- Reply like this: \"{better}\""
            : $"- When a customer says something like \"{asked}\", reply like: \"{better}\"";
        if (wrong is not null)
            rule += $" (not: \"{wrong}\")";
        return rule;
    }

    private static string? OneLine(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // "[Choices shown: ...]" is our own note, not something to copy.
        var idx = s.IndexOf("\n\n[Choices shown:", StringComparison.Ordinal);
        if (idx >= 0) s = s[..idx];
        var one = Spaces().Replace(s.Replace('"', '\''), " ").Trim();
        return one.Length <= max ? one : one[..max].TrimEnd() + "…";
    }

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyBlankLines();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
