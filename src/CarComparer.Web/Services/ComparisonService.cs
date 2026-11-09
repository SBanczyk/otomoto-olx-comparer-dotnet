using System.Security.Cryptography;
using CarComparer.Core.Comparison;
using CarComparer.Core.Listings;
using CarComparer.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace CarComparer.Web.Services;

public sealed record CreateComparisonResult(string? Id, IReadOnlyDictionary<int, string> FieldErrors, string? FormError)
{
    public bool Succeeded => Id is not null;
}

public sealed record RefreshNote(int Position, int MinutesAgo);

public sealed record RefreshResult(IReadOnlyList<RefreshNote> Skipped);

public sealed record ComparisonView(Comparison Comparison, ComparisonTable Table, IReadOnlyList<ColumnInfo> Columns);

public sealed record ColumnInfo(ComparisonColumn Column, DateTimeOffset? LastAttemptAt);

public class ComparisonService(AppDbContext db, ListingService listings, TimeProvider time)
{
    public const int MinListings = 2;
    public const int MaxListings = 4;
    private const string IdAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    public async Task<CreateComparisonResult> CreateAsync(IEnumerable<string?> rawUrls, string? ownerId, CancellationToken ct)
    {
        var inputs = rawUrls.Select(u => u?.Trim()).Where(u => !string.IsNullOrEmpty(u)).ToList();
        if (inputs.Count is < MinListings or > MaxListings)
        {
            return Fail($"Podaj od {MinListings} do {MaxListings} linków do ogłoszeń.");
        }

        var errors = new Dictionary<int, string>();
        var urls = new List<ListingUrl>();
        var seen = new HashSet<(Portal, string)>();
        for (var i = 0; i < inputs.Count; i++)
        {
            if (!ListingUrl.TryCreate(inputs[i], out var url))
            {
                errors[i] = FailureMessages.InvalidUrl;
            }
            else if (!seen.Add((url!.Portal, url.ExternalId)))
            {
                errors[i] = "Ten link powtarza się.";
            }
            else
            {
                urls.Add(url);
            }
        }

        if (errors.Count > 0)
        {
            return new CreateComparisonResult(null, errors, null);
        }

        var existing = await listings.LoadAsync(urls, ct);
        var current = urls.Select(u => existing.GetValueOrDefault((u.Portal, u.ExternalId))).ToList();

        var toFetch = urls.Where((u, i) => !listings.HasFreshSnapshot(current[i])).ToList();
        var scraped = await listings.ScrapeManyAsync(toFetch, ct);
        var results = toFetch.Zip(scraped).ToDictionary(p => p.First.ExternalId, p => p.Second);

        for (var i = 0; i < urls.Count; i++)
        {
            if (results.TryGetValue(urls[i].ExternalId, out var r) && r.Failure?.Kind == ScrapeFailureKind.UnsupportedUrl)
            {
                errors[i] = FailureMessages.InvalidUrl;
            }
        }

        if (errors.Count > 0)
        {
            return new CreateComparisonResult(null, errors, null);
        }

        var entities = new List<Listing>();
        for (var i = 0; i < urls.Count; i++)
        {
            var listing = listings.GetOrAdd(urls[i], current[i]);
            if (results.TryGetValue(urls[i].ExternalId, out var r))
            {
                await listings.ApplyAsync(listing, r, ct);
            }

            entities.Add(listing);
        }

        if (entities.All(l => l.Snapshots.Count == 0))
        {
            // Nothing usable was fetched. Do not save the listings either.
            db.ChangeTracker.Clear();
            var perField = entities
                .Select((l, i) => (i, message: FailureMessages.For(l.LastFetchFailure, l.InactiveSince)))
                .ToDictionary(x => x.i, x => x.message);
            return new CreateComparisonResult(null, perField, "Nie udało się pobrać żadnego z ogłoszeń. Porównanie nie zostało utworzone.");
        }

        var comparison = new Comparison
        {
            Id = NewId(),
            OwnerId = ownerId,
            CreatedAt = time.GetUtcNow(),
            Items = entities.Select((l, i) => new ComparisonItem { Position = i + 1, Listing = l }).ToList(),
        };
        db.Comparisons.Add(comparison);
        await db.SaveChangesAsync(ct);
        return new CreateComparisonResult(comparison.Id, new Dictionary<int, string>(), null);
    }

    public async Task<Comparison?> FindAsync(string id, CancellationToken ct) =>
        await db.Comparisons.AsNoTracking().FirstOrDefaultAsync(c => c.Id == NormaliseId(id), ct);

    public async Task<ComparisonView?> GetViewAsync(string id, CancellationToken ct)
    {
        var comparison = await db.Comparisons
            .Include(c => c.Items.OrderBy(i => i.Position)).ThenInclude(i => i.Listing!)
            .ThenInclude(l => l.Snapshots.OrderByDescending(s => s.FetchedAt).Take(1))
            .AsNoTracking()
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == NormaliseId(id), ct);
        if (comparison is null)
        {
            return null;
        }

        var infos = comparison.Items.OrderBy(i => i.Position).Select(i => ToColumn(i.Listing!)).ToList();
        var table = ComparisonTableBuilder.Build(infos.Select(c => c.Column).ToList(), time.GetUtcNow().Year);
        return new ComparisonView(comparison, table, infos);
    }

    private static ColumnInfo ToColumn(Listing l)
    {
        var snapshot = l.Snapshots.MaxBy(s => s.FetchedAt);
        var column = new ComparisonColumn
        {
            Portal = l.Portal,
            Url = l.Url,
            Data = snapshot?.ToListingData(),
            PricePln = snapshot?.PricePln,
            FetchedAt = snapshot?.FetchedAt,
            Failure = l.LastFetchFailure,
            FailureMessage = l.LastFetchFailure is null ? null : FailureMessages.For(l.LastFetchFailure, l.InactiveSince),
            InactiveSince = l.InactiveSince,
        };
        return new ColumnInfo(column, l.LastFetchAttemptAt);
    }

    /// <summary>Refreshes every listing not fetched within the minimum interval. Safe for anyone with the link: it throttles the shared Listing rows.</summary>
    public async Task<RefreshResult?> RefreshAsync(string id, CancellationToken ct)
    {
        var items = await db.ComparisonItems
            .Where(i => i.ComparisonId == NormaliseId(id))
            .OrderBy(i => i.Position)
            .Include(i => i.Listing!).ThenInclude(l => l.Snapshots.OrderByDescending(s => s.FetchedAt).Take(1))
            .ToListAsync(ct);
        if (items.Count == 0)
        {
            return null;
        }

        var skipped = new List<RefreshNote>();
        var toRefresh = new List<(ComparisonItem Item, ListingUrl Url)>();
        foreach (var item in items)
        {
            if (listings.IsThrottled(item.Listing!))
            {
                skipped.Add(new RefreshNote(item.Position, listings.MinutesSinceAttempt(item.Listing!)));
            }
            else if (ListingUrl.TryCreate(item.Listing!.Url, out var url))
            {
                toRefresh.Add((item, url!));
            }
        }

        var results = await listings.ScrapeManyAsync(toRefresh.Select(t => t.Url).ToList(), ct);
        foreach (var ((item, _), result) in toRefresh.Zip(results))
        {
            try
            {
                await listings.ApplyAsync(item.Listing!, result, ct);
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Someone else refreshed this listing at the same time: first write wins, ours counts as skipped.
                foreach (var entry in ex.Entries)
                {
                    entry.State = EntityState.Detached;
                }

                // Also drop the snapshot we had added, or the next SaveChanges would insert it anyway.
                foreach (var snapshot in item.Listing!.Snapshots.Where(s => db.Entry(s).State == EntityState.Added).ToList())
                {
                    db.Entry(snapshot).State = EntityState.Detached;
                }

                skipped.Add(new RefreshNote(item.Position, 0));
            }
        }

        return new RefreshResult(skipped.OrderBy(s => s.Position).ToList());
    }

    /// <summary>Creates a copy owned by <paramref name="ownerId"/>, pointing at the same Listing rows (no re-scraping).</summary>
    public async Task<string?> SaveCopyAsync(string sourceId, string ownerId, CancellationToken ct)
    {
        var source = await db.Comparisons.AsNoTracking().Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == sourceId.ToLower(), ct);
        if (source is null)
        {
            return null;
        }

        var copy = new Comparison
        {
            Id = NewId(),
            OwnerId = ownerId,
            CreatedAt = time.GetUtcNow(),
            Items = source.Items.Select(i => new ComparisonItem { Position = i.Position, ListingId = i.ListingId }).ToList(),
        };
        db.Comparisons.Add(copy);
        await db.SaveChangesAsync(ct);
        return copy.Id;
    }

    public async Task<IReadOnlyList<MyComparisonRow>> ListForOwnerAsync(string ownerId, CancellationToken ct)
    {
        var comparisons = await db.Comparisons
            .Where(c => c.OwnerId == ownerId)
            .OrderByDescending(c => c.CreatedAt)
            .Include(c => c.Items.OrderBy(i => i.Position)).ThenInclude(i => i.Listing!)
            .ThenInclude(l => l.Snapshots.OrderByDescending(s => s.FetchedAt).Take(1))
            .AsNoTracking()
            .AsSplitQuery()
            .ToListAsync(ct);

        return comparisons.Select(c => new MyComparisonRow(
            c.Id,
            c.CreatedAt,
            c.Items.Select(i => i.Listing!.Snapshots.MaxBy(s => s.FetchedAt) is { } s
                ? new MyComparisonCar($"{s.Brand} {s.Model}", s.Year, s.Price, s.Currency)
                : null).ToList())).ToList();
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct) =>
        await db.Comparisons.Where(c => c.Id == NormaliseId(id)).ExecuteDeleteAsync(ct) > 0;

    public Task<int> DeleteAllForOwnerAsync(string ownerId, CancellationToken ct) =>
        db.Comparisons.Where(c => c.OwnerId == ownerId).ExecuteDeleteAsync(ct);

    private static string NormaliseId(string id) => id.ToLowerInvariant();

    public static string NewId() => RandomNumberGenerator.GetString(IdAlphabet, 12);

    private static CreateComparisonResult Fail(string message) =>
        new(null, new Dictionary<int, string>(), message);
}

public sealed record MyComparisonCar(string Name, int Year, decimal Price, string Currency);

public sealed record MyComparisonRow(string Id, DateTimeOffset CreatedAt, IReadOnlyList<MyComparisonCar?> Cars);

