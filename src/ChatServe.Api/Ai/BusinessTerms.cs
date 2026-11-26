using ChatServe.Api.Data;

namespace ChatServe.Api.Ai;

/// <summary>
/// The words the bot uses for each kind of business, so a clothing shop's bot talks about "products",
/// a salon's about "services" and "appointments", and a restaurant's about the "menu" and "table bookings".
/// </summary>
public sealed record BusinessTerms(
    string Business,         // "restaurant"
    string Item,             // "menu item"
    string AnItem,           // "a dish"
    string Items,            // "dishes"
    string ItemsTitle,       // "Dishes"
    string MenuTitle,        // "Menu"
    string MenuButton,       // "menu"
    string BookingTitle,     // "Table booking"
    string BookingWord,      // "table booking"
    bool BookingHasPeople)
{
    public static BusinessTerms For(BusinessType type) => type switch
    {
        BusinessType.Shop => new("shop", "product", "a product", "products", "Products", "Products", "products",
            "Appointment / visit booking", "booking", false),
        BusinessType.Services => new("business", "service", "a service", "services", "Services", "Services", "services",
            "Appointment booking", "appointment", false),
        BusinessType.Other => new("business", "item", "an item", "products or services", "Items", "Products and services", "products/services",
            "Booking", "booking", false),
        _ => new("restaurant", "menu item", "a dish", "dishes", "Dishes", "Menu", "menu",
            "Table booking", "table booking", true),
    };
}
