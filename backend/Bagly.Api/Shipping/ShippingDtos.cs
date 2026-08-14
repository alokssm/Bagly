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
    DateTime? UpdatedAt
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
    int AwbCount
);

public record CourierOptionDto(
    int CourierId,
    string CourierName,
    decimal Rate,
    string? EstimatedDelivery,
    int? EstimatedDeliveryDays
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
