namespace Bagly.Api.DTOs;

public record ProductDto(
    string Id,
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
    int StockQuantity,
    bool IsAvailable
);

public record CategoryDto(string Id, string Label, int SortOrder = 0);

public record LoginRequest(string Email, string Password);

public record LoginResponse(string Token, string Email, string Name, string Role, DateTime ExpiresAt);

public record CustomerRegisterRequest(string Name, string Email, string Password, string ConfirmPassword);

public record CustomerLoginRequest(string Email, string Password);

public record CustomerGoogleLoginRequest(string IdToken);

public record CustomerAuthResponse(string Token, Guid Id, string Email, string Name, DateTime ExpiresAt);

public record UpsertCategoryRequest(string Id, string Label, int SortOrder);

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
    int StockQuantity = 999
);

public record AdminProductDto(
    string Id,
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
    bool IsActive,
    int StockQuantity,
    bool IsAvailable,
    DateTime CreatedAt
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
