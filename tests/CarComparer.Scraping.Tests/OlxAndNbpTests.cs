using System.Net;
using CarComparer.Core.Listings;
using CarComparer.Scraping.Nbp;
using CarComparer.Scraping.Olx;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using static CarComparer.Scraping.Tests.TestSupport;

namespace CarComparer.Scraping.Tests;

public class OlxScraperTests
{
    private static readonly Uri Url = new("https://www.olx.pl/d/oferta/land-rover-freelander-CID5-ID1d6KJb.html");

    private static OlxScraper Scraper(string pageFixture, string apiFixture, StubHandler? handler = null)
    {
        handler ??= new StubHandler(r => r.RequestUri!.AbsolutePath.StartsWith("/api/")
            ? Ok(Fixture($"olx/{apiFixture}.api.json"))
            : Ok(Fixture($"olx/{pageFixture}.page.html")));
        return new OlxScraper(Client(handler), NullLogger<OlxScraper>.Instance);
    }

    [Fact]
    public async Task Petrol_listing_is_parsed_and_the_api_is_called_with_the_numeric_id()
    {
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.StartsWith("/api/")
            ? Ok(Fixture("olx/petrol.api.json"))
            : Ok(Fixture("olx/petrol.page.html")));

        var result = await Scraper("petrol", "petrol", handler).FetchAsync(Url, TestContext.Current.CancellationToken);

        var success = Assert.IsType<ScrapeSuccess>(result.Success);
        Assert.Equal("ID1d6KJb", success.ExternalId);
        Assert.Equal(new Uri("https://www.olx.pl/api/v1/offers/1109834793/"), handler.Requests[1]);
        var d = success.Data;
        Assert.Equal(("Land Rover", "Freelander", 1999), (d.Brand, d.Model, d.Year));
        Assert.Equal(FuelType.Lpg, d.FuelType);
        Assert.Equal(Gearbox.Manual, d.Gearbox);
        Assert.Equal(BodyType.Suv, d.BodyType);
        Assert.Equal(Drive.AllWheel, d.Drive);
        Assert.Equal(1800, d.EngineCapacityCm3);
        Assert.Equal(120, d.PowerHp);
        Assert.Equal(254000, d.MileageKm);
        Assert.Equal((6900m, "PLN"), (d.Price, d.Currency));
        Assert.False(d.IsDamaged);
    }

    [Fact]
    public async Task Diesel_listing_is_parsed()
    {
        var d = (await Scraper("diesel", "diesel").FetchAsync(Url, TestContext.Current.CancellationToken)).Success!.Data;
        Assert.Equal(("BMW", 2005, FuelType.Diesel), (d.Brand, d.Year, d.FuelType));
        Assert.Equal(Drive.Rear, d.Drive);
        Assert.Equal(BodyType.Wagon, d.BodyType);
        Assert.Equal(450000, d.MileageKm);
    }

    [Fact]
    public async Task Listing_outside_passenger_cars_is_rejected_before_calling_the_api()
    {
        var page = Fixture("olx/petrol.page.html").Replace("/motoryzacja/samochody/", "/motoryzacja/motocykle-skutery/");
        var handler = new StubHandler(_ => Ok(page));
        var result = await Scraper("petrol", "petrol", handler).FetchAsync(Url, TestContext.Current.CancellationToken);
        Assert.Equal(ScrapeFailureKind.NotPassengerCar, result.Failure!.Kind);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Page_404_is_removed()
    {
        var result = await Scraper("petrol", "petrol", new StubHandler(_ => Status(HttpStatusCode.NotFound))).FetchAsync(Url, TestContext.Current.CancellationToken);
        Assert.Equal(ScrapeFailureKind.Removed, result.Failure!.Kind);
    }

    [Fact]
    public async Task Api_404_is_removed()
    {
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.StartsWith("/api/")
            ? Status(HttpStatusCode.NotFound)
            : Ok(Fixture("olx/petrol.page.html")));
        var result = await Scraper("petrol", "petrol", handler).FetchAsync(Url, TestContext.Current.CancellationToken);
        Assert.Equal(ScrapeFailureKind.Removed, result.Failure!.Kind);
    }

    [Fact]
    public async Task Inactive_status_is_removed()
    {
        var api = Fixture("olx/petrol.api.json").Replace("\"status\":\"active\"", "\"status\":\"removed_by_user\"");
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.StartsWith("/api/") ? Ok(api) : Ok(Fixture("olx/petrol.page.html")));
        var result = await Scraper("petrol", "petrol", handler).FetchAsync(Url, TestContext.Current.CancellationToken);
        Assert.Equal(ScrapeFailureKind.Removed, result.Failure!.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, ScrapeFailureKind.Blocked)]
    [InlineData(HttpStatusCode.TooManyRequests, ScrapeFailureKind.Blocked)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ScrapeFailureKind.Unreachable)]
    public async Task Http_status_maps_to_failure_kind(HttpStatusCode status, ScrapeFailureKind expected)
    {
        var result = await Scraper("petrol", "petrol", new StubHandler(_ => Status(status))).FetchAsync(Url, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Failure!.Kind);
    }

    [Theory]
    [InlineData("\"sku\"", "\"skuX\"")]
    [InlineData("\"brand\"", "\"brandX\"")]
    [InlineData("\"@type\":\"Car\"", "\"@type\":\"Thing\"")]
    public async Task Missing_page_node_is_format_changed(string find, string replace)
    {
        var page = Fixture("olx/petrol.page.html").Replace(find, replace);
        var handler = new StubHandler(_ => Ok(page));
        var result = await Scraper("petrol", "petrol", handler).FetchAsync(Url, TestContext.Current.CancellationToken);
        Assert.Equal(ScrapeFailureKind.FormatChanged, result.Failure!.Kind);
    }

    [Theory]
    [InlineData("\"params\"", "\"paramsX\"")]
    [InlineData("\"key\":\"year\"", "\"key\":\"yearX\"")]
    [InlineData("\"key\":\"petrol\"", "\"key\":\"petrolX\"")]
    [InlineData("\"data\"", "\"dataX\"")]
    public async Task Missing_api_node_is_format_changed(string find, string replace)
    {
        var api = Fixture("olx/petrol.api.json").Replace(find, replace);
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.StartsWith("/api/") ? Ok(api) : Ok(Fixture("olx/petrol.page.html")));
        var result = await Scraper("petrol", "petrol", handler).FetchAsync(Url, TestContext.Current.CancellationToken);
        Assert.Equal(ScrapeFailureKind.FormatChanged, result.Failure!.Kind);
    }
}

public class NbpExchangeRateProviderTests
{
    [Fact]
    public async Task Rate_is_parsed_and_cached()
    {
        var handler = new StubHandler(_ => Ok(Fixture("nbp/eur.json")));
        var provider = new NbpExchangeRateProvider(Client(handler), new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System, NullLogger<NbpExchangeRateProvider>.Instance);

        var first = await provider.GetLatestAsync("EUR", TestContext.Current.CancellationToken);
        var second = await provider.GetLatestAsync("eur", TestContext.Current.CancellationToken);

        Assert.Equal(new ExchangeRate(4.3925m, new DateOnly(2026, 11, 6)), first);
        Assert.Equal(first, second);
        Assert.Single(handler.Requests);
        Assert.Equal("/api/exchangerates/rates/a/eur/", handler.Requests[0].AbsolutePath);
    }

    [Fact]
    public async Task Pln_needs_no_request()
    {
        var handler = new StubHandler(_ => Status(HttpStatusCode.InternalServerError));
        var provider = new NbpExchangeRateProvider(Client(handler), new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System, NullLogger<NbpExchangeRateProvider>.Instance);
        Assert.Equal(1m, (await provider.GetLatestAsync("PLN", TestContext.Current.CancellationToken))!.Rate);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("../../etc")]
    [InlineData("EU")]
    public async Task Invalid_currency_code_is_null_without_a_request(string code)
    {
        var handler = new StubHandler(_ => Ok(Fixture("nbp/eur.json")));
        var provider = new NbpExchangeRateProvider(Client(handler), new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System, NullLogger<NbpExchangeRateProvider>.Instance);
        Assert.Null(await provider.GetLatestAsync(code, TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Server_error_or_bad_payload_gives_null()
    {
        foreach (var respond in new Func<HttpRequestMessage, HttpResponseMessage>[] { _ => Status(HttpStatusCode.NotFound), _ => Ok("{}") })
        {
            var provider = new NbpExchangeRateProvider(Client(new StubHandler(respond)), new MemoryCache(new MemoryCacheOptions()),
                TimeProvider.System, NullLogger<NbpExchangeRateProvider>.Instance);
            Assert.Null(await provider.GetLatestAsync("EUR", TestContext.Current.CancellationToken));
        }
    }
}

