using System.Text.RegularExpressions;
using CarComparer.Core.Listings;
using CarComparer.Scraping.Nbp;
using CarComparer.Scraping.Olx;
using CarComparer.Scraping.Otomoto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CarComparer.Scraping.Tests;

/// <summary>
/// Canary: runs against the live portals, never in the normal build (Category=Live). A failure message starts with
/// BLOCKED or FORMAT_CHANGED so the weekly workflow can tell "portal rejected us" from "portal changed its markup".
/// </summary>
[Trait("Category", "Live")]
public sealed partial class LiveTests : IDisposable
{
    private readonly ServiceProvider _services = new ServiceCollection()
        .AddLogging()
        .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
        .AddListingScraping()
        .BuildServiceProvider();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _services.Dispose();

    [GeneratedRegex(@"https://www\.otomoto\.pl/osobowe/oferta/[^""\\]+\.html")]
    private static partial Regex OtomotoOfferUrl();

    private static void AssertScraped(string portal, ScrapeResult result)
    {
        if (result.Failure is { } failure)
        {
            var label = failure.Kind switch
            {
                ScrapeFailureKind.Blocked => "BLOCKED",
                ScrapeFailureKind.FormatChanged => "FORMAT_CHANGED",
                _ => failure.Kind.ToString().ToUpperInvariant(),
            };
            Assert.Fail($"{label}: {portal} scrape failed ({failure.Kind}): {failure.Message}");
        }

        var data = result.Success!.Data;
        Assert.False(string.IsNullOrWhiteSpace(data.Brand), "Brand missing");
        Assert.False(string.IsNullOrWhiteSpace(data.Model), "Model missing");
        Assert.InRange(data.Year, 1900, DateTime.UtcNow.Year + 1);
        Assert.True(data.Price > 0, "Price missing");
        Assert.Equal(3, data.Currency.Length);
        Assert.NotEqual(FuelType.Other, data.FuelType);
        Assert.NotEqual(Gearbox.Other, data.Gearbox);
    }

    [Fact]
    public async Task Otomoto_first_listing_on_the_results_page_can_be_scraped()
    {
        var http = _services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OtomotoScraper));
        var page = await http.GetStringAsync("https://www.otomoto.pl/osobowe", Ct);
        var url = OtomotoOfferUrl().Match(page);
        Assert.True(url.Success, "FORMAT_CHANGED: no listing URL found on the OTOMOTO results page");

        var result = await _services.GetRequiredService<OtomotoScraper>().FetchAsync(new Uri(url.Value), Ct);

        AssertScraped("OTOMOTO", result);
    }

    [Fact]
    public async Task Olx_first_car_in_the_cars_category_can_be_scraped()
    {
        var http = _services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OlxScraper));
        var json = await http.GetStringAsync("https://www.olx.pl/api/v1/offers/?category_id=84&limit=1", Ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var url = doc.RootElement.GetProperty("data")[0].GetProperty("url").GetString();
        Assert.False(string.IsNullOrEmpty(url), "FORMAT_CHANGED: no listing URL in the OLX search response");

        var result = await _services.GetRequiredService<OlxScraper>().FetchAsync(new Uri(url!), Ct);

        AssertScraped("OLX", result);
    }

    [Fact]
    public async Task Nbp_returns_a_current_euro_rate()
    {
        var rate = await _services.GetRequiredService<NbpExchangeRateProvider>().GetLatestAsync("EUR", Ct);

        Assert.NotNull(rate);
        Assert.InRange(rate.Rate, 3m, 6m);
        Assert.True(rate.TableDate > DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10), "NBP table is more than 10 days old");
    }
}
