namespace ChatServe.Api.Data;

// Multi-tenant: har table mein RestaurantId. Restaurant webhook ke phone_number_id se pehchana jata hai.

public sealed class Restaurant
{
    public int Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Meta ka Phone number ID (number nahi). Webhook isi se restaurant dhoondta hai.</summary>
    public required string WhatsAppPhoneNumberId { get; set; }

    public bool IsBotEnabled { get; set; } = true;

    /// <summary>Bot ka lehja, maslan "dostana, mukhtasar, Lahori andaaz".</summary>
    public string? BotTone { get; set; }

    public string? Address { get; set; }
    public string? Timings { get; set; }              // "Rozana 12 PM se 2 AM"
    public string? DeliveryAreas { get; set; }        // delivery ke mazeed notes (areas aur charges ab DeliveryZones table mein)
    public decimal? MinimumOrder { get; set; }
    public string? PaymentMethods { get; set; }       // "Cash on delivery, JazzCash, Easypaisa"
    public string? ExtraInfo { get; set; }            // koi bhi aur baat jo bot ko pata honi chahiye
    public string Currency { get; set; } = "Rs";

    /// <summary>Kitchen/staff ka WhatsApp number (country code ke saath, maslan 923001234567). Confirmed orders, bookings aur handover yahan jaate hain.</summary>
    public string? StaffWhatsAppNumber { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<MenuItem> MenuItems { get; set; } = [];
    public List<Deal> Deals { get; set; } = [];
    public List<Faq> Faqs { get; set; } = [];
    public List<DeliveryZone> DeliveryZones { get; set; } = [];
}

/// <summary>Delivery area aur us ka charge. AI sirf inhi areas mein order le sakta hai (tool schema mein enum).</summary>
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

    /// <summary>Human handover: is waqt tak bot is customer ko jawab nahi dega (staff baat karega).</summary>
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

    // Kharcha track karne ke liye (sirf bot messages par)
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? CacheReadTokens { get; set; }
}

public enum OrderType { Delivery = 0, Takeaway = 1, DineIn = 2 }

public enum OrderStatus
{
    /// <summary>AI ne draft banaya, customer ka "confirm" baqi. Kitchen tak NAHI gaya.</summary>
    PendingConfirmation = 0,
    Confirmed = 1,
    Preparing = 2,
    OutForDelivery = 3,
    Completed = 4,
    Cancelled = 5,
}

public sealed class Order
{
    public int Id { get; set; }                         // customer ko yahi order number dikhta hai
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

    // Server ne menu ki prices se hisaab lagaya (AI ka hisaab kabhi use nahi hota)
    public decimal Subtotal { get; set; }
    public decimal DeliveryCharge { get; set; }
    public decimal Total { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>Wo bot message jis mein ye summary bheji gayi. "1" sirf usi ke foran baad confirm maana jata hai.</summary>
    public long? SummaryChatMessageId { get; set; }

    public List<OrderItem> Items { get; set; } = [];
}

public sealed class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int? MenuItemId { get; set; }
    public int? DealId { get; set; }
    public required string Name { get; set; }           // order ke waqt ka naam aur price (menu baad mein badle to bhi record sahi rahe)
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
    public DateTimeOffset BookingTime { get; set; }     // Pakistan time offset ke saath
    public int People { get; set; }
    public string? Notes { get; set; }
    public BookingStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
