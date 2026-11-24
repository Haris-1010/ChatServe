using System.Globalization;

namespace ChatServe.Api.Orders;

public static class Money
{
    /// <summary>2600 → "2,600". Prompt, summary aur tickets sab mein yahi format (AI aur server ka hisaab match ho).</summary>
    public static string Format(decimal v) => v.ToString("#,0.##", CultureInfo.InvariantCulture);

    /// <summary>Pakistan time (UTC+5, daylight saving nahi).</summary>
    public static readonly TimeSpan PakistanOffset = TimeSpan.FromHours(5);
}
