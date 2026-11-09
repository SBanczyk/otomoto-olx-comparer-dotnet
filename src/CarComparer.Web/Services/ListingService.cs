using CarComparer.Core.Listings;
using CarComparer.Scraping;
using CarComparer.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CarComparer.Web.Services;

/// <summary>
/// Fetching and persisting listings. Scraping (network) is separate from applying results (DbContext), so callers
/// can scrape several listings in parallel and then persist them one at a time on the single DbContext.
/// </summary>
public class ListingService(
    AppDbContext db,
    IEnumerable<IListingScraper> scrapers,
    IExchangeRateProvider rates,
    TimeProvider time,
    IOptions<ScrapingOptions> options)
{
    private readonly ScrapingOptions _options = options.Value;

    private async Task<ScrapeResult> ScrapeAsync(ListingUrl url, CancellationToken ct)
    {
        var scraper = scrapers.FirstOrDefault(s => s.CanHandle(url.Uri));
        return scraper is null
            ? ScrapeResult.Fail(ScrapeFailureKind.UnsupportedUrl, "No scraper for this URL.")
            : await scraper.FetchAsync(url.Uri, ct).ConfigureAwait(false);
    }

    /// <summary>Scrapes many URLs concurrently, bounded by Scraping:MaxConcurrentRequests.</summary>
    public async Task<IReadOnlyList<ScrapeResult>> ScrapeManyAsync(IReadOnlyList<ListingUrl> urls, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentRequests));
        var tasks = urls.Select(async url =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await ScrapeAsync(url, ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });
        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public Listing GetOrAdd(ListingUrl url, Listing? existing)
    {
        if (existing is not null)
        {
            return existing;
        }

        var listing = new Listing
        {
            Portal = url.Portal,
            ExternalId = url.ExternalId,
            Url = url.Uri.ToString(),
            CreatedAt = time.GetUtcNow(),
        };
        db.Listings.Add(listing);
        return listing;
    }

    /// <summary>
    /// Records the outcome on the listing. Success adds a snapshot; failure only updates LastFetch* and keeps older snapshots.
    /// Does not save.
    /// </summary>
    public async Task ApplyAsync(Listing listing, ScrapeResult result, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        listing.LastFetchAttemptAt = now;

        if (result.Success is { } success)
        {
            var currency = success.Data.Currency;
            var rate = currency == "PLN" ? null : await rates.GetLatestAsync(currency, ct).ConfigureAwait(false);
            listing.Snapshots.Add(ListingSnapshot.From(success.Data, success.RawPayload, now, rate));
            listing.LastFetchFailure = null;
            listing.LastFetchMessage = null;
            listing.InactiveSince = null;
            return;
        }

        var failure = result.Failure!;
        listing.LastFetchFailure = failure.Kind;
        listing.LastFetchMessage = failure.Message.Length > 500 ? failure.Message[..500] : failure.Message;
        if (failure.Kind == ScrapeFailureKind.Removed)
        {
            listing.InactiveSince ??= now;
        }
    }

    public bool HasFreshSnapshot(Listing? listing)
    {
        var latest = listing?.Snapshots.MaxBy(s => s.FetchedAt);
        return latest is not null && time.GetUtcNow() - latest.FetchedAt < _options.MinRefreshInterval;
    }

    public bool IsThrottled(Listing listing) =>
        listing.LastFetchAttemptAt is { } at && time.GetUtcNow() - at < _options.MinRefreshInterval;

    public int MinutesSinceAttempt(Listing listing) =>
        listing.LastFetchAttemptAt is { } at ? (int)Math.Max(0, (time.GetUtcNow() - at).TotalMinutes) : 0;

    public Task<Dictionary<(Portal, string), Listing>> LoadAsync(IEnumerable<ListingUrl> urls, CancellationToken ct)
    {
        var list = urls.ToList();
        var ids = list.Select(u => u.ExternalId).Distinct().ToList();
        var portals = list.Select(u => u.Portal).Distinct().ToList();
        // Both columns of the unique (Portal, ExternalId) index, so SQL Server can seek.
        return db.Listings
            .Where(l => portals.Contains(l.Portal) && ids.Contains(l.ExternalId))
            .Include(l => l.Snapshots.OrderByDescending(s => s.FetchedAt).Take(1))
            .ToDictionaryAsync(l => (l.Portal, l.ExternalId), ct);
    }
}


