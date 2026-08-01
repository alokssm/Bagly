using System.Threading.Channels;
using Bagly.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Services;

public interface IOrderConfirmationEmailDispatcher
{
    void Enqueue(Guid orderId, string trigger);
}

/// <summary>
/// Queues order confirmation emails on a hosted background worker (reliable scoped DI).
/// </summary>
public sealed class OrderConfirmationEmailDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<OrderConfirmationEmailDispatcher> logger) : BackgroundService, IOrderConfirmationEmailDispatcher
{
    private readonly Channel<(Guid OrderId, string Trigger)> _queue =
        Channel.CreateUnbounded<(Guid, string)>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    public void Enqueue(Guid orderId, string trigger)
    {
        if (!_queue.Writer.TryWrite((orderId, trigger)))
        {
            logger.LogWarning(
                "Order confirmation email queue rejected order {OrderId} (trigger={Trigger}).",
                orderId,
                trigger);
            return;
        }

        logger.LogInformation(
            "Order confirmation email queued for order {OrderId} (trigger={Trigger}).",
            orderId,
            trigger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (orderId, trigger) in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var scopedDb = scope.ServiceProvider.GetRequiredService<BaglyDbContext>();
                var scopedEmails = scope.ServiceProvider.GetRequiredService<IOrderConfirmationEmailService>();

                var orderForEmail = await scopedDb.Orders.AsNoTracking()
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == orderId, stoppingToken);

                if (orderForEmail is null)
                {
                    logger.LogWarning(
                        "Order confirmation email skipped: order {OrderId} not found (trigger={Trigger}).",
                        orderId,
                        trigger);
                    continue;
                }

                if (string.Equals(orderForEmail.Status, "OutOfStock", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInformation(
                        "Processing queued out-of-stock/refund email for {OrderNumber} (orderId={OrderId}, trigger={Trigger}).",
                        orderForEmail.OrderNumber,
                        orderId,
                        trigger);

                    await scopedEmails.SendOutOfStockRefundAsync(orderForEmail, stoppingToken);
                }
                else
                {
                    logger.LogInformation(
                        "Processing queued order confirmation email for {OrderNumber} (orderId={OrderId}, trigger={Trigger}).",
                        orderForEmail.OrderNumber,
                        orderId,
                        trigger);

                    await scopedEmails.SendAsync(orderForEmail, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Background order confirmation email failed for order {OrderId} (trigger={Trigger}). Order remains confirmed.",
                    orderId,
                    trigger);
            }
        }
    }
}
