using System.Globalization;

namespace ChatServe.Api.Orders;

public static class Money
{
    /// <summary>2600 → "2,600". The same format in the prompt, summaries and tickets (so AI and server numbers match).</summary>
    public static string Format(decimal v) => v.ToString("#,0.##", CultureInfo.InvariantCulture);

    /// <summary>Pakistan time (UTC+5, no daylight saving).</summary>
    public static readonly TimeSpan PakistanOffset = TimeSpan.FromHours(5);
}
