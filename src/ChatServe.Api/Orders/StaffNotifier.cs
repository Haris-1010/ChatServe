using ChatServe.Api.Data;
using ChatServe.Api.WhatsApp;

namespace ChatServe.Api.Orders;

/// <summary>Notifies the kitchen/staff about orders, bookings and handovers.</summary>
public interface IStaffNotifier
{
    Task NotifyAsync(Restaurant restaurant, string text, CancellationToken ct);
}

/// <summary>
/// A WhatsApp message to the restaurant's StaffWhatsAppNumber. Note: Meta only delivers free-form text
/// if the staff number has messaged the business number in the last 24 hours. So staff should send
/// a "start" message every morning, or use an approved utility template (Phase 7).
/// The order is saved in the DB either way (live orders on the Phase 5 dashboard).
/// </summary>
public sealed class WhatsAppStaffNotifier(IWhatsAppClient whatsApp, SimulationContext simulation, ILogger<WhatsAppStaffNotifier> logger) : IStaffNotifier
{
    public async Task NotifyAsync(Restaurant restaurant, string text, CancellationToken ct)
    {
        if (simulation.IsActive)
        {
            simulation.StaffAlerts.Add(text); // dashboard simulator: don't send real WhatsApp
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
            // If the 24-hour window is closed Meta returns an error (131047). The customer's flow must not stop.
            logger.LogError(ex, "Staff alert to {Restaurant} failed (24h window closed?)", restaurant.Name);
        }
    }
}

/// <summary>For the dashboard's chat simulator (scoped). When active, staff alerts are collected here instead of going to WhatsApp.</summary>
public sealed class SimulationContext
{
    public bool IsActive { get; set; }
    public List<string> StaffAlerts { get; } = [];
}
