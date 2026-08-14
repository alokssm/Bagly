using System.Text;
using System.Text.Json;
using Bagly.Api.Data;
using Bagly.Api.Models;
using Bagly.Api.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Shipping;

/// <summary>
/// Inbound Shiprocket tracking webhooks.
/// Prefer <c>POST /api/webhooks/shipping-status</c> in the Shiprocket panel (path avoids blocked words).
/// Legacy <c>POST /api/webhooks/shiprocket</c> remains supported.
/// GET/HEAD on either path return a quick health payload for panel probes.
/// Every request/response is persisted to <see cref="ShiprocketWebhookLog"/>.
/// </summary>
[ApiController]
[Route("api/webhooks/shipping-status")]
[Route("api/webhooks/shiprocket")]
[AllowAnonymous]
public class ShiprocketWebhookController(
    BaglyDbContext db,
    IAdminShippingService shipping,
    IShiprocketWebhookLogService webhookLogs,
    IOptions<ShiprocketOptions> options,
    ILogger<ShiprocketWebhookController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ShiprocketOptions _options = options.Value;

    /// <summary>Panel / uptime probe — must stay fast and unauthenticated.</summary>
    [HttpGet]
    [HttpHead]
    public IActionResult Health()
    {
        return Ok(new { ok = true, service = "shipping-webhook" });
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var path = Request.Path.HasValue ? Request.Path.Value! : "/api/webhooks/shipping-status";
        var log = new ShiprocketWebhookLog
        {
            ReceivedAtUtc = DateTime.UtcNow,
            HttpMethod = Request.Method,
            Path = path,
            HeadersJson = ShiprocketWebhookLogService.BuildHeadersJson(Request.Headers),
        };

        string? requestBody = null;
        try
        {
            requestBody = await ReadBodyAsync(cancellationToken);
            log.RequestBody = requestBody;

            var isEmptyProbe = string.IsNullOrWhiteSpace(requestBody) ||
                               string.Equals(requestBody.Trim(), "{}", StringComparison.Ordinal);

            if (!IsWebhookAuthorized())
            {
                // Shiprocket panel often probes without the shared secret; empty bodies must still 200.
                if (isEmptyProbe)
                {
                    logger.LogInformation(
                        "Shipping webhook probe accepted without secret (empty body) on {Path}.",
                        path);
                    return await FinishAsync(
                        log,
                        StatusCodes.Status200OK,
                        new { ok = true, received = true, probe = true, service = "shipping-webhook" },
                        processedOk: true,
                        errorMessage: null,
                        cancellationToken);
                }

                logger.LogWarning("Shipping webhook rejected: missing or invalid webhook secret.");
                return await FinishAsync(
                    log,
                    StatusCodes.Status401Unauthorized,
                    new { message = "Invalid webhook secret." },
                    processedOk: false,
                    errorMessage: "Invalid webhook secret.",
                    cancellationToken);
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(requestBody) ? "{}" : requestBody);
            }
            catch (JsonException ex)
            {
                // Keep panel/test clients happy: log and acknowledge instead of 400/500.
                logger.LogWarning(ex, "Shipping webhook: invalid JSON body; acknowledging as probe.");
                return await FinishAsync(
                    log,
                    StatusCodes.Status200OK,
                    new { ok = true, received = true, updated = false, reason = "invalid_json" },
                    processedOk: true,
                    errorMessage: Truncate(ex.Message, 500),
                    cancellationToken);
            }

            using (doc)
            {
                var root = doc.RootElement;
                var raw = Truncate(root.GetRawText(), 3900);

                var awb = ReadString(root, "awb", "awb_code", "awbno")
                          ?? ReadNestedString(root, "data", "awb", "awb_code");
                var srShipmentId = ReadString(root, "shipment_id", "sr_shipment_id", "shiprocket_shipment_id")
                                   ?? ReadNestedString(root, "data", "shipment_id", "sr_shipment_id");
                var srOrderId = ReadString(root, "sr_order_id", "shiprocket_order_id")
                                ?? ReadNestedString(root, "data", "sr_order_id");

                if (isEmptyProbe ||
                    (string.IsNullOrWhiteSpace(awb) &&
                     string.IsNullOrWhiteSpace(srShipmentId) &&
                     string.IsNullOrWhiteSpace(srOrderId)))
                {
                    return await FinishAsync(
                        log,
                        StatusCodes.Status200OK,
                        new { ok = true, received = true, updated = false, reason = "empty_or_test_payload" },
                        processedOk: true,
                        errorMessage: null,
                        cancellationToken);
                }

                var shipment = await FindShipmentAsync(awb, srShipmentId, srOrderId, cancellationToken);
                if (shipment is null)
                {
                    logger.LogWarning(
                        "Shipping webhook: no Bagly shipment for awb={Awb}, srShipment={Sr}, srOrder={SrOrder}.",
                        awb,
                        srShipmentId,
                        srOrderId);
                    return await FinishAsync(
                        log,
                        StatusCodes.Status200OK,
                        new { received = true, updated = false, reason = "shipment_not_found" },
                        processedOk: true,
                        errorMessage: null,
                        cancellationToken);
                }

                log.MatchedOrderId = shipment.OrderId;
                log.MatchedShipmentId = shipment.Id;

                // Collect forward statuses from top-level fields + scans (chronological).
                var pipeline = CollectForwardStatuses(root, shipment.TrackingStatus);
                if (pipeline.Count == 0)
                {
                    var currentStatus = ReadString(root, "current_status", "shipment_status", "status", "current_status_code")
                                        ?? ReadNestedString(root, "data", "current_status", "shipment_status", "status");
                    logger.LogInformation(
                        "Shipping webhook ignored (unmapped/non-forward status={Status}, awb={Awb}, srShipment={Sr}, current={Current}).",
                        currentStatus,
                        awb,
                        srShipmentId,
                        shipment.TrackingStatus);
                    log.MappedStatus = shipment.TrackingStatus;
                    return await FinishAsync(
                        log,
                        StatusCodes.Status200OK,
                        new { received = true, updated = false, reason = "unmapped_or_non_forward_status" },
                        processedOk: true,
                        errorMessage: null,
                        cancellationToken);
                }

                var anyUpdated = false;
                string? lastApplied = shipment.TrackingStatus;
                foreach (var status in pipeline)
                {
                    var updated = await shipping.ApplyTrackingStatusAsync(
                        shipment.Id,
                        status,
                        ShipmentTrackingStatus.SourceShiprocketWebhook,
                        raw,
                        cancellationToken);
                    if (updated)
                    {
                        anyUpdated = true;
                        lastApplied = status;
                    }
                }

                log.MappedStatus = lastApplied ?? pipeline[^1];
                return await FinishAsync(
                    log,
                    StatusCodes.Status200OK,
                    new
                    {
                        received = true,
                        updated = anyUpdated,
                        shipmentId = shipment.Id,
                        trackingStatus = lastApplied,
                        applied = pipeline,
                    },
                    processedOk: true,
                    errorMessage: null,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Never fail panel save / delivery retries with 500 — log and acknowledge.
            logger.LogError(ex, "Shipping webhook processing failed; acknowledging with 200.");
            log.RequestBody ??= requestBody;
            return await FinishAsync(
                log,
                StatusCodes.Status200OK,
                new { ok = true, received = true, updated = false, reason = "processing_error" },
                processedOk: false,
                errorMessage: Truncate(ex.Message, 500),
                cancellationToken);
        }
    }

    private async Task<IActionResult> FinishAsync(
        ShiprocketWebhookLog log,
        int statusCode,
        object responseBody,
        bool processedOk,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        log.ResponseStatusCode = statusCode;
        log.ProcessedOk = processedOk;
        log.ErrorMessage = errorMessage;
        try
        {
            log.ResponseBody = JsonSerializer.Serialize(responseBody, JsonOptions);
        }
        catch
        {
            log.ResponseBody = null;
        }

        await webhookLogs.PersistAsync(log, cancellationToken);

        return StatusCode(statusCode, responseBody);
    }

    private async Task<string> ReadBodyAsync(CancellationToken cancellationToken)
    {
        Request.EnableBuffering();
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;
        return ShiprocketWebhookLogService.TruncateBody(body);
    }

    private bool IsWebhookAuthorized()
    {
        if (!_options.HasWebhookSecret)
        {
            return true;
        }

        var expected = _options.WebhookSecret.Trim();
        if (HeaderEquals("x-api-key", expected) ||
            HeaderEquals("X-Shiprocket-Webhook-Secret", expected) ||
            HeaderEquals("X-Webhook-Secret", expected))
        {
            return true;
        }

        var auth = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(auth))
        {
            const string bearer = "Bearer ";
            if (auth.StartsWith(bearer, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(auth[bearer.Length..].Trim(), expected, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(auth.Trim(), expected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool HeaderEquals(string name, string expected)
    {
        if (!Request.Headers.TryGetValue(name, out var values)) return false;
        var actual = values.ToString()?.Trim();
        return !string.IsNullOrEmpty(actual) &&
               string.Equals(actual, expected, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds an ascending list of mapped pipeline statuses from webhook payload.
    /// Uses <c>current_status</c> / <c>shipment_status</c> / status ids, then walks <c>scans</c>.
    /// Only includes statuses strictly forward of <paramref name="currentTracking"/>.
    /// </summary>
    internal static List<string> CollectForwardStatuses(JsonElement root, string? currentTracking)
    {
        var candidates = new List<(int Rank, string Status, int Order)>();
        var order = 0;

        void Consider(string? mapped)
        {
            if (string.IsNullOrWhiteSpace(mapped)) return;
            if (!ShipmentTrackingStatus.IsForwardOf(mapped, currentTracking)) return;
            var rank = ShipmentTrackingStatus.Rank(mapped);
            if (rank <= 0) return;
            candidates.Add((rank, mapped, order++));
        }

        Consider(ShipmentTrackingStatus.MapFromShiprocket(
            ReadString(root, "current_status", "shipment_status", "status", "current_status_code")
            ?? ReadNestedString(root, "data", "current_status", "shipment_status", "status")));

        Consider(ShipmentTrackingStatus.MapFromShiprocketStatusId(
            ReadInt(root, "current_status_id", "shipment_status_id")
            ?? ReadNestedInt(root, "data", "current_status_id", "shipment_status_id")));

        if (TryGetScans(root, out var scans))
        {
            foreach (var scan in scans)
            {
                Consider(ShipmentTrackingStatus.MapFromShiprocket(
                    ReadString(scan, "sr-status-label", "sr_status_label", "status_label", "activity", "status")));
                Consider(ShipmentTrackingStatus.MapFromShiprocketStatusId(
                    ReadInt(scan, "sr-status", "sr_status", "status_id")));
            }
        }

        if (candidates.Count == 0) return [];

        // Distinct ascending ranks so history fills PICKED_UP → IN_TRANSIT → … without duplicates.
        return candidates
            .GroupBy(c => c.Rank)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(x => x.Order).First().Status)
            .ToList();
    }

    private static bool TryGetScans(JsonElement root, out IEnumerable<JsonElement> scans)
    {
        if (root.TryGetProperty("scans", out var el) && el.ValueKind == JsonValueKind.Array)
        {
            scans = el.EnumerateArray();
            return true;
        }

        if (root.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("scans", out var nested) &&
            nested.ValueKind == JsonValueKind.Array)
        {
            scans = nested.EnumerateArray();
            return true;
        }

        if (root.TryGetProperty("shipment_track_activities", out var acts) &&
            acts.ValueKind == JsonValueKind.Array)
        {
            scans = acts.EnumerateArray();
            return true;
        }

        scans = Array.Empty<JsonElement>();
        return false;
    }

    private async Task<OrderShiprocketShipment?> FindShipmentAsync(
        string? awb,
        string? srShipmentId,
        string? srOrderId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(awb))
        {
            var code = awb.Trim();
            var byAwb = await db.OrderShiprocketShipments
                .FirstOrDefaultAsync(s => s.AwbCode == code, cancellationToken);
            if (byAwb is not null) return byAwb;
        }

        if (!string.IsNullOrWhiteSpace(srShipmentId))
        {
            var id = srShipmentId.Trim();
            var byShipment = await db.OrderShiprocketShipments
                .FirstOrDefaultAsync(s => s.ShiprocketShipmentId == id, cancellationToken);
            if (byShipment is not null) return byShipment;
        }

        if (!string.IsNullOrWhiteSpace(srOrderId))
        {
            var id = srOrderId.Trim();
            return await db.OrderShiprocketShipments
                .FirstOrDefaultAsync(s => s.ShiprocketOrderId == id, cancellationToken);
        }

        return null;
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var el)) continue;
            var value = el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Number => el.GetRawText(),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }

        return null;
    }

    private static string? ReadNestedString(JsonElement root, string objectName, params string[] names)
    {
        if (!root.TryGetProperty(objectName, out var nested) || nested.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadString(nested, names);
    }

    private static int? ReadInt(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var el)) continue;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) return n;
            if (el.ValueKind == JsonValueKind.String &&
                int.TryParse(el.GetString(), out var parsed) &&
                parsed > 0)
            {
                return parsed;
            }
        }

        return null;
    }

    private static int? ReadNestedInt(JsonElement root, string objectName, params string[] names)
    {
        if (!root.TryGetProperty(objectName, out var nested) || nested.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadInt(nested, names);
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max) return value ?? string.Empty;
        return value[..max];
    }
}
