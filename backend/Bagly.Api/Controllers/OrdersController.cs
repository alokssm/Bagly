using System.Security.Claims;
using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Models;
using Bagly.Api.Options;
using Bagly.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController(
    BaglyDbContext db,
    IOrderConfirmationEmailDispatcher emailDispatcher,
    IShiprocketOrderDispatcher shiprocketDispatcher,
    IShiprocketService shiprocketService,
    IOptions<ShiprocketOptions> shiprocketOptions) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<OrderDto>> CreateOrder(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName) ||
            string.IsNullOrWhiteSpace(request.Address))
        {
            return BadRequest(new { message = "Shipping details are incomplete." });
        }

        var isIndia = string.Equals(request.Country?.Trim(), "India", StringComparison.OrdinalIgnoreCase);
        var isCod = IsCodPaymentMethod(request.PaymentMethod);

        if (isIndia && !isCod)
        {
            return BadRequest(new
            {
                message = "Indian customers must complete payment with Razorpay, or select Cash on delivery. Use /api/payments/razorpay/initiate for online payment.",
            });
        }

        if (isIndia && string.IsNullOrWhiteSpace(ShiprocketService.NormalizePhone(request.Phone)))
        {
            return BadRequest(new
            {
                message = "A valid 10-digit Indian mobile number is required for India orders (needed for shipment creation).",
            });
        }

        if (isIndia && ShiprocketService.NormalizePincode(request.Zip) is null)
        {
            return BadRequest(new
            {
                message = "A valid 6-digit Indian PIN code is required for India orders (needed for shipment creation).",
            });
        }

        var lineItems = new List<(string ProductId, string Name, string Color, decimal Price, int Quantity)>();

        if (request.CartId is Guid cartId)
        {
            var cart = await db.Carts.Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.Id == cartId, cancellationToken);

            if (cart is null || cart.Items.Count == 0)
            {
                return BadRequest(new { message = "Cart is empty or was not found." });
            }

            foreach (var item in cart.Items)
            {
                lineItems.Add((item.ProductId, item.ProductName, item.Color, item.UnitPrice, item.Quantity));
            }
        }
        else if (request.Items is { Count: > 0 })
        {
            foreach (var item in request.Items)
            {
                if (item.Quantity < 1)
                {
                    return BadRequest(new { message = "Each item quantity must be at least 1." });
                }

                var product = await db.Products.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == item.ProductId && p.IsActive, cancellationToken);

                if (product is null)
                {
                    return BadRequest(new { message = $"Product '{item.ProductId}' was not found." });
                }

                lineItems.Add((product.Id, product.Name, item.Color, product.Price, item.Quantity));
            }
        }
        else
        {
            return BadRequest(new { message = "Provide a cartId or items to place an order." });
        }

        var subtotal = lineItems.Sum(i => i.Price * i.Quantity);
        var shipping = Pricing.CalculateShipping(subtotal);

        var order = new Order
        {
            OrderNumber = $"BG-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}",
            CustomerUserId = GetOptionalCustomerId(),
            Email = request.Email.Trim(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Address = request.Address.Trim(),
            City = request.City.Trim(),
            State = request.State.Trim(),
            Zip = isIndia
                ? ShiprocketService.NormalizePincode(request.Zip)!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : request.Zip.Trim(),
            Country = string.IsNullOrWhiteSpace(request.Country) ? "India" : request.Country.Trim(),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Subtotal = subtotal,
            Shipping = shipping,
            Total = subtotal + shipping,
            Status = "Confirmed",
            PaymentStatus = isCod ? "Pending" : "NotRequired",
            PaymentProvider = isCod ? "COD" : null,
            Currency = "INR",
            Items = lineItems.Select(i => new OrderItem
            {
                ProductId = i.ProductId,
                ProductName = i.Name,
                Color = i.Color,
                UnitPrice = i.Price,
                Quantity = i.Quantity,
            }).ToList(),
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);

        var stockResult = await StockDecrementer.TryDecrementAsync(db, order.Items, cancellationToken);
        if (!stockResult.Success)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(new
            {
                message = $"'{stockResult.InsufficientProductName}' no longer has enough stock for the requested quantity. Please update your cart and try again.",
            });
        }

        if (request.CartId is Guid cartIdToClear)
        {
            var cartItemsToRemove = await db.CartItems
                .Where(i => i.CartId == cartIdToClear)
                .ToListAsync(cancellationToken);
            if (cartItemsToRemove.Count > 0)
            {
                db.CartItems.RemoveRange(cartItemsToRemove);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);

        var createdOrder = MapOrder(order);
        emailDispatcher.Enqueue(order.Id, "order-create");
        await DispatchShiprocketAsync(order.Id, cancellationToken);
        return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, createdOrder);
    }

    private async Task DispatchShiprocketAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (shiprocketOptions.Value.SyncCreateOnCheckout)
        {
            await shiprocketService.TryCreateAdhocOrderForConfirmedOrderAsync(orderId, cancellationToken);
            return;
        }

        shiprocketDispatcher.Enqueue(orderId);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> GetOrder(Guid id, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.ShiprocketShipments)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        return order is null
            ? NotFound(new { message = "Order not found." })
            : Ok(MapOrder(order));
    }

    /// <summary>Admin-only dump of recent orders across all customers. Never expose this publicly.</summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IEnumerable<OrderDto>>> GetOrders(CancellationToken cancellationToken)
    {
        var orders = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.ShiprocketShipments)
            .OrderByDescending(o => o.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        return Ok(orders.Select(MapOrder));
    }

    public static OrderDto MapOrder(Order order) =>
        new(
            order.Id,
            order.OrderNumber,
            order.Email,
            order.FirstName,
            order.LastName,
            order.Status,
            order.PaymentStatus,
            order.PaymentProvider,
            order.Currency,
            order.AmountInr,
            order.RazorpayOrderId,
            order.RazorpayPaymentId,
            order.PaidAtUtc,
            order.Subtotal,
            order.Shipping,
            order.Total,
            order.CreatedAt,
            order.Items.Select(i => new OrderItemDto(
                i.ProductId,
                i.ProductName,
                i.Color,
                i.UnitPrice,
                i.Quantity
            )).ToList(),
            order.Phone,
            order.ShiprocketOrderId,
            order.ShiprocketShipmentId,
            order.ShiprocketStatus,
            order.ShiprocketLastError,
            order.ShiprocketShipments?
                .OrderBy(s => s.PickupLocation, StringComparer.Ordinal)
                .Select(s => new OrderShiprocketShipmentDto(
                    s.Id,
                    s.PickupLocation,
                    s.ShiprocketOrderId,
                    s.ShiprocketShipmentId,
                    s.Status,
                    s.LastError,
                    s.CreatedAt,
                    s.UpdatedAt,
                    s.ShippingStatus,
                    s.AwbCode,
                    s.CourierId,
                    s.CourierName,
                    s.ActualShippingCharge,
                    s.ReadyToShipAt,
                    s.AwbAssignedAt))
                .ToList()
        );

    /// <summary>
    /// This endpoint has no [Authorize] attribute (guest checkout is allowed), but if the caller
    /// sent a valid customer Bearer token, JWT auth middleware still populates HttpContext.User —
    /// so a logged-in customer's order gets linked to their account without requiring login.
    /// </summary>
    private Guid? GetOptionalCustomerId()
    {
        if (!User.IsInRole("Customer"))
        {
            return null;
        }

        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    internal static bool IsCodPaymentMethod(string? method) =>
        !string.IsNullOrWhiteSpace(method) &&
        (string.Equals(method, "COD", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(method, "CashOnDelivery", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(method, "Cash on delivery", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(method, "PayOnDelivery", StringComparison.OrdinalIgnoreCase));
}
