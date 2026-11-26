using System.Text;

namespace ChatServe.Api.WhatsApp;

/// <summary>One tappable option: a reply button, or a row in a list.</summary>
/// <param name="Id">Comes back in the webhook when the customer taps it (e.g. "rate:12:5").</param>
/// <param name="Title">Button: max 20 characters. List row: max 24.</param>
/// <param name="Description">List rows only (max 72).</param>
public sealed record ReplyOption(string Id, string Title, string? Description = null);

/// <summary>
/// WhatsApp buttons under a message (up to 3) or a list (up to 10 rows behind one button).
/// Free to send inside the 24-hour window, no template needed. Outside the window only templates work.
/// </summary>
/// <param name="ListButton">Set = send as a list, with this text on the button that opens it (max 20). Null = reply buttons.</param>
/// <param name="Header">Optional bold line above the text (max 60).</param>
/// <param name="Footer">Optional small grey line under the text (max 60).</param>
public sealed record InteractiveMessage(
    IReadOnlyList<ReplyOption> Options,
    string? ListButton = null,
    string? Header = null,
    string? Footer = null)
{
    public const int MaxButtons = 3;
    public const int MaxRows = 10;
    public const int MaxBodyLength = 1024;

    public bool IsList => ListButton is not null;

    /// <summary>How the message looks without buttons (simulator, or when WhatsApp refuses the buttons).</summary>
    public string AsPlainText(string body)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(Header)) sb.Append('*').Append(Header).AppendLine("*");
        sb.Append(body);
        sb.AppendLine().AppendLine();
        for (var i = 0; i < Options.Count; i++)
        {
            sb.Append(i + 1).Append(". ").Append(Options[i].Title);
            if (IsList && !string.IsNullOrWhiteSpace(Options[i].Description)) sb.Append(" (").Append(Options[i].Description).Append(')');
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(Footer)) sb.AppendLine().Append('_').Append(Footer).AppendLine("_");
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Buttons the reply generator wants under its reply. Scoped (one per incoming message), so the generator can keep
/// returning plain text and the sender (WhatsApp worker or simulator) picks the buttons up from here.
/// </summary>
public sealed class ReplyButtons
{
    public InteractiveMessage? Value { get; set; }
}
