using Bagly.Api.Data;
using Bagly.Api.DTOs;
using Bagly.Api.Models;
using Bagly.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController(
    BaglyDbContext db,
    IRazorpayService razorpay,
    IPaymentLogService paymentLogs,
    IOrderConfirmationEmailDispatcher emailDispatcher,
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
                Email = request.Email.Trim(),
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Address = request.Address.Trim(),
                City = request.City.Trim(),
                State = request.State.Trim(),
                Zip = request.Zip.Trim(),
                Country = "India",
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

        if (order.PaymentStatus == "Paid")
        {
            logger.LogDebug(
                "Razorpay verify idempotent for {OrderNumber}: already paid; confirmation email is not resent.",
                order.OrderNumber);
            return Ok(OrdersController.MapOrder(order));
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

        order.PaymentStatus = "Paid";
        order.Status = "Confirmed";
        order.RazorpayPaymentId = request.RazorpayPaymentId;
        order.PaidAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

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
        return Ok(confirmedOrder);
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

            return (lineItems, null);
        }

        if (request.Items is { Count: > 0 })
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

            return (lineItems, null);
        }

        return (null, "Provide a cartId or items to place an order.");
    }

    private static bool IsIndia(string? country) =>
        string.Equals(country?.Trim(), "India", StringComparison.OrdinalIgnoreCase);

}
