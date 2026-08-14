using System.Text.Json;
using Bagly.Api.Data;
using Bagly.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Shipping;

/// <summary>
/// Inbound Shiprocket tracking webhooks.
/// Configure in Shiprocket panel → API → Webhooks to POST here.
/// Maps courier statuses into <see cref="OrderShiprocketShipment.TrackingStatus"/>
/// and appends <see cref="OrderShipmentTracking"/> history rows.
/// </summary>
[ApiController]
[Route("api/webhooks/shiprocket")]
[AllowAnonymous]
public class ShiprocketWebhookController(
    BaglyDbContext db,
    IAdminShippingService shipping,
    ILogger<ShiprocketWebhookController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        using var doc = await JsonDocument.ParseAsync(Request.Body, cancellationToken: cancellationToken);
        var root = doc.RootElement;
        var raw = root.GetRawText();

        var awb = ReadString(root, "awb", "awb_code", "awbno")
                  ?? ReadNestedString(root, "data", "awb", "awb_code");
        var srShipmentId = ReadString(root, "shipment_id", "sr_shipment_id", "shiprocket_shipment_id")
                           ?? ReadNestedString(root, "data", "shipment_id");
        var currentStatus = ReadString(root, "current_status", "shipment_status", "status", "current_status_code")
                            ?? ReadNestedString(root, "data", "current_status", "shipment_status", "status");

        var mapped = ShipmentTrackingStatus.MapFromShiprocket(currentStatus);
        if (mapped is null)
        {
            logger.LogInformation(
                "Shiprocket webhook ignored (unmapped status={Status}, awb={Awb}, srShipment={Sr}).",
                currentStatus,
                awb,
                srShipmentId);
            // Acknowledge so Shiprocket does not retry forever for unknown labels.
            return Ok(new { received = true, updated = false, reason = "unmapped_status" });
        }

        var shipment = await FindShipmentAsync(awb, srShipmentId, cancellationToken);
        if (shipment is null)
        {
            logger.LogWarning(
                "Shiprocket webhook: no Bagly shipment for awb={Awb}, srShipment={Sr}, status={Status}.",
                awb,
                srShipmentId,
                mapped);
            return Ok(new { received = true, updated = false, reason = "shipment_not_found" });
        }

        var updated = await shipping.ApplyTrackingStatusAsync(
            shipment.Id,
            mapped,
            ShipmentTrackingStatus.SourceShiprocketWebhook,
            raw,
            cancellationToken);

        return Ok(new
        {
            received = true,
            updated,
            shipmentId = shipment.Id,
            trackingStatus = mapped,
        });
    }

    private async Task<OrderShiprocketShipment?> FindShipmentAsync(
        string? awb,
        string? srShipmentId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(awb))
        {
            var byAwb = await db.OrderShiprocketShipments
                .FirstOrDefaultAsync(s => s.AwbCode == awb.Trim(), cancellationToken);
            if (byAwb is not null) return byAwb;
        }

        if (!string.IsNullOrWhiteSpace(srShipmentId))
        {
            var id = srShipmentId.Trim();
            return await db.OrderShiprocketShipments
                .FirstOrDefaultAsync(s => s.ShiprocketShipmentId == id, cancellationToken);
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
}
