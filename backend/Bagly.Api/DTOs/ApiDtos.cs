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
    bool InStock,
    string? Slug,
    string? SeoTitle,
    string? SeoDescription
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

public record SellerRegisterRequest(
    string Name,
    string BusinessName,
    string Email,
    string? Phone,
    string Password,
    string ConfirmPassword);

public record SellerRegisterResponse(
    Guid Id,
    string Email,
    string Name,
    string BusinessName,
    string Status,
    string Message);

public record SellerLoginRequest(string Email, string Password);

public record SellerAuthResponse(
    string Token,
    Guid Id,
    string Email,
    string Name,
    string BusinessName,
    string Status,
    DateTime ExpiresAt);

public record SellerProfileDto(
    Guid Id,
    string Email,
    string Name,
    string BusinessName,
    string? Phone,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? Pincode,
    string? Gstin,
    string? Description,
    string? UpiId,
    string Status,
    string? RejectionReason,
    DateTime? ApprovedAt,
    DateTime? ProfileSubmittedAt,
    bool ProfileComplete);

public record UpdateSellerProfileRequest(
    string Name,
    string BusinessName,
    string Phone,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string Pincode,
    string? Gstin,
    string? Description,
    string? UpiId);

public record AdminSellerListItemDto(
    Guid Id,
    string Email,
    string Name,
    string BusinessName,
    string? Phone,
    string? City,
    string? State,
    string? Gstin,
    string Status,
    string? RejectionReason,
    bool ProfileComplete,
    DateTime CreatedAt,
    DateTime? ProfileSubmittedAt,
    DateTime? ApprovedAt);

public record AdminSellerDetailDto(
    Guid Id,
    string Email,
    string Name,
    string BusinessName,
    string? Phone,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? Pincode,
    string? Gstin,
    string? Description,
    string? UpiId,
    string Status,
    string? RejectionReason,
    bool IsActive,
    bool ProfileComplete,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    DateTime? ProfileSubmittedAt,
    DateTime? ApprovedAt);

public record RejectSellerRequest(string? Reason);

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
    string? SubCategoryId = null,
    string? Slug = null,
    string? SeoTitle = null,
    string? SeoDescription = null,
    string? SeoKeywords = null,
    /// <summary>Shiprocket pickup nickname (e.g. home/work). Null/empty → platform default.</summary>
    string? ShiprocketPickupLocation = null,
    /// <summary>When true, use ShiprocketOptions package defaults. Default true.</summary>
    bool UseDefaultPackageSize = true,
    decimal? WeightKg = null,
    decimal? LengthCm = null,
    decimal? BreadthCm = null,
    decimal? HeightCm = null
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
    DateTime CreatedAt,
    string? Slug,
    string? SeoTitle,
    string? SeoDescription,
    string? SeoKeywords,
    Guid? SellerId = null,
    string? ShiprocketPickupLocation = null,
    bool UseDefaultPackageSize = true,
    decimal? WeightKg = null,
    decimal? LengthCm = null,
    decimal? BreadthCm = null,
    decimal? HeightCm = null
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
    DateTime CreatedAt,
    Guid? SellerId = null,
    string? ShiprocketPickupLocation = null,
    bool UseDefaultPackageSize = true,
    decimal? WeightKg = null,
    decimal? LengthCm = null,
    decimal? BreadthCm = null,
    decimal? HeightCm = null
);

/// <summary>Admin-only: set Shiprocket pickup nickname without full product CRUD.</summary>
public record PatchProductPickupLocationRequest(string? ShiprocketPickupLocation);

/// <summary>Admin-only: set Shiprocket pickup + package fields without full product CRUD.</summary>
public record PatchProductShippingRequest(
    string? ShiprocketPickupLocation = null,
    bool? UseDefaultPackageSize = null,
    decimal? WeightKg = null,
    decimal? LengthCm = null,
    decimal? BreadthCm = null,
    decimal? HeightCm = null
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
    IReadOnlyList<CheckoutItemRequest>? Items,
    /// <summary>Optional. "COD" / "CashOnDelivery" routes a create-order call through the cash-on-delivery
    /// path (no Razorpay) even for India. Null/anything else keeps the legacy behaviour.</summary>
    string? PaymentMethod = null,
    /// <summary>Optional customer phone. Required for Shiprocket shipment creation when enabled.</summary>
    string? Phone = null
);

public record OrderItemDto(
    string ProductId,
    string ProductName,
    string Color,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal
);

public record OrderShiprocketShipmentDto(
    Guid Id,
    string PickupLocation,
    string? ShiprocketOrderId,
    string? ShiprocketShipmentId,
    string? Status,
    string? LastError,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? ShippingStatus = null,
    string? AwbCode = null,
    int? CourierId = null,
    string? CourierName = null,
    decimal? ActualShippingCharge = null,
    DateTime? ReadyToShipAt = null,
    DateTime? AwbAssignedAt = null,
    string? LabelUrl = null,
    DateTime? LabelGeneratedAt = null
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
    IReadOnlyList<OrderItemDto> Items,
    string? Phone = null,
    string? ShiprocketOrderId = null,
    string? ShiprocketShipmentId = null,
    string? ShiprocketStatus = null,
    string? ShiprocketLastError = null,
    IReadOnlyList<OrderShiprocketShipmentDto>? ShiprocketShipments = null
);

public record CustomerOrderItemDto(
    string ProductId,
    string ProductName,
    string Color,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    string? Image
);

/// <summary>Append-only courier status event for storefront tracking (no admin/raw fields).</summary>
public record CustomerShipmentTrackingEventDto(
    string Status,
    DateTime ChangedAtUtc
);

/// <summary>Customer-visible shipment row — AWB / status / optional label; no admin workflow fields.</summary>
public record CustomerOrderShipmentDto(
    Guid Id,
    string? AwbCode,
    string? TrackingStatus,
    DateTime? TrackingStatusUpdatedAt,
    string? CourierName,
    string? LabelUrl,
    /// <summary>True when AWB exists or pickup/tracking has started.</summary>
    bool CanTrack,
    /// <summary>Shiprocket public tracking URL when AWB is present; otherwise null.</summary>
    string? PublicTrackingUrl,
    IReadOnlyList<CustomerShipmentTrackingEventDto> StatusHistory
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
    IReadOnlyList<CustomerOrderItemDto> Items,
    /// <summary>True when at least one shipment has AWB or tracking/pickup started.</summary>
    bool CanTrack = false,
    IReadOnlyList<CustomerOrderShipmentDto>? Shipments = null
);

/// <summary>Dedicated track payload for <c>GET /api/account/orders/{orderNumber}/track</c>.</summary>
public record CustomerOrderTrackDto(
    Guid OrderId,
    string OrderNumber,
    string Status,
    DateTime CreatedAt,
    bool CanTrack,
    IReadOnlyList<CustomerOrderShipmentDto> Shipments
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
    DateTime CreatedAt,
    string? Phone = null,
    string? ShiprocketOrderId = null,
    string? ShiprocketStatus = null,
    string? ShiprocketLastError = null,
    int ShiprocketShipmentCount = 0,
    int ShiprocketShipmentSuccessCount = 0);

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

public record ProductReviewDto(
    Guid Id,
    string ProductId,
    Guid CustomerUserId,
    string ReviewerName,
    int Rating,
    string? Comment,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsMine = false
);

public record CreateProductReviewRequest(int Rating, string? Comment);

public record UpdateProductReviewRequest(int Rating, string? Comment);

public record ProductReviewsResponse(
    string ProductId,
    double AverageRating,
    int ReviewCount,
    IReadOnlyList<ProductReviewDto> Reviews,
    bool? CanReview = null,
    bool? HasReviewed = null,
    ProductReviewDto? MyReview = null
);
