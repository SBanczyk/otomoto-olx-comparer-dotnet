using CarComparer.Core.Listings;
using CarComparer.Web.Data;
using CarComparer.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CarComparer.Web.IntegrationTests;

[Collection("db")]
public sealed class ComparisonServiceTests(DatabaseFixture database) : IAsyncLifetime
{
    private AppFactory _factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _factory = new AppFactory(database.ConnectionString);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<T> InScope<T>(Func<ComparisonService, AppDbContext, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ComparisonService>(), scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact]
    public async Task Schema_saves_and_reads_a_comparison_with_items()
    {
        var id = await InScope(async (svc, db) =>
        {
            var l1 = new Listing { Portal = Portal.Otomoto, ExternalId = "ID1", Url = "https://www.otomoto.pl/a-ID1.html" };
            var l2 = new Listing { Portal = Portal.Olx, ExternalId = "ID1", Url = "https://www.olx.pl/a-ID1.html" };
            db.Comparisons.Add(new Comparison
            {
                Id = "abc123abc123",
                Items = [new ComparisonItem { Position = 1, Listing = l1 }, new ComparisonItem { Position = 2, Listing = l2 }],
            });
            await db.SaveChangesAsync(Ct);
            return "abc123abc123";
        });

        var loaded = await InScope((svc, db) => db.Comparisons.Include(c => c.Items).SingleAsync(c => c.Id == id, Ct));
        Assert.Equal([1, 2], loaded.Items.Select(i => i.Position).Order());
    }

    [Fact]
    public async Task Same_external_id_on_different_portals_are_different_listings_but_not_twice_on_one_portal()
    {
        await InScope(async (_, db) =>
        {
            db.Listings.Add(new Listing { Portal = Portal.Otomoto, ExternalId = "ID1", Url = "u1" });
            db.Listings.Add(new Listing { Portal = Portal.Olx, ExternalId = "ID1", Url = "u2" });
            await db.SaveChangesAsync(Ct);
            db.Listings.Add(new Listing { Portal = Portal.Olx, ExternalId = "ID1", Url = "u3" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
            return 0;
        });
    }

    [Fact]
    public async Task Creates_an_anonymous_comparison_with_snapshots()
    {
        var result = await InScope((svc, _) => svc.CreateAsync([Sample.Url("IDa1"), Sample.Url("IDb2", olx: true)], null, Ct));

        Assert.True(result.Succeeded);
        Assert.Equal(12, result.Id!.Length);
        Assert.Matches("^[a-z0-9]{12}$", result.Id);
        var view = await InScope((svc, _) => svc.GetViewAsync(result.Id, Ct));
        Assert.Null(view!.Comparison.OwnerId);
        Assert.Equal(2, view.Columns.Count);
        Assert.All(view.Columns, c => Assert.NotNull(c.Column.Data));
    }

    [Fact]
    public async Task One_failing_column_does_not_break_the_comparison()
    {
        _factory.Scraper.Returns("IDbad", ScrapeResult.Fail(ScrapeFailureKind.Blocked, "403"));

        var result = await InScope((svc, _) => svc.CreateAsync([Sample.Url("IDa1"), Sample.Url("IDbad"), Sample.Url("IDc3"), Sample.Url("IDd4")], null, Ct));

        Assert.True(result.Succeeded);
        var view = await InScope((svc, _) => svc.GetViewAsync(result.Id!, Ct));
        var failed = view!.Columns[1].Column;
        Assert.Null(failed.Data);
        Assert.Equal(ScrapeFailureKind.Blocked, failed.Failure);
        Assert.Equal("Portal odrzucił zapytanie. Spróbuj później.", failed.FailureMessage);
        Assert.NotNull(view.Columns[0].Column.Data);
    }

    [Fact]
    public async Task All_failing_creates_nothing()
    {
        _factory.Scraper.Returns("IDx", ScrapeResult.Fail(ScrapeFailureKind.Unreachable, "down"));
        _factory.Scraper.Returns("IDy", ScrapeResult.Fail(ScrapeFailureKind.Removed, "gone"));

        var result = await InScope((svc, _) => svc.CreateAsync([Sample.Url("IDx"), Sample.Url("IDy")], null, Ct));

        Assert.False(result.Succeeded);
        Assert.NotNull(result.FormError);
        Assert.Equal("Portal nie odpowiada.", result.FieldErrors[0]);
        Assert.Equal(0, await InScope((_, db) => db.Comparisons.CountAsync(Ct)));
        Assert.Equal(0, await InScope((_, db) => db.Listings.CountAsync(Ct)));
    }

    [Fact]
    public async Task Unsupported_url_is_a_validation_error_and_blocks_creation()
    {
        var result = await InScope((svc, _) => svc.CreateAsync([Sample.Url("IDa1"), "https://evil.com/otomoto/osobowe"], null, Ct));

        Assert.False(result.Succeeded);
        Assert.Equal(FailureMessages.InvalidUrl, result.FieldErrors[1]);
        Assert.Empty(_factory.Scraper.Calls);
    }

    [Fact]
    public async Task Duplicate_listings_after_normalisation_are_rejected()
    {
        var result = await InScope((svc, _) => svc.CreateAsync([Sample.Url("IDa1"), Sample.Url("IDa1") + "?utm=1"], null, Ct));

        Assert.False(result.Succeeded);
        Assert.Equal("Ten link powtarza się.", result.FieldErrors[1]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task Link_count_must_be_between_two_and_four(int count)
    {
        var urls = Enumerable.Range(0, count).Select(i => Sample.Url($"ID{i}x"));
        var result = await InScope((svc, _) => svc.CreateAsync(urls, null, Ct));
        Assert.False(result.Succeeded);
        Assert.NotNull(result.FormError);
    }

    [Fact]
    public async Task Two_comparisons_with_the_same_listing_share_one_listing_row()
    {
        await InScope((svc, _) => svc.CreateAsync([Sample.Url("IDa1"), Sample.Url("IDb2")], null, Ct));
        _factory.Time.Advance(TimeSpan.FromHours(1));
        await InScope((svc, _) => svc.CreateAsync([Sample.Url("IDa1"), Sample.Url("IDc3")], null, Ct));

        Assert.Equal(3, await InScope((_, db) => db.Listings.CountAsync(Ct)));
        Assert.Equal(2, await InScope((_, db) => db.Comparisons.CountAsync(Ct)));
    }

    [Fact]
    public async Task Fresh_snapshot_is_reused_without_fetching()
    {
        await InScope((svc, _) => svc.CreateAsync([Sample.Url("IDa1"), Sample.Url("IDb2")], null, Ct));
        _factory.Scraper.Calls.Clear();
        _factory.Time.Advance(TimeSpan.FromMinutes(5));

        await InScope((svc, _) => svc.CreateAsync([Sample.Url("IDa1"), Sample.Url("IDc3")], null, Ct));

        Assert.Equal(["IDc3"], _factory.Scraper.Calls);
    }

    [Fact]
    public async Task Foreign_currency_price_is_converted_with_the_rate_and_its_date()
    {
        _factory.Scraper.Returns("IDeur", Sample.Ok("IDeur", price: 10_000, currency: "EUR"));

        await InScope((svc, _) => svc.CreateAsync([Sample.Url("IDeur"), Sample.Url("IDb2")], null, Ct));

        var snapshot = await InScope((_, db) => db.Snapshots.SingleAsync(s => s.Currency == "EUR", Ct));
        Assert.Equal(45_000m, snapshot.PricePln);
        Assert.Equal(4.5m, snapshot.ExchangeRate);
        Assert.Equal(new DateOnly(2026, 11, 6), snapshot.ExchangeRateDate);
    }

    [Fact]
    public async Task Refresh_skips_listings_fetched_within_the_minimum_interval()
    {
        var created = await InScope((svc, _) => svc.CreateAsync([Sample.Url("IDa1"), Sample.Url("IDb2")], null, Ct));
        _factory.Scraper.Calls.Clear();
        _factory.Time.Advance(TimeSpan.FromMinutes(4));

        var early = await InScope((svc, _) => svc.RefreshAsync(created.Id!, Ct));

        Assert.Empty(_factory.Scraper.Calls);
        Assert.Equal([1, 2], early!.Skipped.Select(s => s.Position));
        Assert.All(early.Skipped, s => Assert.Equal(4, s.MinutesAgo));

        _factory.Time.Advance(TimeSpan.FromMinutes(7));
        var later = await InScope((svc, _) => svc.RefreshAsync(created.Id!, Ct));

        Assert.Equal(2, _factory.Scraper.Calls.Count);
        Assert.Empty(later!.Skipped);
        Assert.Equal(4, await InScope((_, db) => db.Snapshots.CountAsync(Ct)));
    }

    [Fact]
    public async Task Failed_refresh_keeps_the_previous_snapshot_and_records_the_failure()
    {
        var created = await InScope((svc, _) => svc.CreateAsync([Sample.Url("IDa1"), Sample.Url("IDb2")], null, Ct));
        _factory.Scraper.Returns("IDa1", ScrapeResult.Fail(ScrapeFailureKind.Removed, "gone"));
        _factory.Time.Advance(TimeSpan.FromMinutes(30));

        await InScope((svc, _) => svc.RefreshAsync(created.Id!, Ct));

        var view = await InScope((svc, _) => svc.GetViewAsync(created.Id!, Ct));
        var column = view!.Columns[0].Column;
        Assert.NotNull(column.Data);
        Assert.Equal(ScrapeFailureKind.Removed, column.Failure);
        Assert.NotNull(column.InactiveSince);
        Assert.Contains("nieaktywne od 09.11.2026", column.FailureMessage);
    }

    [Fact]
    public async Task Unknown_comparison_returns_null()
    {
        Assert.Null(await InScope((svc, _) => svc.GetViewAsync("doesnotexist", Ct)));
        Assert.Null(await InScope((svc, _) => svc.RefreshAsync("doesnotexist", Ct)));
    }
}
