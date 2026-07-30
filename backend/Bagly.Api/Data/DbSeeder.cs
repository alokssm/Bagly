using System.Text.Json;
using Bagly.Api.Models;
using Bagly.Api.Options;
using Bagly.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Data;

public static class DbSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task SeedAsync(BaglyDbContext db, AdminOptions? adminOptions = null)
    {
        if (!await db.Categories.AnyAsync())
        {

            db.Categories.AddRange(
                new Category { Id = "all", Label = "All bags", SortOrder = 0 },
                new Category { Id = "tote", Label = "Totes", SortOrder = 1 },
                new Category { Id = "backpack", Label = "Backpacks", SortOrder = 2 },
                new Category { Id = "crossbody", Label = "Crossbody", SortOrder = 3 },
                new Category { Id = "travel", Label = "Travel", SortOrder = 4 },
                new Category { Id = "work", Label = "Work", SortOrder = 5 }
            );
        }

        if (!await db.Products.AnyAsync())
        {
            db.Products.AddRange(CreateProducts());
        }

        await db.SaveChangesAsync();

        await SeedAdminUserAsync(db, adminOptions);

        if (!await db.Carts.AnyAsync())
        {
            var cart1Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var cart2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

            db.Carts.AddRange(
                new Cart
                {
                    Id = cart1Id,
                    UpdatedAt = DateTime.UtcNow,
                    Items =
                    [
                        new CartItem
                        {
                            ProductId = "atelier-tote",
                            ProductName = "Atelier Leather Tote",
                            Image = "https://images.unsplash.com/photo-1591561954557-26941169b49e?auto=format&fit=crop&w=900&q=80",
                            Color = "Cognac",
                            UnitPrice = 248,
                            Quantity = 1,
                        },
                        new CartItem
                        {
                            ProductId = "city-sling",
                            ProductName = "City Sling",
                            Image = "https://images.unsplash.com/photo-1566150905458-1bf1fc113f0d?auto=format&fit=crop&w=900&q=80",
                            Color = "Black",
                            UnitPrice = 98,
                            Quantity = 2,
                        },
                    ],
                },
                new Cart
                {
                    Id = cart2Id,
                    UpdatedAt = DateTime.UtcNow,
                    Items =
                    [
                        new CartItem
                        {
                            ProductId = "nomad-backpack",
                            ProductName = "Nomad Commute Pack",
                            Image = "https://images.unsplash.com/photo-1581605405669-fcdf81165afa?auto=format&fit=crop&w=900&q=80",
                            Color = "Olive",
                            UnitPrice = 198,
                            Quantity = 1,
                        },
                        new CartItem
                        {
                            ProductId = "horizon-crossbody",
                            ProductName = "Horizon Crossbody",
                            Image = "https://images.unsplash.com/photo-1584917865442-de89df76afd3?auto=format&fit=crop&w=900&q=80",
                            Color = "Rust",
                            UnitPrice = 128,
                            Quantity = 1,
                        },
                    ],
                }
            );
        }

        if (!await db.Orders.AnyAsync())
        {
            db.Orders.AddRange(
                new Order
                {
                    Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    OrderNumber = "BG-DEMO-1001",
                    Email = "ada@example.com",
                    FirstName = "Ada",
                    LastName = "Lovelace",
                    Address = "12 Analytical Engine Ave",
                    City = "London",
                    State = "LDN",
                    Zip = "EC1A 1BB",
                    Country = "United Kingdom",
                    Subtotal = 346,
                    Shipping = 0,
                    Total = 346,
                    Status = "Confirmed",
                    CreatedAt = DateTime.UtcNow.AddDays(-5),
                    Items =
                    [
                        new OrderItem { ProductId = "atelier-tote", ProductName = "Atelier Leather Tote", Color = "Ink", UnitPrice = 248, Quantity = 1 },
                        new OrderItem { ProductId = "city-sling", ProductName = "City Sling", Color = "Camel", UnitPrice = 98, Quantity = 1 },
                    ],
                },
                new Order
                {
                    Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    OrderNumber = "BG-DEMO-1002",
                    Email = "grace@example.com",
                    FirstName = "Grace",
                    LastName = "Hopper",
                    Address = "88 Compiler Road",
                    City = "New York",
                    State = "NY",
                    Zip = "10001",
                    Country = "United States",
                    Subtotal = 168,
                    Shipping = 12,
                    Total = 180,
                    Status = "Shipped",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    Items =
                    [
                        new OrderItem { ProductId = "trail-pack", ProductName = "Trail Daypack", Color = "Forest", UnitPrice = 168, Quantity = 1 },
                    ],
                },
                new Order
                {
                    Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    OrderNumber = "BG-DEMO-1003",
                    Email = "alan@example.com",
                    FirstName = "Alan",
                    LastName = "Turing",
                    Address = "1 Bletchley Park",
                    City = "Milton Keynes",
                    State = "BKM",
                    Zip = "MK3 6EB",
                    Country = "United Kingdom",
                    Subtotal = 512,
                    Shipping = 0,
                    Total = 512,
                    Status = "Processing",
                    CreatedAt = DateTime.UtcNow.AddHours(-8),
                    Items =
                    [
                        new OrderItem { ProductId = "weekender-duffel", ProductName = "Weekender Duffel", Color = "Navy", UnitPrice = 312, Quantity = 1 },
                        new OrderItem { ProductId = "canvas-market", ProductName = "Canvas Market Bag", Color = "Sage", UnitPrice = 72, Quantity = 1 },
                        new OrderItem { ProductId = "horizon-crossbody", ProductName = "Horizon Crossbody", Color = "Stone", UnitPrice = 128, Quantity = 1 },
                    ],
                }
            );
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedAdminUserAsync(BaglyDbContext db, AdminOptions? adminOptions)
    {
        var email = string.IsNullOrWhiteSpace(adminOptions?.Email)
            ? "admin@bagly.store"
            : adminOptions.Email.Trim();
        var name = string.IsNullOrWhiteSpace(adminOptions?.Name)
            ? "Bagly Admin"
            : adminOptions.Name.Trim();
        var password = string.IsNullOrWhiteSpace(adminOptions?.Password)
            ? "Admin@123"
            : adminOptions.Password;

        var existing = await db.AdminUsers
            .FirstOrDefaultAsync(u => u.Email == email);

        if (existing is null)
        {
            db.AdminUsers.Add(new AdminUser
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Email = email,
                Name = name,
                PasswordHash = PasswordHasher.Hash(password),
                Role = "Admin",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            return;
        }

        // Always sync password/name from configured Admin options (Render env vars).
        existing.PasswordHash = PasswordHasher.Hash(password);
        existing.Name = name;
        existing.IsActive = true;
        existing.Role = "Admin";
        await db.SaveChangesAsync();
    }

    private static IEnumerable<Product> CreateProducts() =>
    [
        Create(
            "atelier-tote",
            "Atelier Leather Tote",
            "tote",
            248,
            295,
            ["Cognac", "Ink", "Olive"],
            "Full-grain leather",
            4.9,
            128,
            "Bestseller",
            "A structured everyday tote with quiet luxury proportions.",
            "Hand-finished full-grain leather with a roomy interior, laptop sleeve, and magnetic closure. Built for markets, meetings, and weekends away.",
            ["15\" laptop sleeve", "Interior zip pocket", "Detachable shoulder strap", "Brass hardware"],
            "https://images.unsplash.com/photo-1591561954557-26941169b49e?auto=format&fit=crop&w=900&q=80",
            [
                "https://images.unsplash.com/photo-1591561954557-26941169b49e?auto=format&fit=crop&w=900&q=80",
                "https://images.unsplash.com/photo-1548036328-c9fa89d128fa?auto=format&fit=crop&w=900&q=80",
            ]
        ),
        Create(
            "trail-pack",
            "Trail Daypack",
            "backpack",
            168,
            null,
            ["Forest", "Slate", "Sand"],
            "Recycled nylon",
            4.8,
            96,
            "New",
            "Lightweight daypack with weather-ready shell.",
            "A streamlined 22L pack with padded straps, hidden pocket, and water-resistant zippers. Ready for commute or trail.",
            ["22L capacity", "Padded laptop sleeve", "Water-resistant shell", "Sternum strap"],
            "https://images.unsplash.com/photo-1553062407-98eeb64c6a62?auto=format&fit=crop&w=900&q=80",
            [
                "https://images.unsplash.com/photo-1553062407-98eeb64c6a62?auto=format&fit=crop&w=900&q=80",
                "https://images.unsplash.com/photo-1622560480605-d83c853bc5c3?auto=format&fit=crop&w=900&q=80",
            ]
        ),
        Create(
            "city-sling",
            "City Sling",
            "crossbody",
            98,
            null,
            ["Black", "Camel"],
            "Saffiano leather",
            4.7,
            214,
            null,
            "Compact crossbody for phone, keys, and cards.",
            "Minimal silhouette with RFID card slots and an adjustable strap. Wear it crossbody or as a belt bag.",
            ["RFID card slots", "Adjustable strap", "Quick-access pocket", "Soft lining"],
            "https://images.unsplash.com/photo-1566150905458-1bf1fc113f0d?auto=format&fit=crop&w=900&q=80",
            ["https://images.unsplash.com/photo-1566150905458-1bf1fc113f0d?auto=format&fit=crop&w=900&q=80"]
        ),
        Create(
            "weekender-duffel",
            "Weekender Duffel",
            "travel",
            312,
            360,
            ["Espresso", "Navy"],
            "Waxed canvas + leather",
            4.9,
            67,
            "Limited",
            "Carry-on ready duffel with leather accents.",
            "A spacious weekender with shoe compartment, trolley sleeve, and reinforced handles. Fits overhead bins with ease.",
            ["Shoe compartment", "Trolley sleeve", "Lockable zippers", "Leather trim"],
            "https://images.unsplash.com/photo-1553413077-190dd305871c?auto=format&fit=crop&w=900&q=80",
            [
                "https://images.unsplash.com/photo-1553413077-190dd305871c?auto=format&fit=crop&w=900&q=80",
                "https://images.unsplash.com/photo-1581605405669-fcdf81165afa?auto=format&fit=crop&w=900&q=80",
            ]
        ),
        Create(
            "brief-folio",
            "Brief Folio",
            "work",
            275,
            null,
            ["Charcoal", "Tan"],
            "Vegetable-tanned leather",
            4.8,
            54,
            null,
            "Slim work bag that holds a laptop and documents.",
            "Clean lines for the office. Includes an organizer panel, magnetic flap, and optional shoulder strap.",
            ["14\" laptop fit", "Document pocket", "Organizer panel", "Optional strap"],
            "https://images.unsplash.com/photo-1598532163257-ae3c6b2524b6?auto=format&fit=crop&w=900&q=80",
            [
                "https://images.unsplash.com/photo-1598532163257-ae3c6b2524b6?auto=format&fit=crop&w=900&q=80",
                "https://images.unsplash.com/photo-1548036328-c9fa89d128fa?auto=format&fit=crop&w=900&q=80",
            ]
        ),
        Create(
            "canvas-market",
            "Canvas Market Bag",
            "tote",
            72,
            null,
            ["Natural", "Sage", "Ink"],
            "Organic canvas",
            4.6,
            301,
            null,
            "Soft structured tote for errands and weekends.",
            "Heavyweight organic canvas with reinforced base and interior pocket. Folds flat when not in use.",
            ["Reinforced base", "Interior pocket", "Long handles", "Machine washable"],
            "https://images.unsplash.com/photo-1622560480654-d96214fdc887?auto=format&fit=crop&w=900&q=80",
            [
                "https://images.unsplash.com/photo-1622560480654-d96214fdc887?auto=format&fit=crop&w=900&q=80",
                "https://images.unsplash.com/photo-1547949003-9792a18a2601?auto=format&fit=crop&w=900&q=80",
            ]
        ),
        Create(
            "nomad-backpack",
            "Nomad Commute Pack",
            "backpack",
            198,
            null,
            ["Olive", "Black"],
            "Ballistic nylon",
            4.9,
            142,
            "Bestseller",
            "Clamshell backpack built for modern desks.",
            "Opens flat like a suitcase. Dedicated tech compartment, hidden passport pocket, and luggage pass-through.",
            ["Clamshell opening", "Tech organizer", "Passport pocket", "Luggage pass-through"],
            "https://images.unsplash.com/photo-1581605405669-fcdf81165afa?auto=format&fit=crop&w=900&q=80",
            [
                "https://images.unsplash.com/photo-1581605405669-fcdf81165afa?auto=format&fit=crop&w=900&q=80",
                "https://images.unsplash.com/photo-1553062407-98eeb64c6a62?auto=format&fit=crop&w=900&q=80",
            ]
        ),
        Create(
            "horizon-crossbody",
            "Horizon Crossbody",
            "crossbody",
            128,
            null,
            ["Rust", "Stone", "Black"],
            "Soft calf leather",
            4.7,
            88,
            null,
            "Soft leather bag with room for a compact camera.",
            "Supple calf leather with a zip-top closure and adjustable strap. Light enough for all-day wear.",
            ["Zip-top closure", "Interior divider", "Adjustable strap", "Dust bag included"],
            "https://images.unsplash.com/photo-1584917865442-de89df76afd3?auto=format&fit=crop&w=900&q=80",
            ["https://images.unsplash.com/photo-1584917865442-de89df76afd3?auto=format&fit=crop&w=900&q=80"]
        ),
    ];

    private static Product Create(
        string id,
        string name,
        string category,
        decimal price,
        decimal? compareAt,
        string[] colors,
        string material,
        double rating,
        int reviews,
        string? badge,
        string shortDescription,
        string description,
        string[] features,
        string image,
        string[] gallery
    ) =>
        new()
        {
            Id = id,
            Name = name,
            Category = category,
            Price = price,
            CompareAt = compareAt,
            Material = material,
            Rating = rating,
            Reviews = reviews,
            Badge = badge,
            ShortDescription = shortDescription,
            Description = description,
            Image = image,
            ColorsJson = JsonSerializer.Serialize(colors, JsonOptions),
            FeaturesJson = JsonSerializer.Serialize(features, JsonOptions),
            GalleryJson = JsonSerializer.Serialize(gallery, JsonOptions),
        };
}
