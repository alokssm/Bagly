namespace Bagly.Api.DTOs;

public record ProductDto(
    string Id,
    string Name,
    string Category,
    string? SubCategoryId,
    decimal Price,
    decimal? CompareAt,
    IReadOnlyList<string> Colors,
    string Material,
    double Rating,
    int Reviews,
    string? Badge,
    string ShortDescription,
    string Description,
    IReadOnlyList<string> Features,
    string Image,
    IReadOnlyList<string> Gallery,
    int StockQuantity,
    bool IsAvailable,
    bool InStock
);

public record CategoryDto(
    string Id,
    string Label,
    int SortOrder = 0,
    bool IsActive = true,
    string? ParentId = null
);

public record LoginRequest(string Email, string Password);

public record LoginResponse(string Token, string Email, string Name, string Role, DateTime ExpiresAt);

public record CustomerRegisterRequest(string Name, string Email, string Password, string ConfirmPassword);

public record CustomerLoginRequest(string Email, string Password);

public record CustomerGoogleLoginRequest(string IdToken);

public record CustomerAuthResponse(string Token, Guid Id, string Email, string Name, DateTime ExpiresAt);

public record UpdateCustomerProfileRequest(string Name);

public record UpsertCategoryRequest(
    string Id,
    string Label,
    int SortOrder,
    bool IsActive = true,
    string? ParentId = null
);

public record UpsertProductRequest(
    string? Id,
    string Name,
    string Category,
    decimal Price,
    decimal? CompareAt,
    IReadOnlyList<string> Colors,
    string Material,
    double Rating,
    int Reviews,
    string? Badge,
    string ShortDescription,
    string Description,
    IReadOnlyList<string> Features,
    string Image,
    IReadOnlyList<string> Gallery,
    bool IsActive = true,
    int StockQuantity = 999,
    string? SubCategoryId = null
);

public record AdminProductDto(
    string Id,
    string Name,
    string Category,
    string? SubCategoryId,
    decimal Price,
    decimal? CompareAt,
    IReadOnlyList<string> Colors,
    string Material,
    double Rating,
    int Reviews,
    string? Badge,
    string ShortDescription,
    string Description,
    IReadOnlyList<string> Features,
    string Image,
    IReadOnlyList<string> Gallery,
    bool IsActive,
    int StockQuantity,
    bool IsAvailable,
    DateTime CreatedAt
);

/// <summary>Lean row shape for the admin products list/table — avoids shipping gallery/features/colors JSON for every row.</summary>
public record AdminProductListItemDto(
    string Id,
    string Name,
    string Category,
    string? SubCategoryId,
    decimal Price,
    int StockQuantity,
    string Image,
    bool IsActive,
    bool IsAvailable,
    DateTime CreatedAt
);

public record ProductStatsDto(int TotalCount, int ActiveCount);

/// <summary>Admin categories list row — adds the parent's label so the table doesn't need a lookup.</summary>
public record AdminCategoryDto(
    string Id,
    string Label,
    int SortOrder,
    bool IsActive,
    string? ParentId,
    string? ParentLabel
);

public record AddCartItemRequest(string ProductId, string? Color, int Quantity = 1);

public record UpdateCartItemRequest(int Quantity);

public record CartItemDto(
    string ProductId,
    string Name,
    string Image,
    string Color,
    decimal Price,
    int Quantity
);

public record CartDto(
    Guid CartId,
    IReadOnlyList<CartItemDto> Items,
    int ItemCount,
    decimal Subtotal,
    decimal Shipping,
    decimal Total
);

public record CheckoutItemRequest(string ProductId, string Color, int Quantity);

public record CreateOrderRequest(
    string Email,
    string FirstName,
    string LastName,
    string Address,
    string City,
    string State,
    string Zip,
    string Country,
    Guid? CartId,
    IReadOnlyList<CheckoutItemRequest>? Items
);

public record OrderItemDto(
    string ProductId,
    string ProductName,
    string Color,
    decimal UnitPrice,
    int Quantity
);

public record OrderDto(
    Guid Id,
    string OrderNumber,
    string Email,
    string FirstName,
    string LastName,
    string Status,
    string PaymentStatus,
    string? PaymentProvider,
    string? Currency,
    decimal? AmountInr,
    string? RazorpayOrderId,
    string? RazorpayPaymentId,
    DateTime? PaidAtUtc,
    decimal Subtotal,
    decimal Shipping,
    decimal Total,
    DateTime CreatedAt,
    IReadOnlyList<OrderItemDto> Items
);

public record CustomerOrderItemDto(
    string ProductId,
    string ProductName,
    string Color,
    decimal UnitPrice,
    int Quantity,
    string? Image
);

public record CustomerOrderDto(
    Guid Id,
    string OrderNumber,
    string Status,
    string PaymentStatus,
    string? Currency,
    decimal Subtotal,
    decimal Shipping,
    decimal Total,
    DateTime CreatedAt,
    IReadOnlyList<CustomerOrderItemDto> Items
);

/// <summary>Lean row shape for the admin orders table — avoids shipping full line-item detail for every row.</summary>
public record AdminOrderListItemDto(
    Guid Id,
    string OrderNumber,
    string CustomerName,
    string Email,
    string Status,
    string PaymentStatus,
    string? PaymentProvider,
    string Currency,
    decimal Total,
    int ItemCount,
    DateTime CreatedAt);

/// <summary>Response shape for <c>GET /api/admin/orders</c>. <c>TodayCount</c> is always "today in
/// Asia/Kolkata (IST)" regardless of the from/to filter applied to <c>Items</c>.</summary>
public record AdminOrdersPagedResult(
    IReadOnlyList<AdminOrderListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    int TodayCount);

public record OrderStatusCountDto(string Status, int Count);

public record TopProductSoldDto(string ProductId, string ProductName, int QuantitySold, decimal Revenue);

/// <summary>Response shape for <c>GET /api/admin/analytics</c>. <c>OrdersToday</c>/<c>ThisWeek</c>/
/// <c>ThisMonth</c> are always computed in Asia/Kolkata (IST) and are independent of the from/to filter,
/// which instead scopes <c>TotalOrders</c>, <c>TotalRevenue</c>, <c>AverageOrderValue</c>,
/// <c>OrdersByStatus</c>, and <c>TopProducts</c>.</summary>
public record AdminAnalyticsDto(
    DateOnly? From,
    DateOnly? To,
    int TotalOrders,
    decimal TotalRevenue,
    decimal AverageOrderValue,
    int OrdersToday,
    int OrdersThisWeek,
    int OrdersThisMonth,
    IReadOnlyList<OrderStatusCountDto> OrdersByStatus,
    IReadOnlyList<TopProductSoldDto> TopProducts);

public record GoogleAuthConfigDto(bool Enabled, string? ClientId);

public record RazorpayConfigDto(bool Enabled, string? KeyId, string Currency);

public record RazorpayInitiateResponse(
    Guid OrderId,
    string OrderNumber,
    string RazorpayOrderId,
    string KeyId,
    long AmountPaise,
    decimal AmountInr,
    string Currency,
    string CustomerName,
    string CustomerEmail,
    string? Description,
    OrderDto Order);

public record RazorpayVerifyRequest(
    Guid OrderId,
    string RazorpayOrderId,
    string RazorpayPaymentId,
    string RazorpaySignature,
    Guid? CartId);

public record RazorpayFailureRequest(
    Guid OrderId,
    string? RazorpayOrderId,
    string? Code,
    string? Description);

public record ChatMessageDto(string Role, string Content, DateTime Timestamp);

public record ShippingAddressDto(
    Guid Id,
    string? Label,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string Address,
    string City,
    string State,
    string Zip,
    string Country,
    bool IsDefault,
    DateTime CreatedAt
);

public record ContactRequest(
    string FirstName,
    string LastName,
    string Phone,
    string Email,
    string? CompanyName,
    string Message
);

/// <summary>Body for the public, unauthenticated <c>POST /api/analytics/hit</c> beacon fired once
/// per storefront page navigation. <c>SessionId</c> is an optional client-generated GUID (stored in
/// localStorage) used only to compute "unique sessions" per location — never tied to an account.</summary>
public record SiteHitRequest(string Path, string? SessionId);

/// <summary>One row of the admin locations breakdown — <c>Hits</c> is total page views from that
/// country, <c>UniqueSessions</c> counts distinct client-generated session ids (nulls counted individually
/// since they can't be deduplicated), and <c>Percentage</c> is this row's share of <c>TotalHits</c>.</summary>
public record LocationHitDto(
    string Country,
    int Hits,
    int UniqueSessions,
    double Percentage);

/// <summary>Response shape for <c>GET /api/admin/analytics/locations</c>. Top 50 countries by hit
/// count, plus grand totals for the selected (optional) date range.</summary>
public record AdminLocationsAnalyticsDto(
    DateOnly? From,
    DateOnly? To,
    int TotalHits,
    int UniqueSessions,
    IReadOnlyList<LocationHitDto> Locations);

public record UpsertShippingAddressRequest(
    string? Label,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string Address,
    string City,
    string State,
    string Zip,
    string Country,
    bool IsDefault = false
);
