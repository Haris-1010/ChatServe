using System.Text.RegularExpressions;
using ChatServe.Api.Data;

namespace ChatServe.Api.Orders;

/// <summary>
/// Catches delivery addresses that name two different places, e.g. "House 34, Paragon City, Johar Town"
/// with delivery area Johar Town, or "DHA ... Johar Town". The AI then asks the customer which one is right
/// instead of sending the rider to the wrong side of the city.
/// </summary>
public static partial class AddressCheck
{
    /// <summary>A message for the AI, or null if the address looks fine.</summary>
    public static string? Problem(string address, DeliveryZone zone, IEnumerable<DeliveryZone> allZones)
    {
        var zones = allZones.Where(z => z.IsActive).ToList();
        var areaList = string.Join(", ", zones.Select(z => z.Name));

        // Another delivery area of ours is named in the address.
        var other = zones.FirstOrDefault(z => z.Id != zone.Id && Mentions(address, z.Name) && !Mentions(zone.Name, z.Name));
        if (other is not null)
            return Ask($"\"{other.Name}\"", zone.Name, areaList);

        // A place like "Paragon City" / "Bahria Town" / "Wapda Society" that isn't the chosen area.
        foreach (Match m in PlaceName().Matches(address))
        {
            var place = m.Value.Trim();
            var core = m.Groups["name"].Value + " " + m.Groups["kind"].Value;
            if (Mentions(place, zone.Name) || Mentions(zone.Name, core))
                continue;
            return Ask($"\"{core}\"", zone.Name, areaList);
        }
        return null;
    }

    private static string Ask(string place, string area, string areaList) =>
        $"The address mentions {place} but the delivery area is {area}. These look like two different places. " +
        $"Don't place the order yet: politely ask the customer which one is correct and tell them we deliver to: {areaList}. " +
        "If they confirm the address and area are right, call place_order again with address_confirmed = true.";

    private static bool Mentions(string text, string name) =>
        Regex.IsMatch(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(name.Trim()) + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase);

    [GeneratedRegex(@"(?<![\p{L}])(?<name>\p{L}+)\s+(?<kind>City|Town|Society|Colony|Housing|Scheme|Enclave|Valley|Gardens?|Villas?|Estate)(?![\p{L}])", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceName();
}
