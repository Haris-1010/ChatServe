using System.Text;
using System.Text.RegularExpressions;

namespace ChatServe.Api.Ai;

/// <summary>
/// Cleans the example chats a restaurant gives the bot to learn from: WhatsApp "Export chat" files,
/// pasted text and text read from screenshots. Phone numbers are masked (customer privacy) and the size is capped
/// because the examples go into every AI call.
/// </summary>
public static partial class TrainingText
{
    /// <summary>About 5,000 tokens: enough examples to learn the style, small enough to keep every reply cheap.</summary>
    public const int MaxLength = 20_000;

    public static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var t = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("‎", "").Replace("‏", "");
        t = PhoneNumber().Replace(t, "[number]");
        t = ManyBlankLines().Replace(t, "\n\n").Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>
    /// A WhatsApp chat export ("25/11/2026, 12:22 pm - Haris: hello" on Android,
    /// "[25/11/2026, 12:22:01 PM] Haris: hello" on iPhone) as "Haris: hello" lines, without dates, media and system notes.
    /// Text that doesn't look like an export is returned as it is.
    /// </summary>
    public static string FromWhatsAppExport(string export)
    {
        var lines = export.Replace("\r\n", "\n").Replace('\r', '\n').Replace("‎", "").Replace("‏", "").Split('\n');
        var sb = new StringBuilder();
        var matched = 0;
        var keepContinuation = false;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            var m = ExportLine().Match(line);
            if (m.Success)
            {
                matched++;
                var name = m.Groups["name"].Value.Trim();
                var msg = m.Groups["msg"].Value.Trim();
                keepContinuation = !IsNoise(msg);
                if (keepContinuation)
                    sb.Append('\n').Append(name).Append(": ").Append(msg);
            }
            else if (DateOnlyLine().IsMatch(line))
            {
                keepContinuation = false; // "Messages are end-to-end encrypted", "X joined" and other system lines
            }
            else if (keepContinuation && line.Length > 0)
            {
                sb.Append('\n').Append(line); // a message with several lines
            }
        }
        return matched == 0 ? export : sb.ToString().Trim();
    }

    private static bool IsNoise(string msg) =>
        msg.Length == 0
        || msg.StartsWith('<') && msg.EndsWith('>')                                   // <Media omitted>, <attached: ...>
        || msg.EndsWith(" omitted", StringComparison.OrdinalIgnoreCase)              // image omitted, sticker omitted
        || msg.Equals("This message was deleted", StringComparison.OrdinalIgnoreCase)
        || msg.Equals("You deleted this message", StringComparison.OrdinalIgnoreCase)
        || msg.Equals("null", StringComparison.Ordinal);

    [GeneratedRegex(@"^\[?\d{1,2}[/.\-]\d{1,2}[/.\-]\d{2,4},?\s+\d{1,2}[:.]\d{2}(?:[:.]\d{2})?(?:\s*[aApP]\.?\s?[mM]\.?)?\]?\s*(?:-\s*)?(?<name>[^:\n]{1,60}?):\s?(?<msg>.*)$")]
    private static partial Regex ExportLine();

    [GeneratedRegex(@"^\[?\d{1,2}[/.\-]\d{1,2}[/.\-]\d{2,4},?\s+\d{1,2}[:.]\d{2}")]
    private static partial Regex DateOnlyLine();

    // +92 300 1234567, 0300-1234567, 923001234567 (9+ digits, spaces and dashes allowed)
    [GeneratedRegex(@"\+?\d(?:[\s\-]?\d){8,}")]
    private static partial Regex PhoneNumber();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyBlankLines();
}
