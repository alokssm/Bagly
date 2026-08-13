using System.Threading.Channels;

namespace Bagly.Api.Services;

public interface IShiprocketOrderDispatcher
{
    void Enqueue(Guid orderId);
}

/// <summary>
/// Queues Shiprocket adhoc order creation on a hosted background worker so checkout stays fast.
/// Failures are logged only — customer confirmation is never blocked.
/// </summary>
public sealed class ShiprocketOrderDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<ShiprocketOrderDispatcher> logger) : BackgroundService, IShiprocketOrderDispatcher
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    public void Enqueue(Guid orderId)
    {
        if (!_queue.Writer.TryWrite(orderId))
        {
            logger.LogWarning("Shiprocket queue rejected order {OrderId}.", orderId);
            return;
        }

        logger.LogInformation("Shiprocket create queued for order {OrderId}.", orderId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var orderId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                // Brief delay so checkout CommitAsync is fully visible before the first read
                // (defensive against rare commit visibility races on Neon).
                await Task.Delay(250, stoppingToken);
                await using var scope = scopeFactory.CreateAsyncScope();
                var shiprocket = scope.ServiceProvider.GetRequiredService<IShiprocketService>();
                await shiprocket.TryCreateAdhocOrderForConfirmedOrderAsync(orderId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Background Shiprocket create failed for order {OrderId}. Order remains confirmed.",
                    orderId);
            }
        }
    }
}
