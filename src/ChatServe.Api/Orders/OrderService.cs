using System.Globalization;
using ChatServe.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChatServe.Api.Orders;

public sealed record DraftItemRequest(string Code, int Quantity, string? Note);

public sealed record DraftRequest(
    IReadOnlyList<DraftItemRequest> Items,
    OrderType Type,
    string CustomerName,
    string? DeliveryAddress,
    string? DeliveryArea,
    string? PaymentMethod,
    string? Notes);

/// <summary>Either the order, or an error (sent back to the AI so it can get the right details from the customer).</summary>
public sealed record ServiceResult<T>(T? Value, string? Error) where T : class
{
    public static ServiceResult<T> Ok(T value) => new(value, null);
    public static ServiceResult<T> Fail(string error) => new(null, error);
}

/// <summary>
/// All business logic for orders and bookings. Prices and totals are always calculated here from the menu;
/// the AI's numbers are never used.
/// </summary>
public sealed class OrderService(AppDbContext db, IOptionsMonitor<OrderOptions> options)
{
    /// <summary>
    /// Creates or replaces the customer's order draft (PendingConfirmation). Any earlier pending draft is removed.
    /// <paramref name="restaurant"/> must have MenuItems, Deals and DeliveryZones loaded.
    /// </summary>
    public async Task<ServiceResult<Order>> SaveDraftAsync(
        Restaurant restaurant, Customer customer, DraftRequest req, DateTimeOffset now, CancellationToken ct)
    {
        var opts = options.CurrentValue;
        var c = restaurant.Currency;

        if (string.IsNullOrWhiteSpace(req.CustomerName))
            return ServiceResult<Order>.Fail("customer_name is missing. Ask the customer for their name.");
        if (req.Items.Count == 0)
            return ServiceResult<Order>.Fail("The order has no items.");
        if (req.Items.Count > 30)
            return ServiceResult<Order>.Fail("Too many lines in one order. Ask the customer to call the restaurant for bulk orders.");

        var items = new List<OrderItem>();
        foreach (var line in req.Items)
        {
            if (line.Quantity < 1 || line.Quantity > opts.MaxQuantityPerItem)
                return ServiceResult<Order>.Fail(
                    $"Quantity {line.Quantity} for {line.Code} is not allowed (1 to {opts.MaxQuantityPerItem}). For bulk orders hand over to staff.");

            var item = ResolveItem(restaurant, line.Code);
            if (item.Error is not null)
                return ServiceResult<Order>.Fail(item.Error);

            items.Add(new OrderItem
            {
                MenuItemId = item.MenuItemId,
                DealId = item.DealId,
                Name = item.Name!,
                UnitPrice = item.Price,
                Quantity = line.Quantity,
                LineTotal = item.Price * line.Quantity,
                Note = Clean(line.Note, 200),
            });
        }

        var subtotal = items.Sum(i => i.LineTotal);
        decimal deliveryCharge = 0;
        string? area = null, address = null;

        if (req.Type == OrderType.Delivery)
        {
            var zone = restaurant.DeliveryZones.FirstOrDefault(z =>
                z.IsActive && string.Equals(z.Name, req.DeliveryArea?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (zone is null)
                return ServiceResult<Order>.Fail(
                    "We don't deliver to that area. Tell the customer the delivery areas, or offer takeaway.");

            address = Clean(req.DeliveryAddress, 300);
            if (address is null || address.Length < 5)
                return ServiceResult<Order>.Fail("Full delivery address (house/street/block) is missing. Ask the customer for it.");

            if (restaurant.MinimumOrder is { } min && subtotal < min)
                return ServiceResult<Order>.Fail(
                    $"Subtotal {c} {Money.Format(subtotal)} is below the minimum delivery order of {c} {Money.Format(min)}. Tell the customer and suggest adding items or takeaway.");

            area = zone.Name;
            deliveryCharge = zone.Charge;
        }

        // Remove the old pending draft: a customer has only one draft (it never reached the kitchen).
        var oldDrafts = await db.Orders
            .Where(o => o.CustomerId == customer.Id && o.Status == OrderStatus.PendingConfirmation)
            .ToListAsync(ct);
        db.Orders.RemoveRange(oldDrafts);

        var order = new Order
        {
            RestaurantId = restaurant.Id,
            CustomerId = customer.Id,
            Status = OrderStatus.PendingConfirmation,
            Type = req.Type,
            CustomerName = Clean(req.CustomerName, 100)!,
            Phone = customer.Phone,
            DeliveryAddress = address,
            DeliveryArea = area,
            PaymentMethod = Clean(req.PaymentMethod, 50),
            Notes = Clean(req.Notes, 500),
            Subtotal = subtotal,
            DeliveryCharge = deliveryCharge,
            Total = subtotal + deliveryCharge,
            CreatedAt = now,
            UpdatedAt = now,
            Items = items,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return ServiceResult<Order>.Ok(order);
    }

    /// <summary>The customer's latest pending draft (not expired), or null.</summary>
    public Task<Order?> GetPendingDraftAsync(int customerId, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now.AddMinutes(-options.CurrentValue.DraftExpiryMinutes);
        return db.Orders
            .Include(o => o.Items)
            .Where(o => o.CustomerId == customerId && o.Status == OrderStatus.PendingConfirmation && o.UpdatedAt > cutoff)
            .OrderByDescending(o => o.UpdatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task ConfirmAsync(Order order, DateTimeOffset now, CancellationToken ct)
    {
        order.Status = OrderStatus.Confirmed;
        order.ConfirmedAt = now;
        order.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelDraftAsync(Order order, DateTimeOffset now, CancellationToken ct)
    {
        order.Status = OrderStatus.Cancelled;
        order.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The customer's recent orders (excluding drafts), newest first.</summary>
    public Task<List<Order>> GetRecentOrdersAsync(int customerId, int count, CancellationToken ct) =>
        db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.CustomerId == customerId && o.Status != OrderStatus.PendingConfirmation)
            .OrderByDescending(o => o.CreatedAt)
            .Take(count)
            .ToListAsync(ct);

    public async Task<ServiceResult<TableBooking>> BookTableAsync(
        Restaurant restaurant, Customer customer, string name, string date, string time, int people, string? notes,
        DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ServiceResult<TableBooking>.Fail("customer_name is missing. Ask the customer for their name.");
        if (people < 1 || people > 50)
            return ServiceResult<TableBooking>.Fail("Number of people must be 1 to 50. For bigger events hand over to staff.");

        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ||
            !TimeOnly.TryParseExact(time, ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return ServiceResult<TableBooking>.Fail("Use date as YYYY-MM-DD and time as 24-hour HH:MM (Pakistan time).");

        var when = new DateTimeOffset(d.ToDateTime(t), Money.PakistanOffset);
        if (when <= now)
            return ServiceResult<TableBooking>.Fail("That date/time has already passed. Check the current time and ask the customer again.");
        if (when > now.AddDays(options.CurrentValue.MaxBookingDaysAhead))
            return ServiceResult<TableBooking>.Fail(
                $"Bookings can only be made up to {options.CurrentValue.MaxBookingDaysAhead} days ahead.");

        var booking = new TableBooking
        {
            RestaurantId = restaurant.Id,
            CustomerId = customer.Id,
            CustomerName = Clean(name, 100)!,
            Phone = customer.Phone,
            BookingTime = when,
            People = people,
            Notes = Clean(notes, 500),
            Status = BookingStatus.Requested,
            CreatedAt = now,
        };
        db.TableBookings.Add(booking);
        await db.SaveChangesAsync(ct);
        return ServiceResult<TableBooking>.Ok(booking);
    }

    public async Task PauseBotAsync(Customer customer, DateTimeOffset now, CancellationToken ct)
    {
        customer.BotPausedUntil = now.AddHours(options.CurrentValue.HandoverPauseHours);
        await db.SaveChangesAsync(ct);
    }

    private sealed record ResolvedItem(int? MenuItemId, int? DealId, string? Name, decimal Price, string? Error);

    /// <summary>"M12" = menu item 12, "D3" = deal 3 (the codes used in the system prompt).</summary>
    private static ResolvedItem ResolveItem(Restaurant r, string code)
    {
        code = (code ?? "").Trim().TrimStart('[').TrimEnd(']').ToUpperInvariant();
        if (code.Length >= 2 && int.TryParse(code[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            if (code[0] == 'M' && r.MenuItems.FirstOrDefault(m => m.Id == id) is { } m)
            {
                return m.IsAvailable
                    ? new ResolvedItem(m.Id, null, m.Name, m.Price, null)
                    : new ResolvedItem(null, null, null, 0, $"{m.Name} is NOT AVAILABLE today. Suggest a similar available item.");
            }

            if (code[0] == 'D' && r.Deals.FirstOrDefault(x => x.Id == id && x.IsActive) is { } deal)
                return new ResolvedItem(null, deal.Id, deal.Name, deal.Price, null);
        }

        return new ResolvedItem(null, null, null, 0, $"Unknown item code \"{code}\". Use only the [M..]/[D..] codes from the menu.");
    }

    private static string? Clean(string? value, int max)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length <= max ? v : v[..max];
    }
}
