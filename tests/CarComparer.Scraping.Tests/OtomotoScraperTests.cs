using System.Net;
using CarComparer.Core.Listings;
using CarComparer.Scraping.Otomoto;
using Microsoft.Extensions.Logging.Abstractions;
using static CarComparer.Scraping.Tests.TestSupport;

namespace CarComparer.Scraping.Tests;

public class OtomotoScraperTests
{
    private static readonly Uri Url = new("https://www.otomoto.pl/osobowe/oferta/kia-ceed-ID6IdSID.html");

    private static OtomotoScraper Scraper(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(Client(new StubHandler(respond)), NullLogger<OtomotoScraper>.Instance);

    [Fact]
    public async Task Petrol_listing_is_parsed()
    {
        var result = await Scraper(_ => Ok(Fixture("otomoto/petrol.html"))).FetchAsync(Url, TestContext.Current.CancellationToken);

        var success = Assert.IsType<ScrapeSuccess>(result.Success);
        Assert.Equal("ID6IdSID", success.ExternalId);
        var d = success.Data;
        Assert.Equal(("Kia", "Ceed", 2023), (d.Brand, d.Model, d.Year));
        Assert.Equal(FuelType.Petrol, d.FuelType);
        Assert.Equal(Gearbox.Manual, d.Gearbox);
        Assert.Equal(BodyType.Wagon, d.BodyType);
        Assert.Equal(Drive.Front, d.Drive);
        Assert.Equal(1482, d.EngineCapacityCm3);
        Assert.Equal(160, d.PowerHp);
        Assert.Equal(97760, d.MileageKm);
        Assert.Equal((65850m, "PLN"), (d.Price, d.Currency));
        Assert.False(d.IsDamaged);
        Assert.Equal("Polska", d.CountryOfOrigin);
        Assert.False(string.IsNullOrEmpty(success.RawPayload));
    }

    [Fact]
    public async Task Diesel_listing_is_parsed_with_all_wheel_drive()
    {
        var d = (await Scraper(_ => Ok(Fixture("otomoto/diesel.html"))).FetchAsync(Url, TestContext.Current.CancellationToken)).Success!.Data;
        Assert.Equal(FuelType.Diesel, d.FuelType);
        Assert.Equal(Gearbox.Automatic, d.Gearbox);
        Assert.Equal(Drive.AllWheel, d.Drive);
        Assert.Equal(260000, d.MileageKm);
    }

    [Fact]
    public async Task Electric_listing_has_no_engine_capacity()
    {
        var d = (await Scraper(_ => Ok(Fixture("otomoto/electric.html"))).FetchAsync(Url, TestContext.Current.CancellationToken)).Success!.Data;
        Assert.Equal(FuelType.Electric, d.FuelType);
        Assert.Null(d.EngineCapacityCm3);
        Assert.Equal(BodyType.Suv, d.BodyType);
    }

    [Fact]
    public async Task Foreign_currency_is_kept_as_iso_code()
    {
        var html = Fixture("otomoto/petrol.html").Replace("\"currency\":\"PLN\"", "\"currency\":\"EUR\"");
        var d = (await Scraper(_ => Ok(html)).FetchAsync(Url, TestContext.Current.CancellationToken)).Success!.Data;
        Assert.Equal("EUR", d.Currency);
    }

    [Fact]
    public async Task Non_passenger_car_is_rejected()
    {
        var result = await Scraper(_ => Ok(Fixture("otomoto/truck.html"))).FetchAsync(Url, TestContext.Current.CancellationToken);
        Assert.Equal(ScrapeFailureKind.NotPassengerCar, result.Failure!.Kind);
    }

    [Fact]
    public async Task Inactive_advert_is_removed()
    {
        var html = Fixture("otomoto/petrol.html").Replace("\"status\":\"ACTIVE\"", "\"status\":\"CLOSED\"");
        var result = await Scraper(_ => Ok(html)).FetchAsync(Url, TestContext.Current.CancellationToken);
        Assert.Equal(ScrapeFailureKind.Removed, result.Failure!.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ScrapeFailureKind.Removed)]
    [InlineData(HttpStatusCode.Gone, ScrapeFailureKind.Removed)]
    [InlineData(HttpStatusCode.Forbidden, ScrapeFailureKind.Blocked)]
    [InlineData(HttpStatusCode.TooManyRequests, ScrapeFailureKind.Blocked)]
    [InlineData(HttpStatusCode.BadGateway, ScrapeFailureKind.Unreachable)]
    public async Task Http_status_maps_to_failure_kind(HttpStatusCode status, ScrapeFailureKind expected)
    {
        var result = await Scraper(_ => Status(status)).FetchAsync(Url, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Failure!.Kind);
    }

    [Fact]
    public async Task Network_error_is_unreachable()
    {
        var result = await Scraper(_ => throw new HttpRequestException("boom")).FetchAsync(Url, TestContext.Current.CancellationToken);
        Assert.Equal(ScrapeFailureKind.Unreachable, result.Failure!.Kind);
    }

    [Fact]
    public async Task Timeout_is_unreachable_but_caller_cancellation_propagates()
    {
        var timeout = await Scraper(_ => throw new TaskCanceledException("timeout")).FetchAsync(Url, TestContext.Current.CancellationToken);
        Assert.Equal(ScrapeFailureKind.Unreachable, timeout.Failure!.Kind);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Scraper(_ => throw new TaskCanceledException()).FetchAsync(Url, cts.Token));
    }

    [Theory]
    [InlineData("\"parametersDict\"", "\"parametersDictX\"")]
    [InlineData("\"advert\"", "\"advertX\"")]
    [InlineData("\"__NEXT_DATA__\"", "\"other\"")]
    [InlineData("\"fuel_type\"", "\"fuel_typeX\"")]
    public async Task Missing_node_is_format_changed(string find, string replace)
    {
        var html = Fixture("otomoto/petrol.html").Replace(find, replace);
        var result = await Scraper(_ => Ok(html)).FetchAsync(Url, TestContext.Current.CancellationToken);
        Assert.Equal(ScrapeFailureKind.FormatChanged, result.Failure!.Kind);
    }

    [Fact]
    public async Task Invalid_json_is_format_changed()
    {
        var html = "<html><script id=\"__NEXT_DATA__\">{not json</script></html>";
        var result = await Scraper(_ => Ok(html)).FetchAsync(Url, TestContext.Current.CancellationToken);
        Assert.Equal(ScrapeFailureKind.FormatChanged, result.Failure!.Kind);
    }

    [Fact]
    public async Task Foreign_host_is_rejected_without_a_request()
    {
        var handler = new StubHandler(_ => Ok("x"));
        var scraper = new OtomotoScraper(Client(handler), NullLogger<OtomotoScraper>.Instance);
        var result = await scraper.FetchAsync(new Uri("https://evil.com/otomoto/oferta/a-ID1.html"), TestContext.Current.CancellationToken);
        Assert.Equal(ScrapeFailureKind.UnsupportedUrl, result.Failure!.Kind);
        Assert.Empty(handler.Requests);
    }
}

