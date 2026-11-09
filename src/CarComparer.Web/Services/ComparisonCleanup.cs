using CarComparer.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CarComparer.Web.Services;

public sealed class ComparisonsOptions
{
    public const string SectionName = "Comparisons";
    public TimeSpan AnonymousRetention { get; set; } = TimeSpan.FromDays(30);
    /// <summary>How often the background job runs. Zero or negative disables it.</summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(6);
}

public sealed record CleanupResult(int AnonymousComparisonsDeleted, int OrphanListingsDeleted);

/// <summary>The cleanup logic, kept apart from the timer so a test can run it directly.</summary>
public class ComparisonCleanup(AppDbContext db, TimeProvider time, IOptions<ComparisonsOptions> options)
{
    public async Task<CleanupResult> RunAsync(CancellationToken ct)
    {
        var cutoff = time.GetUtcNow() - options.Value.AnonymousRetention;

        // Items go with their comparison through the database cascade.
        var comparisons = await db.Comparisons
            .Where(c => c.OwnerId == null && c.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        // Snapshots go with their listing through the database cascade.
        var listings = await db.Listings
            .Where(l => !db.ComparisonItems.Any(i => i.ListingId == l.Id))
            .ExecuteDeleteAsync(ct);

        return new CleanupResult(comparisons, listings);
    }
}

public sealed class ComparisonCleanupService(
    IServiceScopeFactory scopes, IOptions<ComparisonsOptions> options, ILogger<ComparisonCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = options.Value.CleanupInterval;
        if (period <= TimeSpan.Zero)
        {
            return;
        }

        // Real clock: the timer is not tied to TimeProvider, so a fake clock in tests cannot make it tick.
        using var timer = new PeriodicTimer(period);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<ComparisonCleanup>().RunAsync(stoppingToken);
                logger.LogInformation("Cleanup removed {Comparisons} anonymous comparisons and {Listings} orphan listings",
                    result.AnonymousComparisonsDeleted, result.OrphanListingsDeleted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Cleanup failed");
            }
        }
    }
}
