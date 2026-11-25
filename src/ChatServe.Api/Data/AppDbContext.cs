using ChatServe.Api.Orders;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ChatServe.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Deal> Deals => Set<Deal>();
    public DbSet<Faq> Faqs => Set<Faq>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<DeliveryZone> DeliveryZones => Set<DeliveryZone>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<TableBooking> TableBookings => Set<TableBooking>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<DashboardUser> DashboardUsers => Set<DashboardUser>();
    public DbSet<AiUsageLog> AiUsageLogs => Set<AiUsageLog>();
    /// <summary>Login-token keys, kept in the DB so logins survive redeploys (Railway starts a fresh container each time).</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Organization>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.MonthlyAiBudget).HasPrecision(12, 2);
        });

        b.Entity<AiUsageLog>(e =>
        {
            e.HasOne<Restaurant>().WithMany().HasForeignKey(x => x.RestaurantId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Kind).HasMaxLength(40);
            e.HasIndex(x => new { x.RestaurantId, x.CreatedAt });
        });

        b.Entity<Restaurant>(e =>
        {
            e.HasOne<Organization>().WithMany(o => o.Restaurants).HasForeignKey(x => x.OrganizationId);
            e.HasIndex(x => x.WhatsAppPhoneNumberId).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.WhatsAppPhoneNumberId).HasMaxLength(50);
            e.Property(x => x.Currency).HasMaxLength(10);
            e.Property(x => x.MinimumOrder).HasPrecision(10, 2);
            e.Property(x => x.StaffWhatsAppNumber).HasMaxLength(StaffNumbers.MaxStoredLength);
        });

        b.Entity<DeliveryZone>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Charge).HasPrecision(10, 2);
            e.HasOne<Restaurant>().WithMany(r => r.DeliveryZones).HasForeignKey(x => x.RestaurantId);
        });

        b.Entity<MenuItem>(e =>
        {
            e.HasIndex(x => new { x.RestaurantId, x.Category });
            e.Property(x => x.Price).HasPrecision(10, 2);
            e.HasOne<Restaurant>().WithMany(r => r.MenuItems).HasForeignKey(x => x.RestaurantId);
        });

        b.Entity<Deal>(e =>
        {
            e.Property(x => x.Price).HasPrecision(10, 2);
            e.HasOne<Restaurant>().WithMany(r => r.Deals).HasForeignKey(x => x.RestaurantId);
        });

        b.Entity<Faq>(e => e.HasOne<Restaurant>().WithMany(r => r.Faqs).HasForeignKey(x => x.RestaurantId));

        b.Entity<Customer>(e =>
        {
            e.HasIndex(x => new { x.RestaurantId, x.Phone }).IsUnique();
            e.Property(x => x.Phone).HasMaxLength(20);
            e.HasOne<Restaurant>().WithMany().HasForeignKey(x => x.RestaurantId);
        });

        b.Entity<ChatMessage>(e =>
        {
            e.HasIndex(x => new { x.CustomerId, x.CreatedAt });
            e.HasIndex(x => new { x.RestaurantId, x.CreatedAt }); // usage report
            e.HasIndex(x => x.WhatsAppMessageId);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(10);
            e.HasOne<Restaurant>().WithMany().HasForeignKey(x => x.RestaurantId);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId);
        });

        b.Entity<Order>(e =>
        {
            e.HasIndex(x => new { x.RestaurantId, x.Status, x.CreatedAt });
            e.HasIndex(x => new { x.CustomerId, x.Status });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(25);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(15);
            e.Property(x => x.CustomerName).HasMaxLength(100);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.DeliveryArea).HasMaxLength(100);
            e.Property(x => x.PaymentMethod).HasMaxLength(50);
            e.Property(x => x.Subtotal).HasPrecision(10, 2);
            e.Property(x => x.DeliveryCharge).HasPrecision(10, 2);
            e.Property(x => x.Total).HasPrecision(10, 2);
            e.HasOne<Restaurant>().WithMany().HasForeignKey(x => x.RestaurantId);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<OrderItem>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.UnitPrice).HasPrecision(10, 2);
            e.Property(x => x.LineTotal).HasPrecision(10, 2);
        });

        b.Entity<AppSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(200);
        });

        b.Entity<DashboardUser>(e =>
        {
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.OrganizationId);
            e.Property(x => x.Username).HasMaxLength(50);
            e.Property(x => x.PasswordHash).HasMaxLength(200);
            e.Property(x => x.SecurityStamp).HasMaxLength(40);
            e.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TableBooking>(e =>
        {
            e.HasIndex(x => new { x.RestaurantId, x.BookingTime });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(15);
            e.Property(x => x.CustomerName).HasMaxLength(100);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.HasOne<Restaurant>().WithMany().HasForeignKey(x => x.RestaurantId);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId);
        });
    }
}
