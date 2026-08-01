using System.Threading.Channels;

namespace Bagly.Api.Services;

public interface IStockAlertNotificationDispatcher
{
    void Enqueue(string productId, string trigger);
}

/// <summary>
/// Queues restock notification emails on a hosted background worker so admin product
/// updates are never slowed down (or failed) by outbound email calls.
/// </summary>
public sealed class StockAlertNotificationDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<StockAlertNotificationDispatcher> logger) : BackgroundService, IStockAlertNotificationDispatcher
{
    private readonly Channel<(string ProductId, string Trigger)> _queue =
        Channel.CreateUnbounded<(string, string)>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    public void Enqueue(string productId, string trigger)
    {
        if (string.IsNullOrWhiteSpace(productId))
        {
            return;
        }

        if (!_queue.Writer.TryWrite((productId, trigger)))
        {
            logger.LogWarning(
                "Stock alert notification queue rejected product {ProductId} (trigger={Trigger}).",
                productId,
                trigger);
            return;
        }

        logger.LogInformation(
            "Stock alert notification queued for product {ProductId} (trigger={Trigger}).",
            productId,
            trigger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (productId, trigger) in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var notifier = scope.ServiceProvider.GetRequiredService<IStockAlertNotifier>();

                logger.LogInformation(
                    "Processing queued restock notification for product {ProductId} (trigger={Trigger}).",
                    productId,
                    trigger);

                await notifier.NotifyRestockAsync(productId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Background restock notification failed for product {ProductId} (trigger={Trigger}).",
                    productId,
                    trigger);
            }
        }
    }
}
