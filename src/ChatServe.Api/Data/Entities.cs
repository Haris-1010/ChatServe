namespace ChatServe.Api.Data;

// Multi-tenant: every table has a RestaurantId. The restaurant is identified by the webhook's phone_number_id.

/// <summary>
/// A client (business) that owns one or more restaurants/branches. Owner logins belong to an organization
/// and see only that organization's restaurants; the admin sees every organization.
/// </summary>
public sealed class Organization
{
    public int Id { get; set; }
    public required string Name { get; set; }
    /// <summary>false = suspended: its owners can't log in (e.g. unpaid). The bot keeps its own ON/OFF per restaurant.</summary>
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>AI replies included in the client's package per month. Only a warning when it runs out; the bot keeps working.</summary>
    public int? MonthlyReplyLimit { get; set; }

    /// <summary>Which usage alert the admin already got this month (so each one is sent once): month "2026-11", level 1 = warning, 2 = limit reached.</summary>
    public string? UsageAlertMonth { get; set; }
    public int UsageAlertLevel { get; set; }

    /// <summary>Extra AI replies for one month only (an approved top-up): <see cref="ExtraRepliesMonth"/> is "2026-11".</summary>
    public int ExtraReplies { get; set; }
    public string? ExtraRepliesMonth { get; set; }

    /// <summary>The replies allowed in a month: the package plus that month's top-up. Null = no limit.</summary>
    public int? ReplyLimitFor(string month) =>
        MonthlyReplyLimit is { } l ? l + (ExtraRepliesMonth == month ? ExtraReplies : 0) : null;

    /// <summary>This month's top-up (0 if none).</summary>
    public int ExtraFor(string month) => ExtraRepliesMonth == month ? ExtraReplies : 0;

    public List<Restaurant> Restaurants { get; set; } = [];
}

/// <summary>The kind of business. Stored as a number: only add new values at the end.</summary>
public enum BusinessType
{
    Restaurant = 0,
    /// <summary>Shop / online store: products, delivery or shipping.</summary>
    Shop = 1,
    /// <summary>Salon, clinic, gym, consultant...: services and appointments.</summary>
    Services = 2,
    Other = 3,
}

public enum ReplyRequestKind
{
    /// <summary>Extra replies for the current month only.</summary>
    TopUp = 0,
    /// <summary>A bigger monthly package from now on.</summary>
    Package = 1,
}

public enum ReplyRequestStatus { Pending = 0, Approved = 1, Rejected = 2 }

/// <summary>A client asking for more AI replies. The admin approves (maybe with another amount) or rejects it.</summary>
public sealed class ReplyRequest
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
    public required string RequestedBy { get; set; }      // the owner's username
    public int Replies { get; set; }                      // how many more replies they ask for
    public ReplyRequestKind Kind { get; set; }
    public string? Note { get; set; }
    public ReplyRequestStatus Status { get; set; }
    public int? ApprovedReplies { get; set; }
    public string? AdminNote { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
}

public sealed class Restaurant
{
    public int Id { get; set; }
    public int OrganizationId { get; set; }
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

    /// <summary>
    /// Example chats the owner pasted or imported (old WhatsApp chats, screenshots read by the AI) so the bot learns
    /// the restaurant's way of talking. Phone numbers are masked before saving. Goes into the system prompt.
    /// </summary>
    public string? TrainingExamples { get; set; }

    // ---- Bot setup from the portal (no code changes needed per client) ----

    /// <summary>What kind of business this is. Changes the bot's words (menu vs products vs services) and its default flow.</summary>
    public BusinessType BusinessType { get; set; } = BusinessType.Restaurant;

    /// <summary>One or two lines about the business, e.g. "Ladies clothing brand, Lahore, ships all over Pakistan".</summary>
    public string? BusinessDescription { get; set; }

    /// <summary>The owner's own rules for the bot, one per line (e.g. "Always offer the family deal"). Followed before the default style.</summary>
    public string? BotInstructions { get; set; }

    /// <summary>How the bot greets a new customer. The bot adapts it to the customer's language.</summary>
    public string? WelcomeMessage { get; set; }

    /// <summary>false = the bot never takes orders (info only, or orders handled by staff).</summary>
    public bool TakesOrders { get; set; } = true;

    /// <summary>false = no table booking / appointment requests.</summary>
    public bool TakesBookings { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // ---- WhatsApp connection ----

    /// <summary>
    /// CloudApi: the number lives only on the API (the classic way; it can't be used in the WhatsApp app).
    /// Coexistence: the same number also stays in the owner's WhatsApp Business app; replies the owner sends
    /// from the phone reach us as "echoes" and pause the bot for that customer.
    /// </summary>
    public WhatsAppMode WhatsAppMode { get; set; } = WhatsAppMode.CloudApi;

    /// <summary>The WhatsApp Business Account (WABA) the number belongs to. Set by Embedded Signup.</summary>
    public string? WhatsAppBusinessAccountId { get; set; }

    /// <summary>
    /// The business token Meta gave for this number's WABA (Embedded Signup). Null = the app-wide token in Settings
    /// is used (the classic setup). Never sent to the dashboard.
    /// </summary>
    public string? WhatsAppAccessToken { get; set; }

    public DateTimeOffset? WhatsAppConnectedAt { get; set; }

    /// <summary>When we last asked Meta to send the phone's contacts and chat history (allowed within 24 hours of onboarding).</summary>
    public DateTimeOffset? HistorySyncRequestedAt { get; set; }

    /// <summary>How many old messages were imported from the phone so far.</summary>
    public int HistoryMessagesImported { get; set; }

    /// <summary>Coexistence: pause the bot for a customer when the owner replies to them from the phone.</summary>
    public bool PauseBotOnPhoneReply { get; set; } = true;

    /// <summary>Last time the owner replied from the WhatsApp Business app (coexistence echo).</summary>
    public DateTimeOffset? LastPhoneReplyAt { get; set; }

    public List<MenuItem> MenuItems { get; set; } = [];
    public List<Deal> Deals { get; set; } = [];
    public List<Faq> Faqs { get; set; } = [];
    public List<DeliveryZone> DeliveryZones { get; set; } = [];
}

public enum WhatsAppMode { CloudApi = 0, Coexistence = 1 }

/// <summary>
/// A contact saved in the owner's WhatsApp Business app (coexistence contact sync). Only used to show a name for
/// customers; contacts are not shown as customers until they message.
/// </summary>
public sealed class PhoneContact
{
    public long Id { get; set; }
    public int RestaurantId { get; set; }
    public required string Phone { get; set; }      // digits only, with country code
    public required string Name { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
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
    public int? CacheWriteTokens { get; set; }
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

    // ---- After delivery: the customer says they got it, then rates it ----

    /// <summary>When the customer said they received the order ("mil gaya" / the button). Null = not yet.</summary>
    public DateTimeOffset? ReceivedAt { get; set; }

    /// <summary>1 to 5 stars from the customer.</summary>
    public int? Rating { get; set; }
    public string? FeedbackComment { get; set; }
    public DateTimeOffset? FeedbackAt { get; set; }

    /// <summary>The bot message that asked for the rating or the comment: a plain "5" or a comment only counts right after it.</summary>
    public long? FeedbackPromptMessageId { get; set; }

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

/// <summary>
/// A client's dashboard login. Owners only see and manage the restaurants of their own organization.
/// The admin is not stored here: the admin logs in with Dashboard:AdminPassword.
/// </summary>
public sealed class DashboardUser
{
    public int Id { get; set; }
    public required string Username { get; set; }          // stored lowercase, unique
    public required string PasswordHash { get; set; }      // ASP.NET Core PasswordHasher (PBKDF2)
    public int OrganizationId { get; set; }
    /// <summary>Changes on password reset, so old login tokens stop working.</summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}

/// <summary>AI calls that are not a bot reply (e.g. reading a training screenshot), so they count in the usage report too.</summary>
public sealed class AiUsageLog
{
    public long Id { get; set; }
    public int RestaurantId { get; set; }
    public required string Kind { get; set; }       // "training-screenshot"
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int CacheReadTokens { get; set; }
    public int CacheWriteTokens { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
