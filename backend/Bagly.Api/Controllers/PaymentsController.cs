using System.Security.Claims;
using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Models;
using Bagly.Api.Options;
using Bagly.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bagly.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController(
    BaglyDbContext db,
    IRazorpayService razorpay,
    IPaymentLogService paymentLogs,
    IOrderConfirmationEmailDispatcher emailDispatcher,
    IShiprocketOrderDispatcher shiprocketDispatcher,
    IShiprocketService shiprocketService,
    IOptions<ShiprocketOptions> shiprocketOptions,
    ILogger<PaymentsController> logger) : ControllerBase
{
    [HttpGet("razorpay/config")]
    public ActionResult<RazorpayConfigDto> GetConfig() =>
        Ok(new RazorpayConfigDto(
            razorpay.IsConfigured,
            razorpay.IsConfigured ? razorpay.KeyId : null,
            razorpay.Currency));

    [HttpPost("razorpay/initiate")]
    public async Task<ActionResult<RazorpayInitiateResponse>> Initiate(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsIndia(request.Country))
        {
            return BadRequest(new { message = "Razorpay checkout is available for Indian customers only. Select India as country." });
        }

        if (!razorpay.IsConfigured)
        {
            return StatusCode(503, new
            {
                message = "Razorpay is not configured. Add Razorpay:KeyId and Razorpay:KeySecret in appsettings.",
            });
        }

        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName) ||
            string.IsNullOrWhiteSpace(request.Address))
        {
            return BadRequest(new { message = "Shipping details are incomplete." });
        }

        if (string.IsNullOrWhiteSpace(ShiprocketService.NormalizePhone(request.Phone)))
        {
            return BadRequest(new
            {
                message = "A valid 10-digit Indian mobile number is required for India orders (needed for shipment creation).",
            });
        }

        if (ShiprocketService.NormalizePincode(request.Zip) is null)
        {
            return BadRequest(new
            {
                message = "A valid 6-digit Indian PIN code is required for India orders (needed for shipment creation).",
            });
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        try
        {
            var (items, lineError) = await TryBuildLineItemsAsync(request, cancellationToken);
            if (items is null || lineError is not null)
            {
                return BadRequest(new { message = lineError ?? "Unable to build order items." });
            }

            var subtotal = items.Sum(i => i.Price * i.Quantity);
            var shipping = Pricing.CalculateShipping(subtotal);
            // Product prices are already INR, so the order total is the Razorpay amount directly — no conversion.
            var totalInr = subtotal + shipping;
            var amountInr = totalInr;
            var orderNumber = $"BG-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

            var order = new Order
            {
                OrderNumber = orderNumber,
                CustomerUserId = GetOptionalCustomerId(),
                Email = request.Email.Trim(),
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Address = request.Address.Trim(),
                City = request.City.Trim(),
                State = request.State.Trim(),
                Zip = ShiprocketService.NormalizePincode(request.Zip)!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Country = "India",
                Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
                Subtotal = subtotal,
                Shipping = shipping,
                Total = totalInr,
                Status = "AwaitingPayment",
                PaymentStatus = "Pending",
                PaymentProvider = "Razorpay",
                Currency = razorpay.Currency,
                AmountInr = amountInr,
                Items = items.Select(i => new OrderItem
                {
                    ProductId = i.ProductId,
                    ProductName = i.Name,
                    Color = i.Color,
                    UnitPrice = i.Price,
                    Quantity = i.Quantity,
                }).ToList(),
            };

            db.Orders.Add(order);
            await db.SaveChangesAsync(cancellationToken);

            await paymentLogs.LogAsync(
                eventType: "InitiateRequested",
                status: "Pending",
                message: $"Payment initiate requested for {orderNumber}",
                orderId: order.Id,
                orderNumber: order.OrderNumber,
                amount: amountInr,
                currency: razorpay.Currency,
                customerEmail: order.Email,
                request: new { order.OrderNumber, totalInr, amountInr, cartId = request.CartId },
                ipAddress: ip,
                cancellationToken: cancellationToken);

            RazorpayOrderResult rzOrder;
            try
            {
                rzOrder = await razorpay.CreateOrderAsync(amountInr, orderNumber, cancellationToken);
            }
            catch (Exception ex)
            {
                order.Status = "PaymentFailed";
                order.PaymentStatus = "Failed";
                await db.SaveChangesAsync(cancellationToken);

                await paymentLogs.LogAsync(
                    eventType: "RazorpayOrderCreateFailed",
                    status: "Failed",
                    message: ex.Message,
                    orderId: order.Id,
                    orderNumber: order.OrderNumber,
                    amount: amountInr,
                    currency: razorpay.Currency,
                    customerEmail: order.Email,
                    errorCode: "RAZORPAY_ORDER_CREATE",
                    ipAddress: ip,
                    cancellationToken: cancellationToken);

                return StatusCode(502, new
                {
                    message = "Unable to create Razorpay order. Please try again.",
                    detail = ex.Message,
                });
            }

            order.RazorpayOrderId = rzOrder.Id;
            await db.SaveChangesAsync(cancellationToken);

            // Keep cart until payment succeeds; store cart id in a payment log note.
            await paymentLogs.LogAsync(
                eventType: "RazorpayOrderCreated",
                status: "Created",
                message: $"Razorpay order {rzOrder.Id} created for {orderNumber}",
                orderId: order.Id,
                orderNumber: order.OrderNumber,
                razorpayOrderId: rzOrder.Id,
                amount: amountInr,
                currency: rzOrder.Currency,
                customerEmail: order.Email,
                request: new { receipt = orderNumber, amountPaise = rzOrder.Amount },
                response: new { rzOrder.Id, rzOrder.Status, rzOrder.Amount, cartId = request.CartId },
                ipAddress: ip,
                cancellationToken: cancellationToken);

            // Persist cartId against order via a lightweight note in PaymentLogs response already done.
            // Attach cart id on order by storing in a dedicated field? Use PaymentLogs + verify clears cart from request.
            // Frontend will pass cartId again on verify.

            var dto = OrdersController.MapOrder(order);
            return Ok(new RazorpayInitiateResponse(
                order.Id,
                order.OrderNumber,
                rzOrder.Id,
                razorpay.KeyId,
                rzOrder.Amount,
                amountInr,
                rzOrder.Currency,
                $"{order.FirstName} {order.LastName}".Trim(),
                order.Email,
                $"Bagly order {order.OrderNumber}",
                dto));
        }
        catch (Exception ex)
        {
            await paymentLogs.LogAsync(
                eventType: "InitiateError",
                status: "Failed",
                message: ex.Message,
                customerEmail: request.Email,
                errorCode: "INITIATE_EXCEPTION",
                ipAddress: ip,
                cancellationToken: cancellationToken);
            throw;
        }
    }

    [HttpPost("razorpay/verify")]
    public async Task<ActionResult<OrderDto>> Verify(
        [FromBody] RazorpayVerifyRequest request,
        CancellationToken cancellationToken)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        var order = await db.Orders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken);

        if (order is null)
        {
            return NotFound(new { message = "Order not found." });
        }

        if (!string.Equals(order.RazorpayOrderId, request.RazorpayOrderId, StringComparison.Ordinal))
        {
            await paymentLogs.LogAsync(
                eventType: "VerificationFailed",
                status: "Failed",
                message: "Razorpay order id mismatch.",
                orderId: order.Id,
                orderNumber: order.OrderNumber,
                razorpayOrderId: request.RazorpayOrderId,
                razorpayPaymentId: request.RazorpayPaymentId,
                customerEmail: order.Email,
                errorCode: "ORDER_ID_MISMATCH",
                ipAddress: ip,
                cancellationToken: cancellationToken);
            return BadRequest(new { message = "Payment order mismatch." });
        }

        if (order.Status == "Confirmed" && order.PaymentStatus == "Paid")
        {
            logger.LogDebug(
                "Razorpay verify idempotent for {OrderNumber}: already confirmed and paid; confirmation email is not resent.",
                order.OrderNumber);
            // Idempotent Shiprocket create (skips if ShiprocketOrderId already stored).
            if (string.IsNullOrWhiteSpace(order.ShiprocketOrderId))
            {
                await DispatchShiprocketAsync(order.Id, cancellationToken);
            }

            return Ok(OrdersController.MapOrder(order));
        }

        if (order.Status == "OutOfStock")
        {
            return await HandleOutOfStockRetryAsync(order, request, ip, cancellationToken);
        }

        var valid = razorpay.VerifyPaymentSignature(
            request.RazorpayOrderId,
            request.RazorpayPaymentId,
            request.RazorpaySignature);

        if (!valid)
        {
            order.PaymentStatus = "Failed";
            order.Status = "PaymentFailed";
            await db.SaveChangesAsync(cancellationToken);

            await paymentLogs.LogAsync(
                eventType: "SignatureInvalid",
                status: "Failed",
                message: "Razorpay payment signature verification failed.",
                orderId: order.Id,
                orderNumber: order.OrderNumber,
                razorpayOrderId: request.RazorpayOrderId,
                razorpayPaymentId: request.RazorpayPaymentId,
                razorpaySignature: request.RazorpaySignature,
                amount: order.AmountInr,
                currency: order.Currency,
                customerEmail: order.Email,
                errorCode: "INVALID_SIGNATURE",
                ipAddress: ip,
                cancellationToken: cancellationToken);

            return BadRequest(new { message = "Payment verification failed." });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var stockResult = await StockDecrementer.TryDecrementAsync(db, order.Items, cancellationToken);

        if (!stockResult.Success)
        {
            await transaction.RollbackAsync(cancellationToken);

            // Razorpay auto-captures on order creation (payment_capture: 1), so the customer's
            // money is already taken by the time we get here. Best effort: refund it immediately
            // rather than leaving Paid+OutOfStock as the only outcome.
            var refunded = await razorpay.TryRefundPaymentAsync(
                request.RazorpayPaymentId,
                reason: $"Out of stock: {stockResult.InsufficientProductName}",
                cancellationToken);

            order.Status = "OutOfStock";
            order.PaymentStatus = refunded ? "Refunded" : "Paid";
            order.RazorpayPaymentId = request.RazorpayPaymentId;
            order.PaidAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogCritical(
                "Out of stock after payment captured for order {OrderNumber} ({OrderId}): product {ProductId} had insufficient stock. Refund {RefundOutcome}.",
                order.OrderNumber,
                order.Id,
                stockResult.InsufficientProductId,
                refunded ? "succeeded" : "failed or unavailable — needs manual support follow-up");

            await paymentLogs.LogAsync(
                eventType: "OutOfStockAfterPayment",
                status: order.PaymentStatus,
                message: $"Stock insufficient for product '{stockResult.InsufficientProductName}' after payment captured for {order.OrderNumber}. Refund {(refunded ? "succeeded" : "failed or unavailable")}.",
                orderId: order.Id,
                orderNumber: order.OrderNumber,
                razorpayOrderId: request.RazorpayOrderId,
                razorpayPaymentId: request.RazorpayPaymentId,
                razorpaySignature: request.RazorpaySignature,
                amount: order.AmountInr,
                currency: order.Currency,
                customerEmail: order.Email,
                errorCode: "OUT_OF_STOCK",
                ipAddress: ip,
                cancellationToken: cancellationToken);

            emailDispatcher.Enqueue(order.Id, "razorpay-verify-outofstock");

            return Conflict(new
            {
                message = refunded
                    ? "An item in your order just sold out and your payment has been refunded. Please reorder the remaining items."
                    : "Your payment was received but an item in your order just sold out. Our team has been notified — please contact support with your order number for a refund.",
                orderNumber = order.OrderNumber,
                refunded,
            });
        }

        order.PaymentStatus = "Paid";
        order.Status = "Confirmed";
        order.RazorpayPaymentId = request.RazorpayPaymentId;
        order.PaidAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (request.CartId is Guid cartId)
        {
            var cart = await db.Carts.Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.Id == cartId, cancellationToken);
            if (cart is not null && cart.Items.Count > 0)
            {
                db.CartItems.RemoveRange(cart.Items);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        await paymentLogs.LogAsync(
            eventType: "PaymentVerified",
            status: "Paid",
            message: $"Payment verified for {order.OrderNumber}",
            orderId: order.Id,
            orderNumber: order.OrderNumber,
            razorpayOrderId: request.RazorpayOrderId,
            razorpayPaymentId: request.RazorpayPaymentId,
            razorpaySignature: request.RazorpaySignature,
            amount: order.AmountInr,
            currency: order.Currency,
            customerEmail: order.Email,
            response: new { order.Status, order.PaymentStatus, order.PaidAtUtc },
            ipAddress: ip,
            cancellationToken: cancellationToken);

        var confirmedOrder = OrdersController.MapOrder(order);
        emailDispatcher.Enqueue(order.Id, "razorpay-verify");
        await DispatchShiprocketAsync(order.Id, cancellationToken);
        return Ok(confirmedOrder);
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

    /// <summary>
    /// Handles a verify retry for an order already marked OutOfStock (e.g. the customer's browser
    /// re-submitted after a network blip, or a duplicate webhook/click). Never re-runs the atomic
    /// stock decrement, and never re-issues a refund once one has already succeeded — Razorpay
    /// refunds are not safely repeatable and PaymentStatus must never be downgraded from
    /// "Refunded" back to "Paid".
    /// </summary>
    private async Task<ActionResult<OrderDto>> HandleOutOfStockRetryAsync(
        Order order,
        RazorpayVerifyRequest request,
        string? ip,
        CancellationToken cancellationToken)
    {
        if (order.PaymentStatus == "Refunded")
        {
            logger.LogDebug(
                "Razorpay verify idempotent for {OrderNumber}: already out-of-stock and refunded; no refund API call made.",
                order.OrderNumber);

            await paymentLogs.LogAsync(
                eventType: "OutOfStockRetryIdempotent",
                status: order.PaymentStatus,
                message: $"Retry verify for out-of-stock order {order.OrderNumber} short-circuited; already refunded.",
                orderId: order.Id,
                orderNumber: order.OrderNumber,
                razorpayOrderId: request.RazorpayOrderId,
                razorpayPaymentId: order.RazorpayPaymentId ?? request.RazorpayPaymentId,
                amount: order.AmountInr,
                currency: order.Currency,
                customerEmail: order.Email,
                errorCode: "OUT_OF_STOCK_RETRY",
                ipAddress: ip,
                cancellationToken: cancellationToken);

            return Conflict(new
            {
                message = "An item in your order just sold out and your payment has been refunded. Please reorder the remaining items.",
                orderNumber = order.OrderNumber,
                refunded = true,
            });
        }

        // PaymentStatus is "Paid" here — the original refund attempt failed or Razorpay was
        // unavailable. Allow one more attempt on retry instead of stranding the customer.
        var paymentId = order.RazorpayPaymentId ?? request.RazorpayPaymentId;
        var refunded = await razorpay.TryRefundPaymentAsync(
            paymentId,
            reason: $"Out of stock refund retry for order {order.OrderNumber}",
            cancellationToken);

        if (refunded)
        {
            order.PaymentStatus = "Refunded";
            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogCritical(
            "Out-of-stock verify retry for order {OrderNumber} ({OrderId}): refund retry {RefundOutcome}.",
            order.OrderNumber,
            order.Id,
            refunded ? "succeeded" : "failed or unavailable — still needs manual support follow-up");

        await paymentLogs.LogAsync(
            eventType: "OutOfStockRetryRefundAttempt",
            status: order.PaymentStatus,
            message: $"Retry verify for out-of-stock order {order.OrderNumber}; refund retry {(refunded ? "succeeded" : "failed or unavailable")}.",
            orderId: order.Id,
            orderNumber: order.OrderNumber,
            razorpayOrderId: request.RazorpayOrderId,
            razorpayPaymentId: paymentId,
            amount: order.AmountInr,
            currency: order.Currency,
            customerEmail: order.Email,
            errorCode: "OUT_OF_STOCK_RETRY",
            ipAddress: ip,
            cancellationToken: cancellationToken);

        return Conflict(new
        {
            message = refunded
                ? "An item in your order just sold out and your payment has been refunded. Please reorder the remaining items."
                : "Your payment was received but an item in your order just sold out. Our team has been notified — please contact support with your order number for a refund.",
            orderNumber = order.OrderNumber,
            refunded,
        });
    }

    [HttpPost("razorpay/failure")]
    public async Task<IActionResult> ReportFailure(
        [FromBody] RazorpayFailureRequest request,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken);
        if (order is null)
        {
            return NotFound(new { message = "Order not found." });
        }

        if (order.PaymentStatus != "Paid")
        {
            order.PaymentStatus = "Failed";
            order.Status = "PaymentFailed";
            await db.SaveChangesAsync(cancellationToken);
        }

        await paymentLogs.LogAsync(
            eventType: "PaymentFailed",
            status: "Failed",
            message: request.Description ?? "Customer payment failed or cancelled.",
            orderId: order.Id,
            orderNumber: order.OrderNumber,
            razorpayOrderId: request.RazorpayOrderId ?? order.RazorpayOrderId,
            amount: order.AmountInr,
            currency: order.Currency,
            customerEmail: order.Email,
            errorCode: request.Code,
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken: cancellationToken);

        return Ok(new { message = "Payment failure recorded." });
    }

    private async Task<(List<(string ProductId, string Name, string Color, decimal Price, int Quantity)>? Items, string? Error)>
        TryBuildLineItemsAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var lineItems = new List<(string ProductId, string Name, string Color, decimal Price, int Quantity)>();

        if (request.CartId is Guid cartId)
        {
            var cart = await db.Carts.Include(c => c.Items)
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == cartId, cancellationToken);

            if (cart is null || cart.Items.Count == 0)
            {
                return (null, "Cart is empty or was not found.");
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
                    return (null, "Each item quantity must be at least 1.");
                }

                var product = await db.Products.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == item.ProductId && p.IsActive, cancellationToken);

                if (product is null)
                {
                    return (null, $"Product '{item.ProductId}' was not found.");
                }

                lineItems.Add((product.Id, product.Name, item.Color, product.Price, item.Quantity));
            }
        }
        else
        {
            return (null, "Provide a cartId or items to place an order.");
        }

        var stockError = await ValidateStockAsync(lineItems, cancellationToken);
        return stockError is null ? (lineItems, null) : (null, stockError);
    }

    /// <summary>
    /// Re-validates current stock for every distinct product across all lines before a Razorpay
    /// order is created, so we don't send the customer to pay for something that just sold out.
    /// This is a best-effort pre-check only — the authoritative, race-safe check is the atomic
    /// DB decrement performed on verify.
    /// </summary>
    private async Task<string?> ValidateStockAsync(
        List<(string ProductId, string Name, string Color, decimal Price, int Quantity)> lineItems,
        CancellationToken cancellationToken)
    {
        var requestedByProduct = lineItems
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var productIds = requestedByProduct.Keys.ToList();
        var products = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        foreach (var (productId, requestedQty) in requestedByProduct)
        {
            var product = products.FirstOrDefault(p => p.Id == productId);
            if (product is null || !product.IsActive)
            {
                return "One of the items in your order is no longer available. Please update your cart.";
            }

            if (product.StockQuantity < requestedQty)
            {
                return product.StockQuantity <= 0
                    ? $"'{product.Name}' just sold out. Please remove it from your cart."
                    : $"Only {product.StockQuantity} left in stock for '{product.Name}'. Please update your cart.";
            }
        }

        return null;
    }

    private static bool IsIndia(string? country) =>
        string.Equals(country?.Trim(), "India", StringComparison.OrdinalIgnoreCase);

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
}
