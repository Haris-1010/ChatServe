namespace ChatServe.Api.Orders;

public sealed class OrderOptions
{
    public const string SectionName = "Orders";

    /// <summary>Itne minute tak "confirm" ka intezar. Is ke baad draft purana maana jata hai aur naya order banana parta hai.</summary>
    public int DraftExpiryMinutes { get; set; } = 180;

    /// <summary>handover_to_human ke baad bot itne ghante us customer ko jawab nahi dega (staff baat karega).</summary>
    public int HandoverPauseHours { get; set; } = 12;

    /// <summary>Ek order mein zyada se zyada itni dafa ek item (fake/mazaq wale orders se bachao).</summary>
    public int MaxQuantityPerItem { get; set; } = 30;

    /// <summary>Table booking kitne din aage tak ho sakti hai.</summary>
    public int MaxBookingDaysAhead { get; set; } = 30;
}
