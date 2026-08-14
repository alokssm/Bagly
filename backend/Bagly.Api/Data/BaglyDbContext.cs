using Bagly.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Data;

public class BaglyDbContext(DbContextOptions<BaglyDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderShiprocketShipment> OrderShiprocketShipments => Set<OrderShiprocketShipment>();
    public DbSet<OrderShipmentTracking> OrderShipmentTrackings => Set<OrderShipmentTracking>();
    public DbSet<ShipmentStatusLog> ShipmentStatusLogs => Set<ShipmentStatusLog>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<CustomerUser> CustomerUsers => Set<CustomerUser>();
    public DbSet<SellerUser> SellerUsers => Set<SellerUser>();
    public DbSet<SellerPickupLocation> SellerPickupLocations => Set<SellerPickupLocation>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemLog> SystemLogs => Set<SystemLog>();
    public DbSet<PaymentLog> PaymentLogs => Set<PaymentLog>();
    public DbSet<ShiprocketApiLog> ShiprocketApiLogs => Set<ShiprocketApiLog>();
    public DbSet<StockAlert> StockAlerts => Set<StockAlert>();
    public DbSet<CustomerShippingAddress> ShippingAddresses => Set<CustomerShippingAddress>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<SiteHit> SiteHits => Set<SiteHit>();
    public DbSet<ProductReview> ProductReviews => Set<ProductReview>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(100);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(50).IsRequired();
            entity.Property(x => x.SubCategoryId).HasMaxLength(50);
            entity.Property(x => x.Price).HasColumnType("decimal(18,2)");
            entity.Property(x => x.CompareAt).HasColumnType("decimal(18,2)");
            entity.Property(x => x.Material).HasMaxLength(100);
            entity.Property(x => x.Badge).HasMaxLength(50);
            entity.Property(x => x.ShortDescription).HasMaxLength(500);
            entity.Property(x => x.Description).HasMaxLength(4000);
            entity.Property(x => x.Image).HasMaxLength(1000);
            entity.Property(x => x.ColorsJson).HasColumnType("text");
            entity.Property(x => x.FeaturesJson).HasColumnType("text");
            entity.Property(x => x.GalleryJson).HasColumnType("text");
            entity.Property(x => x.StockQuantity).HasDefaultValue(999);
            entity.Property(x => x.Slug).HasMaxLength(160);
            entity.Property(x => x.SeoTitle).HasMaxLength(160);
            entity.Property(x => x.SeoDescription).HasMaxLength(300);
            entity.Property(x => x.SeoKeywords).HasMaxLength(300);
            entity.Property(x => x.ShiprocketPickupLocation).HasMaxLength(100);
            entity.Property(x => x.UseDefaultPackageSize).HasDefaultValue(true);
            entity.Property(x => x.WeightKg).HasColumnType("decimal(18,3)");
            entity.Property(x => x.LengthCm).HasColumnType("decimal(18,2)");
            entity.Property(x => x.BreadthCm).HasColumnType("decimal(18,2)");
            entity.Property(x => x.HeightCm).HasColumnType("decimal(18,2)");
            entity.HasOne(x => x.Seller)
                .WithMany()
                .HasForeignKey(x => x.SellerId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(x => x.Category);
            entity.HasIndex(x => x.SubCategoryId);
            entity.HasIndex(x => x.IsActive);
            entity.HasIndex(x => x.SellerId);
            entity.HasIndex(x => x.Slug).IsUnique().HasFilter("\"Slug\" IS NOT NULL");
            entity.HasIndex(x => x.ShiprocketPickupLocation);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(50);
            entity.Property(x => x.Label).HasMaxLength(100).IsRequired();
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Property(x => x.ParentId).HasMaxLength(50);
            entity.HasIndex(x => x.ParentId);
        });

        modelBuilder.Entity<Cart>(entity =>
        {
            entity.ToTable("Carts");
            entity.HasKey(x => x.Id);
            entity.HasMany(x => x.Items)
                .WithOne(x => x.Cart!)
                .HasForeignKey(x => x.CartId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.ToTable("CartItems");
            entity.Property(x => x.ProductId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Image).HasMaxLength(1000);
            entity.Property(x => x.Color).HasMaxLength(50).IsRequired();
            entity.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
            entity.HasIndex(x => new { x.CartId, x.ProductId, x.Color }).IsUnique();
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.OrderNumber).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Address).HasMaxLength(300).IsRequired();
            entity.Property(x => x.City).HasMaxLength(100).IsRequired();
            entity.Property(x => x.State).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Zip).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Country).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.PaymentStatus).HasMaxLength(50).IsRequired();
            entity.Property(x => x.PaymentProvider).HasMaxLength(50);
            entity.Property(x => x.Currency).HasMaxLength(10);
            entity.Property(x => x.AmountInr).HasColumnType("decimal(18,2)");
            entity.Property(x => x.RazorpayOrderId).HasMaxLength(100);
            entity.Property(x => x.RazorpayPaymentId).HasMaxLength(100);
            entity.Property(x => x.ShiprocketOrderId).HasMaxLength(50);
            entity.Property(x => x.ShiprocketShipmentId).HasMaxLength(50);
            entity.Property(x => x.ShiprocketStatus).HasMaxLength(50);
            entity.Property(x => x.ShiprocketLastError).HasMaxLength(500);
            entity.Property(x => x.Subtotal).HasColumnType("decimal(18,2)");
            entity.Property(x => x.Shipping).HasColumnType("decimal(18,2)");
            entity.Property(x => x.Total).HasColumnType("decimal(18,2)");
            entity.HasIndex(x => x.OrderNumber).IsUnique();
            entity.HasIndex(x => x.Email);
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => x.RazorpayOrderId);
            entity.HasIndex(x => x.ShiprocketOrderId);
            entity.HasIndex(x => x.PaymentStatus);
            entity.HasIndex(x => x.CustomerUserId);
            entity.HasMany(x => x.Items)
                .WithOne(x => x.Order!)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.ShiprocketShipments)
                .WithOne(x => x.Order!)
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("OrderItems");
            entity.Property(x => x.ProductId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Color).HasMaxLength(50).IsRequired();
            entity.Property(x => x.UnitPrice).HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<OrderShiprocketShipment>(entity =>
        {
            entity.ToTable("OrderShiprocketShipments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PickupLocation).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ShiprocketOrderId).HasMaxLength(50);
            entity.Property(x => x.ShiprocketShipmentId).HasMaxLength(50);
            entity.Property(x => x.Status).HasMaxLength(50);
            entity.Property(x => x.ShippingStatus).HasMaxLength(50);
            entity.Property(x => x.LastError).HasMaxLength(500);
            entity.Property(x => x.AwbCode).HasMaxLength(50);
            entity.Property(x => x.CourierName).HasMaxLength(100);
            entity.Property(x => x.ActualShippingCharge).HasColumnType("decimal(18,2)");
            entity.Property(x => x.LabelUrl).HasMaxLength(1000);
            entity.Property(x => x.PickupTokenNumber).HasMaxLength(100);
            entity.Property(x => x.ManifestUrl).HasMaxLength(1000);
            entity.Property(x => x.TrackingStatus).HasMaxLength(50);
            entity.HasIndex(x => x.OrderId);
            entity.HasIndex(x => new { x.OrderId, x.PickupLocation }).IsUnique();
            entity.HasIndex(x => x.ShiprocketOrderId);
            entity.HasIndex(x => x.AwbCode);
            entity.HasIndex(x => x.ShippingStatus);
            entity.HasIndex(x => x.TrackingStatus);
            entity.HasIndex(x => x.PickupRequestedAt);
        });

        modelBuilder.Entity<OrderShipmentTracking>(entity =>
        {
            entity.ToTable("OrderShipmentTrackings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ShiprocketShipmentId).HasMaxLength(50);
            entity.Property(x => x.AwbCode).HasMaxLength(50);
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Source).HasMaxLength(50).IsRequired();
            entity.Property(x => x.RawJson).HasMaxLength(4000);
            entity.HasIndex(x => x.OrderId);
            entity.HasIndex(x => x.OrderShiprocketShipmentId);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.ChangedAtUtc);
            entity.HasOne(x => x.Order)
                .WithMany()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.OrderShiprocketShipment)
                .WithMany()
                .HasForeignKey(x => x.OrderShiprocketShipmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ShipmentStatusLog>(entity =>
        {
            entity.ToTable("ShipmentStatusLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.AwbCode).HasMaxLength(50);
            entity.Property(x => x.ShiprocketShipmentId).HasMaxLength(50);
            entity.Property(x => x.FromStatus).HasMaxLength(50);
            entity.Property(x => x.ToStatus).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Source).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(500);
            entity.Property(x => x.RawJson).HasMaxLength(4000);
            entity.HasIndex(x => x.OrderId);
            entity.HasIndex(x => x.OrderShiprocketShipmentId);
            entity.HasIndex(x => x.ToStatus);
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasOne(x => x.Order)
                .WithMany()
                .HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.OrderShiprocketShipment)
                .WithMany()
                .HasForeignKey(x => x.OrderShiprocketShipmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.ToTable("AdminUsers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Role).HasMaxLength(50).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
            entity.HasIndex(x => x.IsActive);
        });

        modelBuilder.Entity<CustomerUser>(entity =>
        {
            entity.ToTable("CustomerUsers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(500);
            entity.Property(x => x.GoogleSubject).HasMaxLength(100);
            entity.HasIndex(x => x.Email).IsUnique();
            entity.HasIndex(x => x.GoogleSubject).IsUnique().HasFilter("\"GoogleSubject\" IS NOT NULL");
            entity.HasIndex(x => x.IsActive);
        });

        modelBuilder.Entity<SellerUser>(entity =>
        {
            entity.ToTable("SellerUsers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.BusinessName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(x => x.AddressLine1).HasMaxLength(200);
            entity.Property(x => x.AddressLine2).HasMaxLength(200);
            entity.Property(x => x.City).HasMaxLength(100);
            entity.Property(x => x.State).HasMaxLength(100);
            entity.Property(x => x.Pincode).HasMaxLength(12);
            entity.Property(x => x.Gstin).HasMaxLength(20);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.UpiId).HasMaxLength(100);
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.RejectionReason).HasMaxLength(500);
            entity.HasIndex(x => x.Email).IsUnique();
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.IsActive);
        });

        modelBuilder.Entity<SellerPickupLocation>(entity =>
        {
            entity.ToTable("SellerPickupLocations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PickupLocation).HasMaxLength(36).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(15).IsRequired();
            entity.Property(x => x.Address).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Address2).HasMaxLength(80);
            entity.Property(x => x.City).HasMaxLength(50).IsRequired();
            entity.Property(x => x.State).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Country).HasMaxLength(50).IsRequired();
            entity.Property(x => x.PinCode).HasMaxLength(12).IsRequired();
            entity.Property(x => x.Lat).HasMaxLength(30);
            entity.Property(x => x.Long).HasMaxLength(30);
            entity.Property(x => x.Gstin).HasMaxLength(20);
            entity.Property(x => x.ShiprocketPickupId).HasMaxLength(50);
            entity.HasOne(x => x.SellerUser)
                .WithMany()
                .HasForeignKey(x => x.SellerUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.SellerUserId);
            entity.HasIndex(x => new { x.SellerUserId, x.PickupLocation }).IsUnique();
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.Level).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ActorEmail).HasMaxLength(256);
            entity.Property(x => x.EntityType).HasMaxLength(100);
            entity.Property(x => x.EntityId).HasMaxLength(100);
            entity.Property(x => x.Message).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.DetailsJson).HasColumnType("text");
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.Property(x => x.RequestPath).HasMaxLength(500);
            entity.HasIndex(x => x.TimestampUtc);
            entity.HasIndex(x => x.Category);
            entity.HasIndex(x => x.Action);
            entity.HasIndex(x => x.ActorEmail);
        });

        modelBuilder.Entity<SystemLog>(entity =>
        {
            // Logging is console-only (Serilog) since the Neon/Postgres migration, so this table
            // is no longer populated by a DB sink. Kept (EF-managed) so AdminReportsController's
            // system-logs endpoint keeps working — it just always returns an empty page for now.
            entity.ToTable("Logs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.Message).HasColumnType("text");
            entity.Property(x => x.MessageTemplate).HasColumnType("text");
            entity.Property(x => x.Level).HasColumnType("text");
            entity.Property(x => x.TimeStamp).HasColumnType("timestamp with time zone");
            entity.Property(x => x.Exception).HasColumnType("text");
            entity.Property(x => x.LogEvent).HasColumnType("text");
            entity.Property(x => x.RequestPath).HasMaxLength(500);
            entity.Property(x => x.ActorEmail).HasMaxLength(256);
            entity.Property(x => x.AuditCategory).HasMaxLength(50);
            entity.Property(x => x.AuditAction).HasMaxLength(100);
        });

        modelBuilder.Entity<PaymentLog>(entity =>
        {
            entity.ToTable("PaymentLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.OrderNumber).HasMaxLength(50);
            entity.Property(x => x.Provider).HasMaxLength(50).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(50).IsRequired();
            entity.Property(x => x.RazorpayOrderId).HasMaxLength(100);
            entity.Property(x => x.RazorpayPaymentId).HasMaxLength(100);
            entity.Property(x => x.RazorpaySignature).HasMaxLength(256);
            entity.Property(x => x.Amount).HasColumnType("decimal(18,2)");
            entity.Property(x => x.Currency).HasMaxLength(10);
            entity.Property(x => x.CustomerEmail).HasMaxLength(256);
            entity.Property(x => x.Message).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.RequestJson).HasColumnType("text");
            entity.Property(x => x.ResponseJson).HasColumnType("text");
            entity.Property(x => x.ErrorCode).HasMaxLength(100);
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.HasIndex(x => x.TimestampUtc);
            entity.HasIndex(x => x.OrderId);
            entity.HasIndex(x => x.RazorpayOrderId);
            entity.HasIndex(x => x.EventType);
            entity.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<ShiprocketApiLog>(entity =>
        {
            entity.ToTable("ShiprocketApiLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.Action).HasMaxLength(80).IsRequired();
            entity.Property(x => x.HttpMethod).HasMaxLength(10).IsRequired();
            entity.Property(x => x.Url).HasMaxLength(500).IsRequired();
            entity.Property(x => x.RequestJson).HasColumnType("text");
            entity.Property(x => x.ResponseJson).HasColumnType("text");
            entity.Property(x => x.AdminEmail).HasMaxLength(256);
            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasIndex(x => x.OrderId);
            entity.HasIndex(x => x.ShipmentId);
            entity.HasIndex(x => x.Action);
        });

        modelBuilder.Entity<StockAlert>(entity =>
        {
            entity.ToTable("StockAlerts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.ProductId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.HasIndex(x => new { x.Email, x.ProductId }).IsUnique();
            entity.HasIndex(x => x.ProductId);
            entity.HasIndex(x => x.Notified);
        });

        modelBuilder.Entity<CustomerShippingAddress>(entity =>
        {
            entity.ToTable("ShippingAddresses");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Label).HasMaxLength(50);
            entity.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.Address).HasMaxLength(300).IsRequired();
            entity.Property(x => x.City).HasMaxLength(100).IsRequired();
            entity.Property(x => x.State).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Zip).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Country).HasMaxLength(100).IsRequired();
            entity.HasOne(x => x.CustomerUser)
                .WithMany()
                .HasForeignKey(x => x.CustomerUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.CustomerUserId);
            entity.HasIndex(x => new { x.CustomerUserId, x.IsDefault });
        });

        modelBuilder.Entity<ContactMessage>(entity =>
        {
            entity.ToTable("ContactMessages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
            entity.Property(x => x.CompanyName).HasMaxLength(200);
            entity.Property(x => x.Message).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => x.Email);
        });

        modelBuilder.Entity<SiteHit>(entity =>
        {
            entity.ToTable("SiteHits");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.Path).HasMaxLength(500).IsRequired();
            entity.Property(x => x.IpAddress).HasMaxLength(64);
            entity.Property(x => x.Country).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Region).HasMaxLength(100);
            entity.Property(x => x.City).HasMaxLength(100);
            entity.Property(x => x.UserAgent).HasMaxLength(300);
            entity.Property(x => x.SessionId).HasMaxLength(100);
            entity.HasIndex(x => x.OccurredAtUtc);
            entity.HasIndex(x => x.Country);
            entity.HasIndex(x => x.SessionId);
        });

        modelBuilder.Entity<ProductReview>(entity =>
        {
            entity.ToTable("ProductReviews");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProductId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Rating).IsRequired();
            entity.Property(x => x.Comment).HasMaxLength(2000);
            entity.HasOne(x => x.Product)
                .WithMany()
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.CustomerUser)
                .WithMany()
                .HasForeignKey(x => x.CustomerUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ProductId, x.CustomerUserId }).IsUnique();
            entity.HasIndex(x => x.ProductId);
            entity.HasIndex(x => x.CustomerUserId);
            entity.HasIndex(x => x.CreatedAt);
        });
    }
}
