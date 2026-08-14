using Bagly.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Shipping;

/// <summary>Admin shipping workflow: serviceability, AWB, label, pickup, and manifest.</summary>
[ApiController]
[Route("api/admin/shipping")]
[Authorize(Roles = "Admin")]
public class AdminShippingController(IAdminShippingService shipping, BaglyDbContext db) : ControllerBase
{
    /// <summary>
    /// Orders with Shiprocket shipments.
    /// Tabs: new | ready | assign-awb (alias: assign) | label (alias: awb) | pickup (alias: labeled) | manifest | in-progress.
    /// </summary>
    [HttpGet("orders")]
    public async Task<ActionResult<AdminShippingOrdersResult>> ListOrders(
        [FromQuery] string? tab = "new",
        CancellationToken cancellationToken = default)
    {
        var result = await shipping.ListOrdersAsync(tab, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Mark a shipment Ready to Ship and return Shiprocket serviceability couriers.
    /// Works per pickup group (home/work) via OrderShiprocketShipments.Id.
    /// </summary>
    [HttpPost("shipments/{shipmentId:guid}/ready-to-ship")]
    public async Task<ActionResult<ReadyToShipResponse>> ReadyToShip(
        Guid shipmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await shipping.ReadyToShipAsync(shipmentId, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Convenience: Ready to Ship for a specific shipment on an order (same as shipments/{id}/ready-to-ship).
    /// Body optional: { "shipmentId": "..." } — required when the order has multiple pickups.
    /// </summary>
    [HttpPost("orders/{orderId:guid}/ready-to-ship")]
    public async Task<ActionResult<ReadyToShipResponse>> ReadyToShipForOrder(
        Guid orderId,
        [FromBody] ReadyToShipOrderRequest? body,
        CancellationToken cancellationToken)
    {
        Guid shipmentId;
        if (body?.ShipmentId is Guid explicitId)
        {
            shipmentId = explicitId;
        }
        else
        {
            var shipments = await db.OrderShiprocketShipments.AsNoTracking()
                .Where(s => s.OrderId == orderId &&
                            s.ShiprocketShipmentId != null &&
                            s.ShiprocketShipmentId != "")
                .Select(s => new { s.Id, s.PickupLocation, s.ShiprocketShipmentId })
                .ToListAsync(cancellationToken);

            if (shipments.Count == 0)
            {
                return BadRequest(new { message = "No Shiprocket shipments found for this order." });
            }

            if (shipments.Count > 1)
            {
                return BadRequest(new
                {
                    message = "Order has multiple pickups. Pass shipmentId in the body.",
                    shipments,
                });
            }

            shipmentId = shipments[0].Id;
        }

        try
        {
            var result = await shipping.ReadyToShipAsync(shipmentId, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Assign AWB via Shiprocket and persist AWB + actual shipping charge.</summary>
    [HttpPost("shipments/{shipmentId:guid}/assign-awb")]
    public async Task<ActionResult<AssignAwbResponse>> AssignAwb(
        Guid shipmentId,
        [FromBody] AssignAwbRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.CourierId <= 0)
        {
            return BadRequest(new { message = "courierId is required." });
        }

        try
        {
            var result = await shipping.AssignAwbAsync(
                shipmentId,
                request.CourierId,
                request.Rate,
                cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Generate shipping label via Shiprocket (<c>POST v1/external/courier/generate/label</c>)
    /// and store the label URL on the shipment.
    /// </summary>
    [HttpPost("shipments/{shipmentId:guid}/generate-label")]
    public async Task<ActionResult<GenerateLabelResponse>> GenerateLabel(
        Guid shipmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await shipping.GenerateLabelAsync(shipmentId, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Request courier pickup via Shiprocket (<c>POST v1/external/courier/generate/pickup</c>)
    /// and set tracking status to PICKUP_REQUESTED.
    /// </summary>
    [HttpPost("shipments/{shipmentId:guid}/request-pickup")]
    public async Task<ActionResult<RequestPickupResponse>> RequestPickup(
        Guid shipmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await shipping.RequestPickupAsync(shipmentId, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Generate manifest via Shiprocket (<c>POST v1/external/manifests/generate</c>)
    /// and store the manifest URL on the shipment.
    /// </summary>
    [HttpPost("shipments/{shipmentId:guid}/generate-manifest")]
    public async Task<ActionResult<GenerateManifestResponse>> GenerateManifest(
        Guid shipmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await shipping.GenerateManifestAsync(shipmentId, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Shiprocket outbound API request logs (request body / query redacted of secrets).
    /// Filter by orderId (Guid), orderNumber (e.g. BG-...), and/or shipmentId.
    /// </summary>
    [HttpGet("logs")]
    public async Task<ActionResult<IReadOnlyList<ShiprocketApiLogDto>>> ListLogs(
        [FromQuery] Guid? orderId = null,
        [FromQuery] string? orderNumber = null,
        [FromQuery] Guid? shipmentId = null,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var logs = await shipping.ListApiLogsAsync(orderId, shipmentId, orderNumber, take, cancellationToken);
        return Ok(logs);
    }

    /// <summary>
    /// Append-only tracking status audit log for a shipment
    /// (PICKUP_REQUESTED → … → DELIVERED), newest first.
    /// </summary>
    [HttpGet("shipments/{shipmentId:guid}/status-logs")]
    public async Task<ActionResult<IReadOnlyList<ShipmentStatusLogDto>>> ListStatusLogs(
        Guid shipmentId,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var exists = await db.OrderShiprocketShipments.AsNoTracking()
            .AnyAsync(s => s.Id == shipmentId, cancellationToken);
        if (!exists)
        {
            return NotFound(new { message = "Shipment not found." });
        }

        var logs = await shipping.ListStatusLogsAsync(shipmentId, take, cancellationToken);
        return Ok(logs);
    }
}

public record ReadyToShipOrderRequest(Guid? ShipmentId = null);
