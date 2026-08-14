using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Bagly.Api.Data;
using Bagly.Api.Models;
using Bagly.Api.Options;
using Bagly.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Shipping;

public interface ISellerPickupService
{
    const int MaxPickupsPerSeller = 2;

    Task<SellerPickupListResponse> ListAsync(Guid sellerUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListNicknamesAsync(Guid sellerUserId, CancellationToken cancellationToken = default);

    Task<SellerPickupLocationDto> CreateAsync(
        SellerUser seller,
        CreateSellerPickupRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class SellerPickupService(
    BaglyDbContext db,
    IHttpClientFactory httpClientFactory,
    ShiprocketTokenStore tokenStore,
    IOptions<ShiprocketOptions> options,
    IShiprocketApiLogService apiLogs,
    ILogger<SellerPickupService> logger) : ISellerPickupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Regex PhoneDigits = new(@"^\d{10}$", RegexOptions.Compiled);
    private static readonly Regex PinDigits = new(@"^\d{6}$", RegexOptions.Compiled);

    /// <summary>Shiprocket requires house/flat/road (etc.) number hints in address line 1.</summary>
    private static readonly Regex AddressHouseHint = new(
        @"\b(house|flat|road|plot|shop|apt|apartment|building|wing|floor|door|no\.?|number|#)\b|\d",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ShiprocketOptions _options = options.Value;

    public async Task<SellerPickupListResponse> ListAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.SellerPickupLocations.AsNoTracking()
            .Where(x => x.SellerUserId == sellerUserId && x.ShiprocketSuccess)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var items = rows.Select(ToDto).ToList();
        return new SellerPickupListResponse(items, items.Count, ISellerPickupService.MaxPickupsPerSeller);
    }

    public async Task<IReadOnlyList<string>> ListNicknamesAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken = default)
    {
        return await db.SellerPickupLocations.AsNoTracking()
            .Where(x => x.SellerUserId == sellerUserId && x.ShiprocketSuccess)
            .OrderBy(x => x.PickupLocation)
            .Select(x => x.PickupLocation)
            .ToListAsync(cancellationToken);
    }

    public async Task<SellerPickupLocationDto> CreateAsync(
        SellerUser seller,
        CreateSellerPickupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || !_options.IsConfigured)
        {
            throw new InvalidOperationException(
                "Shiprocket is not configured. Pickup addresses cannot be created right now.");
        }

        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            throw new ArgumentException(validationError);
        }

        var nickname = request.PickupLocation.Trim();
        var phone = DigitsOnly(request.Phone);
        var pinCode = DigitsOnly(request.PinCode);
        var country = string.IsNullOrWhiteSpace(request.Country) ? "India" : request.Country.Trim();
        var address = ComposeShiprocketAddress(request.HouseNo, request.Address);
        var address2 = string.IsNullOrWhiteSpace(request.Address2) ? string.Empty : request.Address2.Trim();
        var lat = string.IsNullOrWhiteSpace(request.Lat) ? null : request.Lat.Trim();
        var lng = string.IsNullOrWhiteSpace(request.Long) ? null : request.Long.Trim();
        var gstin = string.IsNullOrWhiteSpace(request.Gstin) ? null : request.Gstin.Trim().ToUpperInvariant();

        var existingCount = await db.SellerPickupLocations
            .CountAsync(x => x.SellerUserId == seller.Id && x.ShiprocketSuccess, cancellationToken);

        if (existingCount >= ISellerPickupService.MaxPickupsPerSeller)
        {
            throw new InvalidOperationException(
                $"You already have {ISellerPickupService.MaxPickupsPerSeller} pickup locations (maximum allowed).");
        }

        var nicknameTaken = await db.SellerPickupLocations.AnyAsync(
            x => x.SellerUserId == seller.Id
                 && x.ShiprocketSuccess
                 && x.PickupLocation == nickname,
            cancellationToken);

        if (nicknameTaken)
        {
            throw new ArgumentException($"Pickup nickname '{nickname}' is already used on your account.");
        }

        var payload = new ShiprocketAddPickupPayload
        {
            PickupLocation = nickname,
            Name = request.Name.Trim(),
            Email = request.Email.Trim(),
            Phone = phone,
            Address = address,
            Address2 = address2,
            City = request.City.Trim(),
            State = request.State.Trim(),
            Country = country,
            PinCode = int.Parse(pinCode),
            Lat = lat,
            Long = lng,
            Gstin = gstin,
        };

        var addResult = await AddPickupWithAuthRetryAsync(payload, cancellationToken);

        var entity = new SellerPickupLocation
        {
            SellerUserId = seller.Id,
            PickupLocation = nickname,
            Name = payload.Name,
            Email = payload.Email,
            Phone = phone,
            Address = payload.Address,
            Address2 = string.IsNullOrWhiteSpace(address2) ? null : address2,
            City = payload.City,
            State = payload.State,
            Country = country,
            PinCode = pinCode,
            Lat = lat,
            Long = lng,
            Gstin = gstin,
            ShiprocketSuccess = true,
            ShiprocketPickupId = addResult.PickupId,
            CreatedAt = DateTime.UtcNow,
        };

        db.SellerPickupLocations.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seller {SellerEmail} created Shiprocket pickup '{Nickname}' (Bagly id {Id}, SR id {SrId}).",
            seller.Email,
            nickname,
            entity.Id,
            addResult.PickupId ?? "(none)");

        return ToDto(entity);
    }

    private async Task<AddPickupApiResult> AddPickupWithAuthRetryAsync(
        ShiprocketAddPickupPayload payload,
        CancellationToken cancellationToken)
    {
        try
        {
            return await AddPickupOnceAsync(payload, forceLogin: false, cancellationToken);
        }
        catch (ShiprocketAuthException)
        {
            tokenStore.Invalidate();
            return await AddPickupOnceAsync(payload, forceLogin: true, cancellationToken);
        }
    }

    private async Task<AddPickupApiResult> AddPickupOnceAsync(
        ShiprocketAddPickupPayload payload,
        bool forceLogin,
        CancellationToken cancellationToken)
    {
        var token = forceLogin
            ? await LoginAsync(cancellationToken)
            : tokenStore.GetValidToken() ?? await LoginAsync(cancellationToken);

        const string path = "v1/external/settings/company/addpickup";
        var requestJson = JsonSerializer.Serialize(payload, JsonOptions);

        var client = httpClientFactory.CreateClient("Shiprocket");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        await apiLogs.LogAsync(
            "AddPickup",
            "POST",
            path,
            requestJson: requestJson,
            responseStatus: (int)response.StatusCode,
            responseJson: raw,
            cancellationToken: cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            tokenStore.Invalidate();
            throw new ShiprocketAuthException($"Shiprocket addpickup returned 401. Body: {Truncate(raw)}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                BuildShiprocketError($"Shiprocket addpickup failed HTTP {(int)response.StatusCode}", raw));
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        var root = doc.RootElement;

        if (TryReadStatusCode(root, out var apiStatus) && apiStatus >= 400)
        {
            throw new InvalidOperationException(
                BuildShiprocketError($"Shiprocket rejected addpickup (status_code={apiStatus})", raw, root));
        }

        // Some Shiprocket responses use success: false with HTTP 200.
        if (root.TryGetProperty("success", out var successEl) &&
            successEl.ValueKind == JsonValueKind.False)
        {
            throw new InvalidOperationException(
                BuildShiprocketError("Shiprocket rejected addpickup", raw, root));
        }

        var pickupId = TryReadPickupId(root);
        return new AddPickupApiResult(pickupId);
    }

    private async Task<string> LoginAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("Shiprocket");
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/external/auth/login");
        var body = new { email = _options.Email.Trim(), password = _options.Password };
        request.Content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new ShiprocketAuthException(
                $"Shiprocket login failed HTTP {(int)response.StatusCode}. Body: {Truncate(raw)}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        if (!doc.RootElement.TryGetProperty("token", out var tokenEl) ||
            tokenEl.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(tokenEl.GetString()))
        {
            throw new ShiprocketAuthException($"Shiprocket login response missing token. Body: {Truncate(raw)}");
        }

        var token = tokenEl.GetString()!;
        tokenStore.SetToken(token);
        return token;
    }

    private static string? ValidateRequest(CreateSellerPickupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PickupLocation))
            return "Pickup nickname is required.";
        if (request.PickupLocation.Trim().Length > 36)
            return "Pickup nickname must be at most 36 characters.";
        if (string.IsNullOrWhiteSpace(request.Name))
            return "Contact name is required.";
        if (string.IsNullOrWhiteSpace(request.Email))
            return "Email is required.";
        if (string.IsNullOrWhiteSpace(request.Phone))
            return "Phone is required.";
        if (!PhoneDigits.IsMatch(DigitsOnly(request.Phone)))
            return "Phone must be a 10-digit Indian mobile number.";
        if (string.IsNullOrWhiteSpace(request.Address))
            return "Street address is required.";

        string composed;
        try
        {
            composed = ComposeShiprocketAddress(request.HouseNo, request.Address);
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }

        if (!AddressHouseHint.IsMatch(composed))
            return "Address must include a house/flat/road/plot/shop number (Shiprocket requirement).";
        if (!string.IsNullOrWhiteSpace(request.Address2) && request.Address2.Trim().Length > 80)
            return "Address line 2 (landmark/area) must be at most 80 characters.";
        if (string.IsNullOrWhiteSpace(request.City))
            return "City is required.";
        if (string.IsNullOrWhiteSpace(request.State))
            return "State is required.";
        if (string.IsNullOrWhiteSpace(request.PinCode) || !PinDigits.IsMatch(DigitsOnly(request.PinCode)))
            return "Pin code must be a 6-digit number.";
        return null;
    }

    /// <summary>
    /// Builds Shiprocket address line 1 as <c>House No. {houseNo}, {street}</c> when needed.
    /// If street already contains a house/flat/road hint and no separate houseNo is needed, it is used as-is.
    /// </summary>
    internal static string ComposeShiprocketAddress(string? houseNo, string streetAddress)
    {
        var street = (streetAddress ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(street))
            throw new ArgumentException("Street address is required.");

        var house = (houseNo ?? string.Empty).Trim();
        string composed;

        if (!string.IsNullOrWhiteSpace(house))
        {
            if (street.Contains(house, StringComparison.OrdinalIgnoreCase) && AddressHouseHint.IsMatch(street))
                composed = street;
            else
                composed = $"House No. {house}, {street}";
        }
        else if (AddressHouseHint.IsMatch(street))
        {
            composed = street;
        }
        else
        {
            throw new ArgumentException(
                "House / Flat / Road no. is required. Shiprocket needs it in address line 1.");
        }

        if (composed.Length > 80)
        {
            throw new ArgumentException(
                "House/flat/road no. + street address must be at most 80 characters total.");
        }

        return composed;
    }

    private static string DigitsOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return new string(value.Where(char.IsDigit).ToArray());
    }

    private static SellerPickupLocationDto ToDto(SellerPickupLocation x) =>
        new(
            x.Id,
            x.PickupLocation,
            x.Name,
            x.Email,
            x.Phone,
            x.Address,
            x.Address2,
            x.City,
            x.State,
            x.Country,
            x.PinCode,
            x.Lat,
            x.Long,
            x.Gstin,
            x.CreatedAt);

    private static string BuildShiprocketError(string prefix, string raw, JsonElement? root = null)
    {
        var message = root is JsonElement el ? TryReadMessage(el) : null;
        if (message is null && !string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                message = TryReadMessage(doc.RootElement);
            }
            catch (JsonException)
            {
                // fall through
            }
        }

        message ??= Truncate(raw, 400);
        return string.IsNullOrWhiteSpace(message) ? prefix : $"{prefix}: {message}";
    }

    private static bool TryReadStatusCode(JsonElement root, out int statusCode)
    {
        statusCode = 0;
        if (!root.TryGetProperty("status_code", out var el))
            return false;

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out statusCode))
            return true;

        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out statusCode))
            return true;

        return false;
    }

    private static string? TryReadMessage(JsonElement root)
    {
        foreach (var key in new[] { "message", "msg", "error", "errors" })
        {
            if (!root.TryGetProperty(key, out var el)) continue;
            return el.ValueKind switch
            {
                JsonValueKind.String => FormatFieldErrorsMessage(el.GetString()),
                JsonValueKind.Object => FormatFieldErrorsObject(el),
                JsonValueKind.Array => Truncate(el.GetRawText(), 300),
                _ => null,
            };
        }

        return null;
    }

    /// <summary>
    /// Shiprocket often returns <c>message</c> as a JSON string of field errors, e.g.
    /// <c>{"address":["Address line 1 should have House no / Flat no / Road no."]}</c>.
    /// </summary>
    private static string? FormatFieldErrorsMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return message;

        var trimmed = message.Trim();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    return FormatFieldErrorsObject(doc.RootElement) ?? trimmed;
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    return Truncate(doc.RootElement.GetRawText(), 300);
            }
            catch (JsonException)
            {
                return trimmed;
            }
        }

        return trimmed;
    }

    private static string? FormatFieldErrorsObject(JsonElement obj)
    {
        var parts = new List<string>();
        foreach (var prop in obj.EnumerateObject())
        {
            var field = prop.Name;
            if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                var msgs = prop.Value.EnumerateArray()
                    .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.GetRawText())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();
                if (msgs.Count > 0)
                    parts.Add($"{field}: {string.Join("; ", msgs!)}");
            }
            else if (prop.Value.ValueKind == JsonValueKind.String)
            {
                var s = prop.Value.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    parts.Add($"{field}: {s}");
            }
            else
            {
                parts.Add($"{field}: {prop.Value.GetRawText()}");
            }
        }

        return parts.Count == 0 ? null : Truncate(string.Join(" | ", parts), 400);
    }

    private static string? TryReadPickupId(JsonElement root)
    {
        foreach (var key in new[] { "pickup_id", "id", "address_id" })
        {
            if (!root.TryGetProperty(key, out var el)) continue;
            var value = el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Number => el.GetRawText(),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            return TryReadPickupId(data);
        }

        return null;
    }

    private static string Truncate(string value, int max = 500) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];

    private sealed record AddPickupApiResult(string? PickupId);
}
