namespace ChatServe.Api.Orders;

/// <summary>
/// A restaurant can have several staff/kitchen WhatsApp numbers (manager, kitchen, rider...).
/// They are stored in Restaurant.StaffWhatsAppNumber as digits separated by commas: "923001234567,923331234567".
/// </summary>
public static class StaffNumbers
{
    public const int MaxNumbers = 10;
    public const int MaxStoredLength = 200; // 10 x 15 digits + commas

    private static readonly char[] Separators = [',', ';', '/', '\n', '\r'];

    /// <summary>The stored value as a list of numbers (digits only).</summary>
    public static IReadOnlyList<string> Parse(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? []
            : stored.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Digits)
                .Where(d => d.Length > 0)
                .Distinct()
                .ToList();

    public static bool Contains(string? stored, string phone)
    {
        var p = Digits(phone);
        return p.Length > 0 && Parse(stored).Contains(p);
    }

    /// <summary>
    /// Cleans what was typed in the dashboard ("+92 300 1234567, 92333-1234567") into the stored form.
    /// Returns an error message (and no value) if a number is not valid.
    /// </summary>
    public static (string? Value, string? Error) Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (null, null);

        var numbers = new List<string>();
        foreach (var part in input.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var d = Digits(part);
            if (d.Length == 0)
                continue;
            if (d.StartsWith("00", StringComparison.Ordinal))
                d = d[2..]; // 0092300... → 92300...
            if (d.Length is < 10 or > 15 || d[0] == '0')
                return (null, $"\"{part}\" is not valid. Enter each staff number with the country code, e.g. 923001234567.");
            if (!numbers.Contains(d))
                numbers.Add(d);
        }

        if (numbers.Count > MaxNumbers)
            return (null, $"At most {MaxNumbers} staff numbers.");
        return (numbers.Count == 0 ? null : string.Join(",", numbers), null);
    }

    private static string Digits(string s) => new(s.Where(char.IsAsciiDigit).ToArray());
}
