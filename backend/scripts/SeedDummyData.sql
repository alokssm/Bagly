/*
  Seed dummy data for BaglyDb
  Safe to re-run: skips inserts when sample rows already exist.
*/

USE BaglyDb;
GO

/* ---------- Categories ---------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Id = N'all')
BEGIN
    INSERT INTO dbo.Categories (Id, Label, SortOrder) VALUES
    (N'all', N'All bags', 0),
    (N'tote', N'Totes', 1),
    (N'backpack', N'Backpacks', 2),
    (N'crossbody', N'Crossbody', 3),
    (N'travel', N'Travel', 4),
    (N'work', N'Work', 5);
END
GO

/* ---------- Products ---------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Products WHERE Id = N'atelier-tote')
BEGIN
    INSERT INTO dbo.Products
    (Id, Name, Category, Price, CompareAt, Material, Rating, Reviews, Badge, ShortDescription, Description, Image, ColorsJson, FeaturesJson, GalleryJson, IsActive, CreatedAt)
    VALUES
    (N'atelier-tote', N'Atelier Leather Tote', N'tote', 248.00, 295.00, N'Full-grain leather', 4.9, 128, N'Bestseller',
     N'A structured everyday tote with quiet luxury proportions.',
     N'Hand-finished full-grain leather with a roomy interior, laptop sleeve, and magnetic closure.',
     N'https://images.unsplash.com/photo-1591561954557-26941169b49e?auto=format&fit=crop&w=900&q=80',
     N'["Cognac","Ink","Olive"]',
     N'["15\" laptop sleeve","Interior zip pocket","Detachable shoulder strap","Brass hardware"]',
     N'["https://images.unsplash.com/photo-1591561954557-26941169b49e?auto=format&fit=crop&w=900&q=80","https://images.unsplash.com/photo-1548036328-c9fa89d128fa?auto=format&fit=crop&w=900&q=80"]',
     1, SYSUTCDATETIME()),
    (N'trail-pack', N'Trail Daypack', N'backpack', 168.00, NULL, N'Recycled nylon', 4.8, 96, N'New',
     N'Lightweight daypack with weather-ready shell.',
     N'A streamlined 22L pack with padded straps and water-resistant zippers.',
     N'https://images.unsplash.com/photo-1553062407-98eeb64c6a62?auto=format&fit=crop&w=900&q=80',
     N'["Forest","Slate","Sand"]',
     N'["22L capacity","Padded laptop sleeve","Water-resistant shell","Sternum strap"]',
     N'["https://images.unsplash.com/photo-1553062407-98eeb64c6a62?auto=format&fit=crop&w=900&q=80"]',
     1, SYSUTCDATETIME()),
    (N'city-sling', N'City Sling', N'crossbody', 98.00, NULL, N'Saffiano leather', 4.7, 214, NULL,
     N'Compact crossbody for phone, keys, and cards.',
     N'Minimal silhouette with RFID card slots and an adjustable strap.',
     N'https://images.unsplash.com/photo-1566150905458-1bf1fc113f0d?auto=format&fit=crop&w=900&q=80',
     N'["Black","Camel"]',
     N'["RFID card slots","Adjustable strap","Quick-access pocket","Soft lining"]',
     N'["https://images.unsplash.com/photo-1566150905458-1bf1fc113f0d?auto=format&fit=crop&w=900&q=80"]',
     1, SYSUTCDATETIME()),
    (N'weekender-duffel', N'Weekender Duffel', N'travel', 312.00, 360.00, N'Waxed canvas + leather', 4.9, 67, N'Limited',
     N'Carry-on ready duffel with leather accents.',
     N'A spacious weekender with shoe compartment and trolley sleeve.',
     N'https://images.unsplash.com/photo-1553413077-190dd305871c?auto=format&fit=crop&w=900&q=80',
     N'["Espresso","Navy"]',
     N'["Shoe compartment","Trolley sleeve","Lockable zippers","Leather trim"]',
     N'["https://images.unsplash.com/photo-1553413077-190dd305871c?auto=format&fit=crop&w=900&q=80"]',
     1, SYSUTCDATETIME()),
    (N'brief-folio', N'Brief Folio', N'work', 275.00, NULL, N'Vegetable-tanned leather', 4.8, 54, NULL,
     N'Slim work bag that holds a laptop and documents.',
     N'Clean lines for the office with organizer panel and optional strap.',
     N'https://images.unsplash.com/photo-1598532163257-ae3c6b2524b6?auto=format&fit=crop&w=900&q=80',
     N'["Charcoal","Tan"]',
     N'["14\" laptop fit","Document pocket","Organizer panel","Optional strap"]',
     N'["https://images.unsplash.com/photo-1598532163257-ae3c6b2524b6?auto=format&fit=crop&w=900&q=80"]',
     1, SYSUTCDATETIME()),
    (N'canvas-market', N'Canvas Market Bag', N'tote', 72.00, NULL, N'Organic canvas', 4.6, 301, NULL,
     N'Soft structured tote for errands and weekends.',
     N'Heavyweight organic canvas with reinforced base and interior pocket.',
     N'https://images.unsplash.com/photo-1622560480654-d96214fdc887?auto=format&fit=crop&w=900&q=80',
     N'["Natural","Sage","Ink"]',
     N'["Reinforced base","Interior pocket","Long handles","Machine washable"]',
     N'["https://images.unsplash.com/photo-1622560480654-d96214fdc887?auto=format&fit=crop&w=900&q=80"]',
     1, SYSUTCDATETIME()),
    (N'nomad-backpack', N'Nomad Commute Pack', N'backpack', 198.00, NULL, N'Ballistic nylon', 4.9, 142, N'Bestseller',
     N'Clamshell backpack built for modern desks.',
     N'Opens flat like a suitcase with tech compartment and luggage pass-through.',
     N'https://images.unsplash.com/photo-1581605405669-fcdf81165afa?auto=format&fit=crop&w=900&q=80',
     N'["Olive","Black"]',
     N'["Clamshell opening","Tech organizer","Passport pocket","Luggage pass-through"]',
     N'["https://images.unsplash.com/photo-1581605405669-fcdf81165afa?auto=format&fit=crop&w=900&q=80"]',
     1, SYSUTCDATETIME()),
    (N'horizon-crossbody', N'Horizon Crossbody', N'crossbody', 128.00, NULL, N'Soft calf leather', 4.7, 88, NULL,
     N'Soft leather bag with room for a compact camera.',
     N'Supple calf leather with zip-top closure and adjustable strap.',
     N'https://images.unsplash.com/photo-1584917865442-de89df76afd3?auto=format&fit=crop&w=900&q=80',
     N'["Rust","Stone","Black"]',
     N'["Zip-top closure","Interior divider","Adjustable strap","Dust bag included"]',
     N'["https://images.unsplash.com/photo-1584917865442-de89df76afd3?auto=format&fit=crop&w=900&q=80"]',
     1, SYSUTCDATETIME());
END
GO

/* ---------- Sample carts ---------- */
DECLARE @Cart1 UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';
DECLARE @Cart2 UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';

IF NOT EXISTS (SELECT 1 FROM dbo.Carts WHERE Id = @Cart1)
BEGIN
    INSERT INTO dbo.Carts (Id, UpdatedAt) VALUES
    (@Cart1, SYSUTCDATETIME()),
    (@Cart2, SYSUTCDATETIME());

    INSERT INTO dbo.CartItems (CartId, ProductId, ProductName, Image, Color, UnitPrice, Quantity)
    VALUES
    (@Cart1, N'atelier-tote', N'Atelier Leather Tote',
     N'https://images.unsplash.com/photo-1591561954557-26941169b49e?auto=format&fit=crop&w=900&q=80',
     N'Cognac', 248.00, 1),
    (@Cart1, N'city-sling', N'City Sling',
     N'https://images.unsplash.com/photo-1566150905458-1bf1fc113f0d?auto=format&fit=crop&w=900&q=80',
     N'Black', 98.00, 2),
    (@Cart2, N'nomad-backpack', N'Nomad Commute Pack',
     N'https://images.unsplash.com/photo-1581605405669-fcdf81165afa?auto=format&fit=crop&w=900&q=80',
     N'Olive', 198.00, 1),
    (@Cart2, N'horizon-crossbody', N'Horizon Crossbody',
     N'https://images.unsplash.com/photo-1584917865442-de89df76afd3?auto=format&fit=crop&w=900&q=80',
     N'Rust', 128.00, 1);
END
GO

/* ---------- Sample orders ---------- */
DECLARE @Order1 UNIQUEIDENTIFIER = '33333333-3333-3333-3333-333333333333';
DECLARE @Order2 UNIQUEIDENTIFIER = '44444444-4444-4444-4444-444444444444';
DECLARE @Order3 UNIQUEIDENTIFIER = '55555555-5555-5555-5555-555555555555';

IF NOT EXISTS (SELECT 1 FROM dbo.Orders WHERE OrderNumber = N'BG-DEMO-1001')
BEGIN
    INSERT INTO dbo.Orders
    (Id, OrderNumber, Email, FirstName, LastName, Address, City, State, Zip, Country, Subtotal, Shipping, Total, Status, CreatedAt)
    VALUES
    (@Order1, N'BG-DEMO-1001', N'ada@example.com', N'Ada', N'Lovelace', N'12 Analytical Engine Ave', N'London', N'LDN', N'EC1A 1BB', N'United Kingdom',
     346.00, 0.00, 346.00, N'Confirmed', DATEADD(day, -5, SYSUTCDATETIME())),
    (@Order2, N'BG-DEMO-1002', N'grace@example.com', N'Grace', N'Hopper', N'88 Compiler Road', N'New York', N'NY', N'10001', N'United States',
     168.00, 12.00, 180.00, N'Shipped', DATEADD(day, -2, SYSUTCDATETIME())),
    (@Order3, N'BG-DEMO-1003', N'alan@example.com', N'Alan', N'Turing', N'1 Bletchley Park', N'Milton Keynes', N'BKM', N'MK3 6EB', N'United Kingdom',
     512.00, 0.00, 512.00, N'Processing', DATEADD(hour, -8, SYSUTCDATETIME()));

    INSERT INTO dbo.OrderItems (OrderId, ProductId, ProductName, Color, UnitPrice, Quantity)
    VALUES
    (@Order1, N'atelier-tote', N'Atelier Leather Tote', N'Ink', 248.00, 1),
    (@Order1, N'city-sling', N'City Sling', N'Camel', 98.00, 1),
    (@Order2, N'trail-pack', N'Trail Daypack', N'Forest', 168.00, 1),
    (@Order3, N'weekender-duffel', N'Weekender Duffel', N'Navy', 312.00, 1),
    (@Order3, N'canvas-market', N'Canvas Market Bag', N'Sage', 72.00, 1),
    (@Order3, N'horizon-crossbody', N'Horizon Crossbody', N'Stone', 128.00, 1);
END
GO

PRINT 'Bagly dummy data seed completed.';
GO
