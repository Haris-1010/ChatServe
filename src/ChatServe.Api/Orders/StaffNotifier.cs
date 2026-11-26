using ChatServe.Api.Data;
using ChatServe.Api.WhatsApp;

namespace ChatServe.Api.Orders;

/// <summary>Notifies the kitchen/staff about orders, bookings and handovers.</summary>
public interface IStaffNotifier
{
    /// <param name="exceptPhone">A staff number to skip (e.g. the one who sent the command).</param>
    /// <param name="buttons">Buttons under the message (e.g. Prep / Rider gaya / Delivered on a new order).</param>
    Task NotifyAsync(Restaurant restaurant, string text, CancellationToken ct, string? exceptPhone = null, InteractiveMessage? buttons = null);
}

/// <summary>
/// A WhatsApp message to every one of the restaurant's staff numbers (StaffWhatsAppNumber, comma-separated). Note: Meta only delivers free-form text
/// if the staff number has messaged the business number in the last 24 hours. So staff should send
/// a "start" message every morning (each staff number for itself), or use an approved utility template (Phase 7).
/// The order is saved in the DB either way (live orders on the Phase 5 dashboard).
/// </summary>
public sealed class WhatsAppStaffNotifier(IWhatsAppClient whatsApp, SimulationContext simulation, ILogger<WhatsAppStaffNotifier> logger) : IStaffNotifier
{
    public async Task NotifyAsync(Restaurant restaurant, string text, CancellationToken ct, string? exceptPhone = null, InteractiveMessage? buttons = null)
    {
        if (simulation.IsActive)
        {
            simulation.StaffAlerts.Add(buttons is null ? text : buttons.AsPlainText(text)); // dashboard simulator: don't send real WhatsApp
            return;
        }

        var numbers = StaffNumbers.Parse(restaurant.StaffWhatsAppNumber);
        if (numbers.Count == 0)
        {
            logger.LogWarning("Restaurant {Restaurant} has no StaffWhatsAppNumber; staff alert only logged:\n{Text}", restaurant.Name, text);
            return;
        }

        foreach (var number in numbers)
        {
            if (exceptPhone is not null && number == exceptPhone)
                continue;
            try
            {
                if (buttons is null)
                    await whatsApp.SendTextAsync(restaurant.WhatsAppPhoneNumberId, number, text, replyToMessageId: null, ct);
                else
                    await whatsApp.SendInteractiveAsync(restaurant.WhatsAppPhoneNumberId, number, text, buttons, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // If the 24-hour window is closed Meta returns an error (131047). The other numbers and the customer's flow must not stop.
                logger.LogError(ex, "Staff alert to {Restaurant} ({Number}) failed (24h window closed?)", restaurant.Name, number);
            }
        }
    }
}

/// <summary>For the dashboard's chat simulator (scoped). When active, staff alerts are collected here instead of going to WhatsApp.</summary>
/// <summary>A message for the staff numbers, optionally with buttons.</summary>
public sealed record StaffAlert(string Text, InteractiveMessage? Buttons = null)
{
    public static implicit operator StaffAlert(string text) => new(text);
}

public sealed class SimulationContext
{
    public bool IsActive { get; set; }
    public List<string> StaffAlerts { get; } = [];
}
