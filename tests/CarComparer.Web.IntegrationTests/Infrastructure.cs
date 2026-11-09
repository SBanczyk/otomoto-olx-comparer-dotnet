using CarComparer.Core.Listings;
using CarComparer.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Respawn;
using Testcontainers.MsSql;

namespace CarComparer.Web.IntegrationTests;

/// <summary>One SQL Server container per test collection; data is wiped between tests with Respawn.</summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private Respawner? _respawner;

    public string ConnectionString => new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_container.GetConnectionString())
    {
        InitialCatalog = "CarComparerTests",
    }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();

        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            TablesToIgnore = [new Respawn.Graph.Table("__EFMigrationsHistory")],
        });
    }

    public async Task ResetAsync()
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _respawner!.ResetAsync(connection);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("db")]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;

public sealed class FakeScraper : IListingScraper
{
    private readonly Dictionary<string, ScrapeResult> _results = new();
    private readonly object _lock = new();
    public List<string> Calls { get; } = [];

    public Portal Portal => Portal.Otomoto;

    public bool CanHandle(Uri url) => true;

    public FakeScraper Returns(string externalId, ScrapeResult result)
    {
        _results[externalId] = result;
        return this;
    }

    public Task<ScrapeResult> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        var id = ListingUrl.TryCreate(url.ToString(), out var listingUrl) ? listingUrl!.ExternalId : throw new InvalidOperationException($"Not a listing URL: {url}");
        lock (_lock)
        {
            Calls.Add(id);
        }

        return Task.FromResult(_results.TryGetValue(id, out var r) ? r : Sample.Ok(id));
    }
}

public sealed class FakeRates : IExchangeRateProvider
{
    public Task<ExchangeRate?> GetLatestAsync(string currency, CancellationToken cancellationToken) =>
        Task.FromResult<ExchangeRate?>(currency == "EUR" ? new ExchangeRate(4.5m, new DateOnly(2026, 11, 6)) : null);
}

public static class Sample
{
    public static ListingData Data(string brand = "Kia", decimal price = 50_000, string currency = "PLN") => new()
    {
        Brand = brand, Model = "Ceed", Year = 2020, FuelType = FuelType.Petrol, Gearbox = Gearbox.Manual,
        MileageKm = 100_000, Price = price, Currency = currency,
    };

    public static ScrapeResult Ok(string id, string brand = "Kia", decimal price = 50_000, string currency = "PLN") =>
        ScrapeResult.Ok(Data(brand, price, currency), id, "{}");

    public static string Url(string id, bool olx = false) => olx
        ? $"https://www.olx.pl/d/oferta/car-CID5-{id}.html"
        : $"https://www.otomoto.pl/osobowe/oferta/car-{id}.html";
}

public sealed class AppFactory(string connectionString) : WebApplicationFactory<Program>
{
    public FakeScraper Scraper { get; } = new();
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IListingScraper>();
            services.RemoveAll<IExchangeRateProvider>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<IListingScraper>(Scraper);
            services.AddSingleton<IExchangeRateProvider>(new FakeRates());
            services.AddSingleton<TimeProvider>(Time);
        });
    }
}


