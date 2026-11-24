namespace ChatServe.Api.Data;

// Multi-tenant: every table has a RestaurantId. The restaurant is identified by the webhook's phone_number_id.

public sealed class Restaurant
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Meta's Phone number ID (not the phone number). The webhook uses it to find the restaurant.</summary>
    public required string WhatsAppPhoneNumberId { get; set; }

    public bool IsBotEnabled { get; set; } = true;

    /// <summary>The bot's tone, e.g. "friendly, brief, Lahori style".</summary>
    public string? BotTone { get; set; }

    public string? Address { get; set; }
    public string? Timings { get; set; }              // "Daily 12 PM to 2 AM"
    public string? DeliveryAreas { get; set; }        // extra delivery notes (areas and charges are in the DeliveryZones table)
    public decimal? MinimumOrder { get; set; }
    public string? PaymentMethods { get; set; }       // "Cash on delivery, JazzCash, Easypaisa"
    public string? ExtraInfo { get; set; }            // anything else the bot should know
    public string Currency { get; set; } = "Rs";

    /// <summary>Kitchen/staff WhatsApp number (with country code, e.g. 923001234567). Confirmed orders, bookings and handovers are sent here.</summary>
    public string? StaffWhatsAppNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<MenuItem> MenuItems { get; set; } = [];
    public List<Deal> Deals { get; set; } = [];
    public List<Faq> Faqs { get; set; } = [];
    public List<DeliveryZone> DeliveryZones { get; set; } = [];
}

/// <summary>A delivery area and its charge. The AI can only take orders for these areas (an enum in the tool schema).</summary>
public sealed class DeliveryZone
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public required string Name { get; set; }
    public decimal Charge { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class MenuItem
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public required string Category { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class Deal
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public required string Name { get; set; }
    public required string Details { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Faq
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public required string Question { get; set; }
    public required string Answer { get; set; }
}

public sealed class Customer
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public required string Phone { get; set; }          // wa_id
    public string? Name { get; set; }                   // WhatsApp profile name
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastMessageAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Human handover: the bot won't reply to this customer until this time (staff will talk).</summary>
    public DateTimeOffset? BotPausedUntil { get; set; }
    // Phase 4: Label, TotalOrders, NoShowCount, IsBlacklisted
}

public enum ChatRole { Customer = 0, Bot = 1, Staff = 2 }

public sealed class ChatMessage
{
    public long Id { get; set; }
    public int RestaurantId { get; set; }
    public int CustomerId { get; set; }
    public ChatRole Role { get; set; }
    public required string Text { get; set; }
    public string? WhatsAppMessageId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // For tracking cost (bot messages only)
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? CacheReadTokens { get; set; }
}

public enum OrderType { Delivery = 0, Takeaway = 1, DineIn = 2 }

public enum OrderStatus
{
    /// <summary>The AI created a draft; waiting for the customer's "confirm". NOT sent to the kitchen.</summary>
    PendingConfirmation = 0,
    Confirmed = 1,
    Preparing = 2,
    OutForDelivery = 3,
    Completed = 4,
    Cancelled = 5,
}

public sealed class Order
{
    public int Id { get; set; }                         // this is the order number the customer sees
    public int RestaurantId { get; set; }
    public int CustomerId { get; set; }
    public OrderStatus Status { get; set; }
    public OrderType Type { get; set; }
    public required string CustomerName { get; set; }
    public required string Phone { get; set; }
    public string? DeliveryAddress { get; set; }
    public string? DeliveryArea { get; set; }
    public string? PaymentMethod { get; set; }
    public string? Notes { get; set; }

    // Calculated by the server from menu prices (the AI's math is never used)
    public decimal Subtotal { get; set; }
    public decimal DeliveryCharge { get; set; }
    public decimal Total { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>The bot message that carried this summary. "1" only counts as confirm right after it.</summary>
    public long? SummaryChatMessageId { get; set; }

    public List<OrderItem> Items { get; set; } = [];
}

public sealed class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int? MenuItemId { get; set; }
    public int? DealId { get; set; }
    public required string Name { get; set; }           // name and price at order time (the record stays correct if the menu changes later)
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public string? Note { get; set; }
}

public enum BookingStatus { Requested = 0, Confirmed = 1, Cancelled = 2 }

public sealed class TableBooking
{
    public int Id { get; set; }
    public int RestaurantId { get; set; }
    public int CustomerId { get; set; }
    public required string CustomerName { get; set; }
    public required string Phone { get; set; }
    public DateTimeOffset BookingTime { get; set; }     // with the Pakistan time offset
    public int People { get; set; }
    public string? Notes { get; set; }
    public BookingStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Settings changed from the dashboard (keys like "WhatsApp:AccessToken"). Loaded last into the app configuration,
/// so they take priority over appsettings/env vars. Not per restaurant (app-wide settings).
/// </summary>
public sealed class AppSetting
{
    public required string Key { get; set; }
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
