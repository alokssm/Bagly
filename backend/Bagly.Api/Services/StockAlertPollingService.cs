using Bagly.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Bagly.Api.Services;

/// <summary>
/// Safety net for restock emails: periodically scans for in-stock, active products that still
/// have pending stock alerts (e.g. stock changed via a path other than the admin update endpoint)
/// and notifies them. The admin-update trigger (<see cref="StockAlertNotificationDispatcher"/>)
/// handles the common case immediately; this just catches anything that slips through.
/// </summary>
public sealed class StockAlertPollingService(
    IServiceScopeFactory scopeFactory,
    ILogger<StockAlertPollingService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the app a moment to finish startup/seeding before the first sweep.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Stock alert polling sweep failed.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BaglyDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IStockAlertNotifier>();

        var productIds = await db.StockAlerts
            .Where(a => !a.Notified)
            .Select(a => a.ProductId)
            .Distinct()
            .Join(
                db.Products.Where(p => p.IsActive && p.StockQuantity > 0),
                alertProductId => alertProductId,
                product => product.Id,
                (alertProductId, product) => product.Id)
            .ToListAsync(stoppingToken);

        if (productIds.Count == 0)
        {
            return;
        }

        logger.LogInformation(
            "Stock alert polling sweep found {Count} in-stock product(s) with pending alerts.",
            productIds.Count);

        foreach (var productId in productIds)
        {
            await notifier.NotifyRestockAsync(productId, stoppingToken);
        }
    }
}
