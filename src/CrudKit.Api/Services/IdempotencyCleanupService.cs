using CrudKit.Api.Models;
using CrudKit.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrudKit.Api.Services;

/// <summary>
/// Background service that periodically removes expired <see cref="IdempotencyRecord"/> rows.
/// Runs once per hour. Uses <c>ExecuteDeleteAsync</c> for a single bulk-delete statement.
/// </summary>
public class IdempotencyCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IdempotencyCleanupService> _logger;

    public IdempotencyCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<IdempotencyCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Delay slightly at startup so the application is fully initialised
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await CleanupAsync(stoppingToken);

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CrudKitDbContext>();
            var now = DateTime.UtcNow;

            var deleted = await db.Set<IdempotencyRecord>()
                .Where(r => r.ExpiresAt <= now)
                .ExecuteDeleteAsync(ct);

            if (deleted > 0)
                _logger.LogInformation("Idempotency cleanup: removed {Count} expired records.", deleted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Idempotency cleanup failed.");
        }
    }
}
