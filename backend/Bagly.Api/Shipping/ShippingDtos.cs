namespace Bagly.Api.Shipping;

public record AdminShippingShipmentDto(
    Guid Id,
    string PickupLocation,
    string? ShiprocketOrderId,
    string? ShiprocketShipmentId,
    string? Status,
    string? ShippingStatus,
    string? LastError,
    string? AwbCode,
    int? CourierId,
    string? CourierName,
    decimal? ActualShippingCharge,
    DateTime? ReadyToShipAt,
    DateTime? AwbAssignedAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? SellerReadyToShipAt = null,
    bool SellerReady = false,
    string? LabelUrl = null,
    DateTime? LabelGeneratedAt = null,
    DateTime? PickupRequestedAt = null,
    string? PickupTokenNumber = null,
    string? TrackingStatus = null,
    DateTime? TrackingStatusUpdatedAt = null,
    string? ManifestUrl = null,
    DateTime? ManifestGeneratedAt = null
);

public record AdminShippingOrderDto(
    Guid Id,
    string OrderNumber,
    string CustomerName,
    string Email,
    string Status,
    string PaymentStatus,
    string? PaymentProvider,
    string? Currency,
    decimal Total,
    decimal Shipping,
    string? Zip,
    string? Phone,
    DateTime CreatedAt,
    IReadOnlyList<AdminShippingShipmentDto> Shipments
);

public record AdminShippingOrdersResult(
    IReadOnlyList<AdminShippingOrderDto> Items,
    int TotalCount,
    string Tab,
    int NewCount,
    int ReadyCount,
    int AssignAwbCount,
    int LabelCount,
    int LabeledCount,
    int PickupCount = 0,
    int ManifestCount = 0,
    int InProgressCount = 0
);

public record CourierOptionDto(
    int CourierId,
    string CourierName,
    /// <summary>Total shipping charge (panel-aligned): Freight + Coverage + WhatsApp [+ COD].</summary>
    decimal Rate,
    string? EstimatedDelivery,
    int? EstimatedDeliveryDays,
    decimal FreightCharge = 0,
    decimal CoverageCharge = 0,
    decimal WhatsAppCharge = 0,
    decimal CodCharge = 0,
    /// <summary>Shiprocket courier <c>rating</c>.</summary>
    decimal? Rating = null,
    /// <summary>Human-readable expected pickup from Shiprocket courier object.</summary>
    string? ExpectedPickup = null
);

public record ReadyToShipResponse(
    Guid ShipmentId,
    Guid OrderId,
    string PickupLocation,
    string? ShiprocketShipmentId,
    int PickupPostcode,
    int DeliveryPostcode,
    bool Cod,
    double WeightKg,
    double Length,
    double Breadth,
    double Height,
    decimal DeclaredValue,
    string ShippingStatus,
    DateTime? ReadyToShipAt,
    IReadOnlyList<CourierOptionDto> Couriers
);

public record AssignAwbRequest(int CourierId, decimal? Rate = null);

public record AssignAwbResponse(
    Guid ShipmentId,
    Guid OrderId,
    string PickupLocation,
    string? AwbCode,
    int? CourierId,
    string? CourierName,
    decimal? ActualShippingCharge,
    string ShippingStatus,
    DateTime? AwbAssignedAt
);

public record GenerateLabelResponse(
    Guid ShipmentId,
    Guid OrderId,
    string PickupLocation,
    string? AwbCode,
    string? LabelUrl,
    string ShippingStatus,
    DateTime? LabelGeneratedAt
);

public record RequestPickupResponse(
    Guid ShipmentId,
    Guid OrderId,
    string PickupLocation,
    string? AwbCode,
    string? PickupTokenNumber,
    string ShippingStatus,
    string? TrackingStatus,
    DateTime? PickupRequestedAt
);

public record GenerateManifestResponse(
    Guid ShipmentId,
    Guid OrderId,
    string PickupLocation,
    string? AwbCode,
    string? ManifestUrl,
    string ShippingStatus,
    DateTime? ManifestGeneratedAt
);

public record ShiprocketApiLogDto(
    long Id,
    Guid? OrderId,
    Guid? ShipmentId,
    string Action,
    string HttpMethod,
    string Url,
    string? RequestJson,
    int? ResponseStatus,
    string? ResponseJson,
    DateTime CreatedAtUtc,
    string? AdminEmail
);

public record ShipmentStatusLogDto(
    long Id,
    Guid OrderId,
    Guid OrderShiprocketShipmentId,
    string? AwbCode,
    string? ShiprocketShipmentId,
    string? FromStatus,
    string ToStatus,
    string Source,
    string? Message,
    string? RawJson,
    DateTime CreatedAtUtc
);

public record ShiprocketWebhookLogDto(
    long Id,
    DateTime ReceivedAtUtc,
    string HttpMethod,
    string Path,
    string? HeadersJson,
    string? RequestBody,
    int ResponseStatusCode,
    string? ResponseBody,
    bool ProcessedOk,
    string? ErrorMessage,
    Guid? MatchedOrderId,
    Guid? MatchedShipmentId,
    string? MappedStatus
);
