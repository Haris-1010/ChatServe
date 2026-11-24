namespace ChatServe.Api.Orders;

public sealed class OrderOptions
{
    public const string SectionName = "Orders";

    /// <summary>Minutes to wait for "confirm". After this the draft is stale and a new order is needed.</summary>
    public int DraftExpiryMinutes { get; set; } = 180;

    /// <summary>After handover_to_human, the bot won't reply to that customer for this many hours (staff will talk).</summary>
    public int HandoverPauseHours { get; set; } = 12;

    /// <summary>Max quantity of one item in an order (protects against fake or joke orders).</summary>
    public int MaxQuantityPerItem { get; set; } = 30;

    /// <summary>How many days ahead a table can be booked.</summary>
    public int MaxBookingDaysAhead { get; set; } = 30;
}
