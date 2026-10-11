using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atlas.Infrastructure.Persistence;

/// <summary>
/// Hourly housekeeping: deletes refresh tokens that expired more than a week ago. Recently expired/revoked rows
/// are kept so reuse of a stolen token can still be recognised for a while.
/// </summary>
public sealed class RefreshTokenCleanupService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<RefreshTokenCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), clock, stoppingToken); // let the app finish starting first

            using var timer = new PeriodicTimer(Interval, clock);
            do
            {
                await CleanOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task CleanOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();
            var cutoff = clock.GetUtcNow() - Retention;

            var deleted = await db.RefreshTokens.Where(t => t.ExpiresAt < cutoff).ExecuteDeleteAsync(cancellationToken);
            logger.LogDebug("Refresh token cleanup removed {Count} expired tokens", deleted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Refresh token cleanup failed; will retry at the next interval");
        }
    }
}
