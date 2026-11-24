using ChatServe.Api.Data;
using ChatServe.Api.WhatsApp;

namespace ChatServe.Api.Orders;

/// <summary>Kitchen/staff tak order, booking aur handover ki khabar pohanchana.</summary>
public interface IStaffNotifier
{
    Task NotifyAsync(Restaurant restaurant, string text, CancellationToken ct);
}

/// <summary>
/// Restaurant ke StaffWhatsAppNumber par WhatsApp message. Note: Meta free-form text sirf tab deliver karta hai
/// jab staff number ne pichhle 24 ghante mein business number ko message kiya ho. Is liye staff roz subah
/// ek "start" message bhej de, ya Phase 7 mein approved utility template use karein.
/// Order DB mein phir bhi save hota hai (Phase 5 dashboard par live orders).
/// </summary>
public sealed class WhatsAppStaffNotifier(IWhatsAppClient whatsApp, SimulationContext simulation, ILogger<WhatsAppStaffNotifier> logger) : IStaffNotifier
{
    public async Task NotifyAsync(Restaurant restaurant, string text, CancellationToken ct)
    {
        if (simulation.IsActive)
        {
            simulation.StaffAlerts.Add(text); // dashboard simulator: asli WhatsApp nahi bhejna
            return;
        }

        if (string.IsNullOrWhiteSpace(restaurant.StaffWhatsAppNumber))
        {
            logger.LogWarning("Restaurant {Restaurant} has no StaffWhatsAppNumber; staff alert only logged:\n{Text}", restaurant.Name, text);
            return;
        }

        try
        {
            await whatsApp.SendTextAsync(restaurant.WhatsAppPhoneNumberId, restaurant.StaffWhatsAppNumber, text, replyToMessageId: null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 24-hour window band ho to Meta error deta hai (131047). Customer ka flow nahi rukna chahiye.
            logger.LogError(ex, "Staff alert to {Restaurant} failed (24h window closed?)", restaurant.Name);
        }
    }
}

/// <summary>Dashboard ke chat simulator ke liye (scoped). Active ho to staff alerts WhatsApp ki jagah yahan jama hote hain.</summary>
public sealed class SimulationContext
{
    public bool IsActive { get; set; }
    public List<string> StaffAlerts { get; } = [];
}
