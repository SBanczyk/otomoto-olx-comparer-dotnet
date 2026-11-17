using System.Text.Json;
using AngleSharp.Html.Parser;
using CarComparer.Core.Listings;
using Microsoft.Extensions.Logging;

namespace CarComparer.Scraping.Otomoto;

/// <summary>Reads listing data from the JSON in &lt;script id="__NEXT_DATA__"&gt; (props.pageProps.advert), not from CSS classes.</summary>
public sealed class OtomotoScraper(HttpClient http, ILogger<OtomotoScraper> logger) : IListingScraper
{
    public Portal Portal => Portal.Otomoto;

    public bool CanHandle(Uri url) => ListingUrl.TryCreate(url.ToString(), out var u) && u!.Portal == Portal.Otomoto;

    public async Task<ScrapeResult> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        if (!ListingUrl.TryCreate(url.ToString(), out var listingUrl) || listingUrl!.Portal != Portal.Otomoto)
        {
            return ScrapeResult.Fail(ScrapeFailureKind.UnsupportedUrl, "Unsupported URL.");
        }

        var outcome = await ScraperHttp.GetAsync(http, listingUrl.Uri, "text/html", cancellationToken).ConfigureAwait(false);
        if (outcome.Failure is { } failure)
        {
            return ScrapeResult.Fail(failure.Kind, failure.Message);
        }

        try
        {
            return Parse(outcome.Body!, listingUrl.ExternalId);
        }
        catch (Exception ex) when (ex is FormatChangedException or JsonException)
        {
            logger.LogError(ex, "OTOMOTO format changed for {Url}", listingUrl.Uri);
            return ScrapeResult.Fail(ScrapeFailureKind.FormatChanged, ex.Message);
        }
    }

    internal static ScrapeResult Parse(string html, string externalId)
    {
        var script = new HtmlParser().ParseDocument(html).QuerySelector("script#__NEXT_DATA__")?.TextContent
            ?? throw new FormatChangedException("__NEXT_DATA__ script not found.");

        using var doc = JsonDocument.Parse(script);
        var advert = doc.RootElement.Path("props", "pageProps", "advert")
            ?? throw new FormatChangedException("props.pageProps.advert not found.");

        var status = advert.Path("status").AsString();
        if (status is not null && !status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            return ScrapeResult.Fail(ScrapeFailureKind.Removed, "Listing is no longer active.");
        }

        var category = advert.Path("category", "code").AsString().Require("category.code");
        if (category != "osobowe")
        {
            return ScrapeResult.Fail(ScrapeFailureKind.NotPassengerCar, $"Category is '{category}'.");
        }

        (string? Key, string? Label) P(string name)
        {
            JsonElement? first = advert.Path("parametersDict", name, "values") is { ValueKind: JsonValueKind.Array } v && v.GetArrayLength() > 0
                ? v[0]
                : null;
            return (first.Path("value").AsString() is { Length: > 0 } k ? k : null, first.Path("label").AsString());
        }

        var fuel = P("fuel_type");
        var gearbox = P("gearbox");
        var body = P("body_type");
        var drive = P("transmission");
        var damaged = P("damaged").Key;

        var data = new ListingData
        {
            Brand = P("make").Label.Require("make"),
            Model = P("model").Label.Require("model"),
            Year = P("year").Key.AsInt() ?? throw new FormatChangedException("Missing year."),
            FuelType = Mappers.Fuel(fuel.Key.Require("fuel_type")),
            FuelTypeRaw = fuel.Label,
            EngineCapacityCm3 = P("engine_capacity").Key.AsInt(),
            PowerHp = P("engine_power").Key.AsInt(),
            Gearbox = Mappers.Gearbox(gearbox.Key.Require("gearbox")),
            GearboxRaw = gearbox.Label,
            MileageKm = P("mileage").Key.AsInt(),
            Price = advert.Path("price", "value").AsString().AsDecimal() ?? throw new FormatChangedException("Missing price.value."),
            Currency = Mappers.NormaliseCurrency(advert.Path("price", "currency").AsString().Require("price.currency")),
            BodyType = body.Key is null ? null : Mappers.Body(body.Key),
            BodyTypeRaw = body.Label,
            Drive = drive.Key is null ? null : Mappers.Drive(drive.Key),
            DriveRaw = drive.Label,
            IsDamaged = damaged is null ? null : damaged == "1",
            CountryOfOrigin = P("country_origin").Label,
        };

        return ScrapeResult.Ok(data, externalId, advert.GetRawText());
    }
}
