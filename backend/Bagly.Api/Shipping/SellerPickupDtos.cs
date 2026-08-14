using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Bagly.Api.Shipping;

public record SellerPickupLocationDto(
    Guid Id,
    string PickupLocation,
    string Name,
    string Email,
    string Phone,
    string Address,
    string? Address2,
    string City,
    string State,
    string Country,
    string PinCode,
    string? Lat,
    string? Long,
    string? Gstin,
    DateTime CreatedAt
);

public record SellerPickupListResponse(
    IReadOnlyList<SellerPickupLocationDto> Items,
    int Count,
    int MaxAllowed
);

public sealed class CreateSellerPickupRequest
{
    [Required]
    [MaxLength(36)]
    public string PickupLocation { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(15)]
    public string Phone { get; set; } = string.Empty;

    [Required]
    [MaxLength(80)]
    public string Address { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? Address2 { get; set; }

    [Required]
    [MaxLength(50)]
    public string City { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string State { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Country { get; set; }

    [Required]
    [MaxLength(12)]
    public string PinCode { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? Lat { get; set; }

    [MaxLength(30)]
    public string? Long { get; set; }

    [MaxLength(20)]
    public string? Gstin { get; set; }
}

/// <summary>Shiprocket <c>POST …/addpickup</c> body (snake_case JSON).</summary>
internal sealed class ShiprocketAddPickupPayload
{
    [JsonPropertyName("pickup_location")]
    public string PickupLocation { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("address_2")]
    public string Address2 { get; set; } = string.Empty;

    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; set; } = "India";

    /// <summary>Must serialize as a JSON number (e.g. 110001).</summary>
    [JsonPropertyName("pin_code")]
    public int PinCode { get; set; }

    [JsonPropertyName("lat")]
    public string? Lat { get; set; }

    [JsonPropertyName("long")]
    public string? Long { get; set; }

    [JsonPropertyName("gstin")]
    public string? Gstin { get; set; }
}
