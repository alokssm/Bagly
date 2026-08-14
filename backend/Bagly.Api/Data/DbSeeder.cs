using System.Text.Json;
using Bagly.Api.Mapping;
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

    private const string SchoolBagsCategoryId = "school-bags";
    private const string StationeryCategoryId = "stationery";
    private const string SeedHomeProductId = "seed-home-1";
    private const string SeedWorkProductId = "seed-work-1";
    private const string SeedWarehouse1ProductId = "seed-warehouse-1";
    private const string SeedWarehouse2ProductId = "seed-warehouse-2";
    private const string Warehouse1Pickup = "wareHouse1";
    private const string Warehouse2Pickup = "wareHouse2";
    private static readonly string[] LegacyCategoryIds = ["tote", "backpack", "crossbody", "travel", "work"];

    public static async Task SeedAsync(
        BaglyDbContext db,
        AdminOptions? adminOptions = null,
        ShiprocketOptions? shiprocketOptions = null)
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

        // Always runs (even when categories/products already exist from a prior seed) so School
        // Bags + its Boys/Girls/Kids subcategories stay active/populated on every deploy, and so
        // calling POST /api/setup/seed again on a non-empty Azure/Render database still syncs it.
        await EnsureSchoolBagsCatalogAsync(db);
        await EnsureStationeryCatalogAsync(db);
        await EnsurePickupDemoProductsAsync(db, shiprocketOptions);
        await EnsureWarehousePickupSampleProductsAsync(db);

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
                            UnitPrice = 14999,
                            Quantity = 1,
                        },
                        new CartItem
                        {
                            ProductId = "city-sling",
                            ProductName = "City Sling",
                            Image = "https://images.unsplash.com/photo-1566150905458-1bf1fc113f0d?auto=format&fit=crop&w=900&q=80",
                            Color = "Black",
                            UnitPrice = 5999,
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
                            UnitPrice = 11999,
                            Quantity = 1,
                        },
                        new CartItem
                        {
                            ProductId = "horizon-crossbody",
                            ProductName = "Horizon Crossbody",
                            Image = "https://images.unsplash.com/photo-1584917865442-de89df76afd3?auto=format&fit=crop&w=900&q=80",
                            Color = "Rust",
                            UnitPrice = 7499,
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
                    Subtotal = 20998,
                    Shipping = 0,
                    Total = 20998,
                    Status = "Confirmed",
                    CreatedAt = DateTime.UtcNow.AddDays(-5),
                    Items =
                    [
                        new OrderItem { ProductId = "atelier-tote", ProductName = "Atelier Leather Tote", Color = "Ink", UnitPrice = 14999, Quantity = 1 },
                        new OrderItem { ProductId = "city-sling", ProductName = "City Sling", Color = "Camel", UnitPrice = 5999, Quantity = 1 },
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
                    Subtotal = 9999,
                    Shipping = 199,
                    Total = 10198,
                    Status = "Shipped",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    Items =
                    [
                        new OrderItem { ProductId = "trail-pack", ProductName = "Trail Daypack", Color = "Forest", UnitPrice = 9999, Quantity = 1 },
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
                    Subtotal = 30497,
                    Shipping = 0,
                    Total = 30497,
                    Status = "Processing",
                    CreatedAt = DateTime.UtcNow.AddHours(-8),
                    Items =
                    [
                        new OrderItem { ProductId = "weekender-duffel", ProductName = "Weekender Duffel", Color = "Navy", UnitPrice = 18999, Quantity = 1 },
                        new OrderItem { ProductId = "canvas-market", ProductName = "Canvas Market Bag", Color = "Sage", UnitPrice = 3999, Quantity = 1 },
                        new OrderItem { ProductId = "horizon-crossbody", ProductName = "Horizon Crossbody", Color = "Stone", UnitPrice = 7499, Quantity = 1 },
                    ],
                }
            );
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedAdminUserAsync(BaglyDbContext db, AdminOptions? adminOptions)
    {
        var options = adminOptions ?? new AdminOptions();
        var email = options.ResolveEmail();
        var name = options.ResolveName();
        var password = options.ResolvePassword();

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

    /// <summary>
    /// Ensures the School Bags catalog (category + Boys/Girls/Kids subcategories + 360 products)
    /// exists and is active, deactivating legacy categories so the storefront's category filter
    /// focuses on School Bags. Safe to call repeatedly (e.g. via POST /api/setup/seed) even when
    /// the database already has products — categories are upserted and products are added only
    /// if their id doesn't already exist.
    /// </summary>
    private static async Task EnsureSchoolBagsCatalogAsync(BaglyDbContext db)
    {
        var categories = await db.Categories.ToListAsync();
        var byId = categories.ToDictionary(c => c.Id, c => c, StringComparer.OrdinalIgnoreCase);

        UpsertCategory(db, byId, SchoolBagsCategoryId, "School Bags", sortOrder: 1, parentId: null);
        UpsertCategory(db, byId, "boys", "Boys", sortOrder: 2, parentId: SchoolBagsCategoryId);
        UpsertCategory(db, byId, "girls", "Girls", sortOrder: 3, parentId: SchoolBagsCategoryId);
        UpsertCategory(db, byId, "kids", "Kids", sortOrder: 4, parentId: SchoolBagsCategoryId);

        // Legacy demo categories are hidden (not deleted) so the storefront's category filter
        // focuses on School Bags, while existing legacy products remain purchasable via direct link.
        foreach (var legacyId in LegacyCategoryIds)
        {
            if (byId.TryGetValue(legacyId, out var legacy))
            {
                legacy.IsActive = false;
            }
        }

        if (byId.TryGetValue("all", out var all))
        {
            all.IsActive = true;
        }

        await db.SaveChangesAsync();

        var existingIds = (await db.Products.Select(p => p.Id).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newSchoolBagProducts = CreateSchoolBagProducts()
            .Where(p => !existingIds.Contains(p.Id))
            .ToList();

        if (newSchoolBagProducts.Count > 0)
        {
            db.Products.AddRange(newSchoolBagProducts);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Ensures two platform demo products exist with distinct Shiprocket pickup nicknames
    /// (typically <c>home</c> and <c>work</c> from <c>Shiprocket:PickupLocations</c>).
    /// Idempotent by id (<c>seed-home-1</c>, <c>seed-work-1</c>): inserts when missing,
    /// otherwise syncs pickup nickname / active / package defaults.
    /// </summary>
    private static async Task EnsurePickupDemoProductsAsync(
        BaglyDbContext db,
        ShiprocketOptions? shiprocketOptions)
    {
        var (homePickup, workPickup) = ResolveDemoPickupNicknames(shiprocketOptions);

        var categoryId = await ResolvePickupDemoCategoryIdAsync(db);
        var specs = new[]
        {
            (
                Id: SeedHomeProductId,
                Name: "Sample Bag — Home pickup",
                Pickup: homePickup,
                Image: SchoolBagImage(0, 0),
                GalleryOffset: 1
            ),
            (
                Id: SeedWorkProductId,
                Name: "Sample Bag — Work pickup",
                Pickup: workPickup,
                Image: SchoolBagImage(3, 2),
                GalleryOffset: 4
            ),
        };

        var ids = specs.Select(s => s.Id).ToArray();
        var existing = await db.Products
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var spec in specs)
        {
            var gallery = new[] { spec.Image, SchoolBagImage(spec.GalleryOffset, 0) };
            var colors = new[] { "Navy", "Charcoal" };
            var features = new[]
            {
                "Shiprocket multi-pickup demo",
                "Platform catalog product",
                "Uses default package size",
                "Assigned to School Bags when available",
            };
            var shortDescription =
                $"Demo school bag fulfilled from Shiprocket pickup \"{spec.Pickup}\".";
            var description =
                $"{spec.Name} is a seeded platform product for admin shipping demos. " +
                $"It ships from the \"{spec.Pickup}\" pickup nickname configured in Shiprocket.";

            if (existing.TryGetValue(spec.Id, out var product))
            {
                product.Name = spec.Name;
                product.Category = categoryId;
                product.SubCategoryId = null;
                product.ShiprocketPickupLocation = spec.Pickup;
                product.IsActive = true;
                product.UseDefaultPackageSize = true;
                product.WeightKg = null;
                product.LengthCm = null;
                product.BreadthCm = null;
                product.HeightCm = null;
                product.SellerId = null;
                product.Price = product.Price > 0 ? product.Price : 1499m;
                product.StockQuantity = product.StockQuantity > 0 ? product.StockQuantity : 50;
                if (string.IsNullOrWhiteSpace(product.Image))
                {
                    product.Image = spec.Image;
                }

                continue;
            }

            db.Products.Add(new Product
            {
                Id = spec.Id,
                Name = spec.Name,
                Category = categoryId,
                SubCategoryId = null,
                Price = 1499m,
                CompareAt = 1999m,
                Material = "Durable Polyester",
                Rating = 4.8,
                Reviews = 12,
                Badge = "New",
                ShortDescription = shortDescription,
                Description = description,
                Image = spec.Image,
                ColorsJson = JsonSerializer.Serialize(colors, JsonOptions),
                FeaturesJson = JsonSerializer.Serialize(features, JsonOptions),
                GalleryJson = JsonSerializer.Serialize(gallery, JsonOptions),
                IsActive = true,
                StockQuantity = 50,
                CreatedAt = DateTime.UtcNow,
                Slug = spec.Id,
                SeoTitle = $"{spec.Name} | Bagly",
                SeoDescription = shortDescription,
                SeoKeywords = "sample bag, shiprocket, pickup demo, bagly",
                SellerId = null,
                ShiprocketPickupLocation = spec.Pickup,
                UseDefaultPackageSize = true,
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Ensures two platform sample products exist with fixed Shiprocket pickup nicknames
    /// <c>wareHouse1</c> and <c>wareHouse2</c>. Idempotent by id (<c>seed-warehouse-1</c>,
    /// <c>seed-warehouse-2</c>): inserts when missing, otherwise syncs pickup / name / active / package defaults.
    /// </summary>
    private static async Task EnsureWarehousePickupSampleProductsAsync(BaglyDbContext db)
    {
        var categoryId = await ResolvePickupDemoCategoryIdAsync(db);
        var specs = new[]
        {
            (
                Id: SeedWarehouse1ProductId,
                Name: "Sample Bag — wareHouse1",
                Pickup: Warehouse1Pickup,
                Image: SchoolBagImage(1, 0),
                GalleryOffset: 2
            ),
            (
                Id: SeedWarehouse2ProductId,
                Name: "Sample Bag — wareHouse2",
                Pickup: Warehouse2Pickup,
                Image: SchoolBagImage(4, 1),
                GalleryOffset: 5
            ),
        };

        var ids = specs.Select(s => s.Id).ToArray();
        var existing = await db.Products
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var spec in specs)
        {
            var gallery = new[] { spec.Image, SchoolBagImage(spec.GalleryOffset, 0) };
            var colors = new[] { "Navy", "Charcoal" };
            var features = new[]
            {
                "Shiprocket warehouse pickup sample",
                "Platform catalog product",
                "Uses default package size",
                "Assigned to School Bags when available",
            };
            var shortDescription =
                $"Sample school bag fulfilled from Shiprocket pickup \"{spec.Pickup}\".";
            var description =
                $"{spec.Name} is a seeded platform product for warehouse pickup demos. " +
                $"It ships from the \"{spec.Pickup}\" pickup nickname configured in Shiprocket.";

            if (existing.TryGetValue(spec.Id, out var product))
            {
                product.Name = spec.Name;
                product.Category = categoryId;
                product.SubCategoryId = null;
                product.ShiprocketPickupLocation = spec.Pickup;
                product.IsActive = true;
                product.UseDefaultPackageSize = true;
                product.WeightKg = null;
                product.LengthCm = null;
                product.BreadthCm = null;
                product.HeightCm = null;
                product.SellerId = null;
                product.Price = product.Price > 0 ? product.Price : 1499m;
                product.StockQuantity = product.StockQuantity > 0 ? product.StockQuantity : 50;
                if (string.IsNullOrWhiteSpace(product.Image))
                {
                    product.Image = spec.Image;
                }

                continue;
            }

            db.Products.Add(new Product
            {
                Id = spec.Id,
                Name = spec.Name,
                Category = categoryId,
                SubCategoryId = null,
                Price = 1499m,
                CompareAt = 1999m,
                Material = "Durable Polyester",
                Rating = 4.8,
                Reviews = 12,
                Badge = "New",
                ShortDescription = shortDescription,
                Description = description,
                Image = spec.Image,
                ColorsJson = JsonSerializer.Serialize(colors, JsonOptions),
                FeaturesJson = JsonSerializer.Serialize(features, JsonOptions),
                GalleryJson = JsonSerializer.Serialize(gallery, JsonOptions),
                IsActive = true,
                StockQuantity = 50,
                CreatedAt = DateTime.UtcNow,
                Slug = spec.Id,
                SeoTitle = $"{spec.Name} | Bagly",
                SeoDescription = shortDescription,
                SeoKeywords = "sample bag, shiprocket, warehouse pickup, bagly",
                SellerId = null,
                ShiprocketPickupLocation = spec.Pickup,
                UseDefaultPackageSize = true,
            });
        }

        await db.SaveChangesAsync();
    }

    private static (string Home, string Work) ResolveDemoPickupNicknames(ShiprocketOptions? shiprocketOptions)
    {
        var preferred = (shiprocketOptions?.GetPickupLocationChoices() ?? Array.Empty<string>())
            .Where(c => !ShiprocketOptions.IsPlaceholderPickup(c))
            .ToList();

        string? Find(string name) =>
            preferred.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));

        var home = Find("home");
        var work = Find("work");

        if (home is not null && work is not null)
        {
            return (home, work);
        }

        // Config may use custom nicknames — take the first two distinct choices when available.
        if (preferred.Count >= 2)
        {
            home ??= preferred[0];
            work ??= preferred.First(c => !string.Equals(c, home, StringComparison.OrdinalIgnoreCase));
            if (!string.Equals(home, work, StringComparison.OrdinalIgnoreCase))
            {
                return (home!, work!);
            }
        }

        return (home ?? "home", work ?? "work");
    }

    private static async Task<string> ResolvePickupDemoCategoryIdAsync(BaglyDbContext db)
    {
        var schoolBags = await db.Categories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == SchoolBagsCategoryId && c.IsActive);
        if (schoolBags is not null)
        {
            return SchoolBagsCategoryId;
        }

        var firstActive = await db.Categories.AsNoTracking()
            .Where(c => c.IsActive && c.Id != "all")
            .OrderBy(c => c.SortOrder)
            .Select(c => c.Id)
            .FirstOrDefaultAsync();

        return string.IsNullOrWhiteSpace(firstActive) ? SchoolBagsCategoryId : firstActive;
    }

    /// <summary>
    /// Ensures the Stationery top-level category and ~50 school/office supply products exist.
    /// Safe to call repeatedly — category is upserted and products are added only when their id
    /// is missing (e.g. via POST /api/setup/seed on a non-empty database).
    /// </summary>
    private static async Task EnsureStationeryCatalogAsync(BaglyDbContext db)
    {
        var categories = await db.Categories.ToListAsync();
        var byId = categories.ToDictionary(c => c.Id, c => c, StringComparer.OrdinalIgnoreCase);

        // Sort after School Bags (1) and its Boys/Girls/Kids children (2–4).
        UpsertCategory(db, byId, StationeryCategoryId, "Stationery", sortOrder: 5, parentId: null);
        await db.SaveChangesAsync();

        var existingIds = (await db.Products.Select(p => p.Id).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var newStationeryProducts = CreateStationeryProducts()
            .Where(p => !existingIds.Contains(p.Id))
            .ToList();

        if (newStationeryProducts.Count > 0)
        {
            db.Products.AddRange(newStationeryProducts);
            await db.SaveChangesAsync();
        }
    }

    // Stationery / desk / school-supply Unsplash photos, cycled across products (image + gallery).
    private static readonly string[] StationeryImagePool =
    [
        "photo-1452860606245-08befc0ff44b",
        "photo-1513542789411-b6a5d4f31634",
        "photo-1583485088034-697b8b9c3c4a",
        "photo-1606760227091-3dd870d97f1d",
        "photo-1586075010923-2dd457ba8392",
        "photo-1596495577886-d920f1fb7238",
        "photo-1611532736597-de2d4265fba3",
        "photo-1531346878377-a5be20888e57",
        "photo-1517842645767-c639042777db",
        "photo-1544816155-12df9643f363",
        "photo-1506784983877-45594efa4cbe",
        "photo-1434030216411-0b793f4b4173",
        "photo-1484480974693-6ca0a78fb36b",
        "photo-1455390582262-044cdead277a",
        "photo-1586281380349-632531db7ed4",
    ];

    private static string StationeryImage(int index, int offset) =>
        $"https://images.unsplash.com/{StationeryImagePool[(index + offset) % StationeryImagePool.Length]}?auto=format&fit=crop&w=900&q=80";

    private static IEnumerable<Product> CreateStationeryProducts()
    {
        var colorPool = new[] { "Forest Green", "Brass", "Ink Black", "Navy", "Cream", "Burgundy", "Slate Grey" };
        var materialPool = new[]
        {
            "Recycled Paper", "FSC Paper", "Stainless Steel", "Brass & Resin", "PU Leather",
            "Cotton Canvas", "ABS Plastic", "Wood & Metal", "Cardstock", "Polyester Blend",
        };
        var featurePool = new[]
        {
            "School & office ready", "Durable everyday build", "Gift-box friendly packaging",
            "Smooth write feel", "Compact desk footprint", "Refillable where noted",
            "Acid-free pages", "Sturdy binding", "Ergonomic grip", "Wipe-clean surfaces",
        };

        // Stable sequential ids (st-001 … st-050) keep re-seeds idempotent.
        string[] names =
        [
            "Forest Grove Spiral Notebook", "Brass Tip Ballpoint Pen Set", "Scholar Ruled Composition Book",
            "Inkwell Gel Pen Trio", "Desk Companion Sticky Notes", "Campus Highlighter Pack",
            "Heritage Leather Pencil Case", "Midnight Index Tabs", "Study Session Planner",
            "Oak Desk Pencil Cup", "Graph Grid Sketch Pad", "Classic Fountain Pen",
            "Math Set Compass Kit", "Erasure Soft Rubber Pack", "Color Burst Marker Set",
            "A4 Clipboard Portfolio", "Weekly Desk Pad Calendar", "Binder Ring Subject Folder",
            "Calligraphy Nib Starter Kit", "Pastel Sticky Flag Assortment", "Travel Pocket Notebook",
            "Brass Clip Paper Fasteners", "Recycled Kraft Journal", "Protractor & Ruler Duo",
            "Whiteboard Marker Four-Pack", "Canvas Roll Pencil Wrap", "Lined Index Card Bundle",
            "Desk Organizer Tray", "Mechanical Pencil 0.7mm", "Correction Tape Twin Pack",
            "Watercolor Brush Pen Set", "Hardcover Daily Diary", "Stapler & Staple Kit",
            "Washi Tape Accent Rolls", "Acrylic Desk Paperweight", "Scientific Calculator Soft Case",
            "Folder Expanding Accordion File", "Chalk Stick Classroom Pack", "Glitter Glue Stick Duo",
            "Letter Writing Stationery Set", "Clipboard with Storage Lid", "Fineliner Drawing Pens",
            "Hardcover Ring Binder", "Push-Pin Corkboard Assortment", "Scissors & Cutter Set",
            "Magnetic Fridge Memo Pad", "Ballpoint Refill Cartridge Pack", "Student Exam Pad Bundle",
            "Desk Lamp Bookmark Set", "Office Essentials Starter Kit",
        ];

        for (var i = 0; i < names.Length; i++)
        {
            var seq = i + 1;
            var name = names[i];
            var id = $"st-{seq:D3}";
            var material = materialPool[i % materialPool.Length];
            var primaryColor = colorPool[i % colorPool.Length];
            var secondaryColor = colorPool[(i + 3) % colorPool.Length];
            var price = Math.Round((99m + i * (2499m - 99m) / Math.Max(names.Length - 1, 1)) / 10m) * 10m;
            var compareAt = i % 5 == 0 ? Math.Round(price * 1.2m / 10m) * 10m : (decimal?)null;
            var stock = 40 + (i * 17) % 160;
            var rating = Math.Round(4.1 + (i % 9) * 0.09, 1);
            var reviews = 12 + (i * 7) % 180;
            var badge = i % 7 == 0 ? "Bestseller" : i % 7 == 3 ? "New" : null;
            var image = StationeryImage(i, 0);
            var gallery = new[] { image, StationeryImage(i + 1, 2) };
            var features = new[]
            {
                featurePool[i % featurePool.Length],
                featurePool[(i + 1) % featurePool.Length],
                featurePool[(i + 2) % featurePool.Length],
                featurePool[(i + 3) % featurePool.Length],
            };
            var shortDescription =
                $"{material} stationery in {primaryColor.ToLowerInvariant()} — ideal for school and desk.";
            var description =
                $"{name} brings forest-green & brass desk energy to everyday study and office work. " +
                $"Crafted with {material.ToLowerInvariant()} accents in {primaryColor.ToLowerInvariant()} " +
                $"with {secondaryColor.ToLowerInvariant()} details. A practical companion for notes, exams, and tidy desks.";

            yield return new Product
            {
                Id = id,
                Name = name,
                Category = StationeryCategoryId,
                SubCategoryId = null,
                Price = price,
                CompareAt = compareAt,
                Material = material,
                Rating = rating,
                Reviews = reviews,
                Badge = badge,
                ShortDescription = shortDescription,
                Description = description,
                Image = image,
                ColorsJson = JsonSerializer.Serialize(new[] { primaryColor, secondaryColor }, JsonOptions),
                FeaturesJson = JsonSerializer.Serialize(features, JsonOptions),
                GalleryJson = JsonSerializer.Serialize(gallery, JsonOptions),
                IsActive = true,
                StockQuantity = stock,
                CreatedAt = DateTime.UtcNow,
                Slug = id,
                SeoTitle = $"{name} | Bagly Stationery",
                SeoDescription = shortDescription.Length <= 300 ? shortDescription : shortDescription[..300],
                SeoKeywords = "stationery, school supplies, office supplies, notebook, pen, bagly",
            };
        }
    }

    private static void UpsertCategory(
        BaglyDbContext db,
        Dictionary<string, Category> byId,
        string id,
        string label,
        int sortOrder,
        string? parentId)
    {
        if (byId.TryGetValue(id, out var existing))
        {
            existing.Label = label;
            existing.SortOrder = sortOrder;
            existing.IsActive = true;
            existing.ParentId = parentId;
            return;
        }

        var category = new Category
        {
            Id = id,
            Label = label,
            SortOrder = sortOrder,
            IsActive = true,
            ParentId = parentId,
        };
        db.Categories.Add(category);
        byId[id] = category;
    }

    // Verified, product-style Unsplash backpack photos, cycled across School Bags products
    // (image + gallery) — reuses the same "pattern" as the original demo seeder above.
    private static readonly string[] SchoolBagImagePool =
    [
        "photo-1774977867285-3c55012e1a56",
        "photo-1581605405669-fcdf81165afa",
        "photo-1535982330050-f1c2fb79ff78",
        "photo-1726726192148-af52008ff663",
        "photo-1577733975197-3b950ca5cabe",
        "photo-1622560481156-01fc7e1693e6",
        "photo-1726726192241-a36ce220c2b3",
        "photo-1650500426868-27a68714a4a4",
        "photo-1551974222-1d49f576a2a4",
        "photo-1765516833058-c322c800fee1",
        "photo-1504424715129-fa3bcb0b8903",
        "photo-1553062407-98eeb64c6a62",
        "photo-1622560480605-d83c853bc5c3",
        "photo-1594608661623-aa0bd3a69d98",
        "photo-1495968283540-e1df41995ba6",
    ];

    private static string SchoolBagImage(int index, int offset) =>
        $"https://images.unsplash.com/{SchoolBagImagePool[(index + offset) % SchoolBagImagePool.Length]}?auto=format&fit=crop&w=900&q=80";

    private static IEnumerable<Product> CreateSchoolBagProducts()
    {
        var boys = GenerateSchoolBagVariant(
            subCategoryId: "boys",
            subCategoryLabel: "boys",
            imageOffset: 0,
            colorPool: ["Navy Blue", "Charcoal Grey", "Racing Red", "Forest Green", "Jet Black", "Steel Blue", "Burnt Orange"],
            featurePool:
            [
                "Padded laptop/tablet sleeve", "Reinforced bottom panel", "Adjustable padded shoulder straps",
                "Side mesh water bottle pockets", "Multiple zip compartments", "Breathable back padding",
            ],
            names:
            [
                "Turbo Blast Backpack", "Galaxy Racer School Bag", "Thunder Bolt Backpack", "Cricket Champion Bag",
                "Robo Warrior Backpack", "Dino Explorer School Bag", "Football Star Backpack", "Space Mission Bag",
                "Jungle Safari Backpack", "Superhero Squad Bag", "Racing Stripe Backpack", "Camo Commando Bag",
                "Shark Attack Backpack", "Pirate Adventure Bag", "Dragon Force Backpack", "Skate Park Bag",
                "Ninja Strike Backpack", "Rocket Ship School Bag", "Wild Tiger Backpack", "Champion League Bag",
            ]);

        var girls = GenerateSchoolBagVariant(
            subCategoryId: "girls",
            subCategoryLabel: "girls",
            imageOffset: 5,
            colorPool: ["Blush Pink", "Lavender", "Lilac Purple", "Peach", "Rose Gold", "Mint Green", "Soft White"],
            featurePool:
            [
                "Cute front pocket detailing", "Adjustable padded shoulder straps", "Roomy main compartment",
                "Side mesh water bottle pockets", "Sparkle print finish", "Breathable back padding",
            ],
            names:
            [
                "Unicorn Dream Backpack", "Princess Sparkle School Bag", "Rainbow Magic Backpack", "Butterfly Garden Bag",
                "Floral Charm Backpack", "Mermaid Tales School Bag", "Sweet Blossom Bag", "Star Dazzle Backpack",
                "Fairy Tale School Bag", "Polka Dot Petal Bag", "Glitter Bow Backpack", "Cherry Blossom Bag",
                "Ballerina Dream Backpack", "Candy Pop School Bag", "Heart & Hues Bag", "Sunshine Daisy Backpack",
                "Kitty Whiskers Bag", "Pastel Cloud Backpack", "Bloom & Sparkle School Bag", "Moonlight Fairy Bag",
            ]);

        var kids = GenerateSchoolBagVariant(
            subCategoryId: "kids",
            subCategoryLabel: "little kids",
            imageOffset: 10,
            colorPool: ["Sunshine Yellow", "Sky Blue", "Mint Green", "Coral", "Lilac", "Cream", "Turquoise"],
            featurePool:
            [
                "Lightweight & easy to carry", "Wipe-clean lining", "Chest strap for extra stability",
                "Fun front pocket for small items", "Rounded safe-edge design", "Breathable back padding",
            ],
            names:
            [
                "Little Explorer Mini Backpack", "Cartoon Buddy School Bag", "Puppy Pal Backpack", "Panda Cub School Bag",
                "Bunny Hop Backpack", "Choo Choo Train Bag", "Happy Farm Backpack", "ABC Learner Bag",
                "Teddy Bear Backpack", "Jungle Friends School Bag", "Rocket Tot Backpack", "Cloud Nine Mini Bag",
                "Baby Dino Backpack", "Fun Friends School Bag", "Playtime Pals Backpack", "Little Star Bag",
                "Kindergarten Buddy Backpack", "Chirpy Bird School Bag", "Sunny Day Mini Backpack", "First Day Hero Bag",
            ]);

        var extraBoys = GenerateExtendedSchoolBagProducts(
            subCategoryId: "boys",
            subCategoryLabel: "boys",
            startIndex: 21,
            count: 100,
            imageOffset: 0,
            colorPool: ["Navy Blue", "Charcoal Grey", "Racing Red", "Forest Green", "Jet Black", "Steel Blue", "Burnt Orange"],
            featurePool:
            [
                "Padded laptop/tablet sleeve", "Reinforced bottom panel", "Adjustable padded shoulder straps",
                "Side mesh water bottle pockets", "Multiple zip compartments", "Breathable back padding",
            ],
            themes:
            [
                "Turbo", "Galaxy", "Thunder", "Cricket", "Robo", "Dino", "Football", "Space", "Jungle", "Superhero",
                "Racing", "Camo", "Shark", "Pirate", "Dragon", "Skate", "Ninja", "Rocket", "Wild", "Champion",
                "Blaze", "Storm", "Velocity", "Apex", "Titan", "Falcon", "Cobra", "Vortex", "Blitz", "Stealth",
                "Matrix", "Neon", "Circuit", "Atomic", "Fusion", "Hyper", "Quantum", "Pulse", "Surge", "Strike",
                "Alpha", "Omega", "Prime", "Elite", "Pro", "Max", "Ultra", "Mega", "Power", "Force",
            ],
            styles:
            [
                "Blast", "Racer", "Bolt", "Champion", "Warrior", "Explorer", "Star", "Mission", "Safari", "Squad",
                "Stripe", "Commando", "Attack", "Adventure", "Force", "Park", "Ship", "Tiger", "League", "Strike",
                "Edge", "Rush", "Drive", "Shift", "Gear", "Mode", "Zone", "Wave", "Spark", "Core",
            ],
            suffixes: ["Backpack", "School Bag"]);

        var extraGirls = GenerateExtendedSchoolBagProducts(
            subCategoryId: "girls",
            subCategoryLabel: "girls",
            startIndex: 21,
            count: 100,
            imageOffset: 5,
            colorPool: ["Blush Pink", "Lavender", "Lilac Purple", "Peach", "Rose Gold", "Mint Green", "Soft White"],
            featurePool:
            [
                "Cute front pocket detailing", "Adjustable padded shoulder straps", "Roomy main compartment",
                "Side mesh water bottle pockets", "Sparkle print finish", "Breathable back padding",
            ],
            themes:
            [
                "Unicorn", "Princess", "Rainbow", "Butterfly", "Floral", "Mermaid", "Sweet", "Star", "Fairy", "Polka",
                "Glitter", "Cherry", "Ballerina", "Candy", "Heart", "Sunshine", "Kitty", "Pastel", "Bloom", "Moonlight",
                "Sparkle", "Dream", "Magic", "Petal", "Blossom", "Dazzle", "Charm", "Glow", "Shimmer", "Twinkle",
                "Crystal", "Pearl", "Velvet", "Silk", "Ribbon", "Lace", "Bloom", "Grace", "Delight", "Wonder",
                "Pixie", "Frost", "Aurora", "Coral", "Honey", "Berry", "Rose", "Lily", "Daisy", "Poppy",
            ],
            styles:
            [
                "Dream", "Sparkle", "Magic", "Garden", "Charm", "Tales", "Blossom", "Dazzle", "Tale", "Dot",
                "Bow", "Blossom", "Dream", "Pop", "Hues", "Daisy", "Whiskers", "Cloud", "Sparkle", "Fairy",
                "Glow", "Shine", "Bliss", "Bloom", "Grace", "Wish", "Glow", "Mist", "Light", "Glow",
            ],
            suffixes: ["Backpack", "School Bag"]);

        var extraKids = GenerateExtendedSchoolBagProducts(
            subCategoryId: "kids",
            subCategoryLabel: "little kids",
            startIndex: 21,
            count: 100,
            imageOffset: 10,
            colorPool: ["Sunshine Yellow", "Sky Blue", "Mint Green", "Coral", "Lilac", "Cream", "Turquoise"],
            featurePool:
            [
                "Lightweight & easy to carry", "Wipe-clean lining", "Chest strap for extra stability",
                "Fun front pocket for small items", "Rounded safe-edge design", "Breathable back padding",
            ],
            themes:
            [
                "Little", "Cartoon", "Puppy", "Panda", "Bunny", "Choo Choo", "Happy", "ABC", "Teddy", "Jungle",
                "Rocket", "Cloud", "Baby", "Fun", "Playtime", "Little", "Kindergarten", "Chirpy", "Sunny", "First Day",
                "Tiny", "Mini", "Cute", "Happy", "Bright", "Snuggly", "Cheery", "Giggly", "Bouncy", "Sprout",
                "Peewee", "Wee", "Itty", "Bitty", "Pint", "Small", "Sweet", "Soft", "Cuddly", "Gentle",
                "Buddy", "Pal", "Friend", "Mate", "Chum", "Sidekick", "Partner", "Companion", "Helper", "Hero",
            ],
            styles:
            [
                "Explorer", "Buddy", "Pal", "Cub", "Hop", "Train", "Farm", "Learner", "Bear", "Friends",
                "Tot", "Nine", "Dino", "Friends", "Pals", "Star", "Buddy", "Bird", "Day", "Hero",
                "Steps", "Smile", "Giggle", "Hop", "Skip", "Jump", "Play", "Learn", "Grow", "Shine",
            ],
            suffixes: ["Mini Backpack", "School Bag"]);

        return boys.Concat(girls).Concat(kids)
            .Concat(extraBoys).Concat(extraGirls).Concat(extraKids);
    }

    /// <summary>
    /// Generates additional School Bags products with stable sequential ids (e.g. sb-boys-021 … sb-boys-120).
    /// </summary>
    private static IEnumerable<Product> GenerateExtendedSchoolBagProducts(
        string subCategoryId,
        string subCategoryLabel,
        int startIndex,
        int count,
        int imageOffset,
        string[] colorPool,
        string[] featurePool,
        string[] themes,
        string[] styles,
        string[] suffixes)
    {
        var materials = new[] { "Waterproof Polyester", "Ripstop Nylon", "Durable Canvas", "Polyester Blend", "Oxford Fabric" };

        for (var n = 0; n < count; n++)
        {
            var seq = startIndex + n;
            var theme = themes[n % themes.Length];
            var style = styles[(n / themes.Length + n) % styles.Length];
            var suffix = suffixes[(n / (themes.Length * styles.Length) + n) % suffixes.Length];
            var name = $"{theme} {style} {suffix}";
            var id = $"sb-{subCategoryId}-{seq:D3}";
            var material = materials[n % materials.Length];
            var primaryColor = colorPool[n % colorPool.Length];
            var secondaryColor = colorPool[(n + 2) % colorPool.Length];
            var price = Math.Round((799m + n * (4999m - 799m) / Math.Max(count - 1, 1)) / 10m) * 10m;
            var compareAt = n % 4 == 0 ? Math.Round(price * 1.18m / 10m) * 10m : (decimal?)null;
            var stock = 25 + (n * 11) % 90;
            var rating = Math.Round(4.0 + (n % 10) * 0.09, 1);
            var reviews = 35 + (n * 13) % 260;
            var badge = n % 6 == 0 ? "Bestseller" : n % 6 == 3 ? "New" : null;
            var image = SchoolBagImage(seq, imageOffset);
            var gallery = new[] { image, SchoolBagImage(seq + 1, imageOffset) };
            var features = new[]
            {
                featurePool[n % featurePool.Length],
                featurePool[(n + 1) % featurePool.Length],
                featurePool[(n + 2) % featurePool.Length],
                featurePool[(n + 3) % featurePool.Length],
            };

            yield return new Product
            {
                Id = id,
                Name = name,
                Category = SchoolBagsCategoryId,
                SubCategoryId = subCategoryId,
                Price = price,
                CompareAt = compareAt,
                Material = material,
                Rating = rating,
                Reviews = reviews,
                Badge = badge,
                ShortDescription = $"{material} school bag in {primaryColor.ToLowerInvariant()}, made for {subCategoryLabel}.",
                Description =
                    $"{name} is a durable {material.ToLowerInvariant()} school bag in {primaryColor.ToLowerInvariant()} " +
                    $"with {secondaryColor.ToLowerInvariant()} accents. Roomy compartments, comfortable padded straps, " +
                    $"and everyday-tough construction make it a reliable pick for {subCategoryLabel} heading to school.",
                Image = image,
                ColorsJson = JsonSerializer.Serialize(new[] { primaryColor, secondaryColor }, JsonOptions),
                FeaturesJson = JsonSerializer.Serialize(features, JsonOptions),
                GalleryJson = JsonSerializer.Serialize(gallery, JsonOptions),
                IsActive = true,
                StockQuantity = stock,
                CreatedAt = DateTime.UtcNow,
            };
        }
    }

    private static IEnumerable<Product> GenerateSchoolBagVariant(
        string subCategoryId,
        string subCategoryLabel,
        int imageOffset,
        string[] colorPool,
        string[] featurePool,
        string[] names)
    {
        var materials = new[] { "Waterproof Polyester", "Ripstop Nylon", "Durable Canvas", "Polyester Blend", "Oxford Fabric" };

        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i];
            var id = $"{subCategoryId}-{ProductMapper.Slugify(name)}";
            var material = materials[i % materials.Length];
            var primaryColor = colorPool[i % colorPool.Length];
            var secondaryColor = colorPool[(i + 2) % colorPool.Length];
            var price = Math.Round((799m + i * (4999m - 799m) / (names.Length - 1)) / 10m) * 10m;
            var compareAt = i % 4 == 0 ? Math.Round(price * 1.18m / 10m) * 10m : (decimal?)null;
            var stock = 25 + (i * 11) % 90;
            var rating = Math.Round(4.0 + (i % 10) * 0.09, 1);
            var reviews = 35 + (i * 13) % 260;
            var badge = i % 6 == 0 ? "Bestseller" : i % 6 == 3 ? "New" : null;
            var image = SchoolBagImage(i, imageOffset);
            var gallery = new[] { image, SchoolBagImage(i + 1, imageOffset) };
            var features = new[]
            {
                featurePool[i % featurePool.Length],
                featurePool[(i + 1) % featurePool.Length],
                featurePool[(i + 2) % featurePool.Length],
                featurePool[(i + 3) % featurePool.Length],
            };

            yield return new Product
            {
                Id = id,
                Name = name,
                Category = SchoolBagsCategoryId,
                SubCategoryId = subCategoryId,
                Price = price,
                CompareAt = compareAt,
                Material = material,
                Rating = rating,
                Reviews = reviews,
                Badge = badge,
                ShortDescription = $"{material} school bag in {primaryColor.ToLowerInvariant()}, made for {subCategoryLabel}.",
                Description =
                    $"{name} is a durable {material.ToLowerInvariant()} school bag in {primaryColor.ToLowerInvariant()} " +
                    $"with {secondaryColor.ToLowerInvariant()} accents. Roomy compartments, comfortable padded straps, " +
                    $"and everyday-tough construction make it a reliable pick for {subCategoryLabel} heading to school.",
                Image = image,
                ColorsJson = JsonSerializer.Serialize(new[] { primaryColor, secondaryColor }, JsonOptions),
                FeaturesJson = JsonSerializer.Serialize(features, JsonOptions),
                GalleryJson = JsonSerializer.Serialize(gallery, JsonOptions),
                IsActive = true,
                StockQuantity = stock,
                CreatedAt = DateTime.UtcNow,
            };
        }
    }

    private static IEnumerable<Product> CreateProducts() =>
    [
        Create(
            "atelier-tote",
            "Atelier Leather Tote",
            "tote",
            14999,
            17999,
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
            9999,
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
            ],
            stockQuantity: 0
        ),
        Create(
            "city-sling",
            "City Sling",
            "crossbody",
            5999,
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
            18999,
            21999,
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
            16999,
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
            3999,
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
            11999,
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
            7499,
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
        string[] gallery,
        int stockQuantity = 999
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
            StockQuantity = stockQuantity,
        };
}
