using System.Text.Json;
using AngleSharp.Html.Parser;
using CarComparer.Core.Listings;
using Microsoft.Extensions.Logging;

namespace CarComparer.Scraping.Olx;

/// <summary>
/// Two steps: the listing page (JSON-LD gives the numeric id as "sku", the make and the category path),
/// then the JSON API /api/v1/offers/{sku}/ for the parameters. The ID code in the URL is not accepted by the API.
/// </summary>
public sealed class OlxScraper(HttpClient http, ILogger<OlxScraper> logger) : IListingScraper
{
    private const string PassengerCarPath = "/motoryzacja/samochody";

    public Portal Portal => Portal.Olx;

    public bool CanHandle(Uri url) => ListingUrl.TryCreate(url.ToString(), out var u) && u!.Portal == Portal.Olx;

    public async Task<ScrapeResult> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        if (!ListingUrl.TryCreate(url.ToString(), out var listingUrl) || listingUrl!.Portal != Portal.Olx)
        {
            return ScrapeResult.Fail(ScrapeFailureKind.UnsupportedUrl, "Unsupported URL.");
        }

        var page = await ScraperHttp.GetAsync(http, listingUrl.Uri, "text/html", cancellationToken).ConfigureAwait(false);
        if (page.Failure is { } pageFailure)
        {
            return ScrapeResult.Fail(pageFailure.Kind, pageFailure.Message);
        }

        try
        {
            var meta = ParsePage(page.Body!);
            if (!meta.IsPassengerCar)
            {
                return ScrapeResult.Fail(ScrapeFailureKind.NotPassengerCar, $"Category is '{meta.CategoryPath}'.");
            }

            var apiUri = new Uri($"https://{listingUrl.Uri.Host}/api/v1/offers/{meta.Sku}/");
            var api = await ScraperHttp.GetAsync(http, apiUri, "application/json", cancellationToken).ConfigureAwait(false);
            if (api.Failure is { } apiFailure)
            {
                return ScrapeResult.Fail(apiFailure.Kind, apiFailure.Message);
            }

            return ParseApi(api.Body!, meta.Brand, listingUrl.ExternalId);
        }
        catch (Exception ex) when (ex is FormatChangedException or JsonException)
        {
            logger.LogError(ex, "OLX format changed for {Url}", listingUrl.Uri);
            return ScrapeResult.Fail(ScrapeFailureKind.FormatChanged, ex.Message);
        }
    }

    internal sealed record PageMeta(string Sku, string Brand, string CategoryPath)
    {
        public bool IsPassengerCar => CategoryPath.StartsWith(PassengerCarPath, StringComparison.Ordinal);
    }

    internal static PageMeta ParsePage(string html)
    {
        var scripts = new HtmlParser().ParseDocument(html).QuerySelectorAll("script[type='application/ld+json']");
        foreach (var script in scripts)
        {
            using var doc = JsonDocument.Parse(script.TextContent);
            var root = doc.RootElement;
            if (root.Path("@type").AsString() != "Car")
            {
                continue;
            }

            var category = root.Path("category").AsString().Require("category");
            var path = Uri.TryCreate(category, UriKind.Absolute, out var uri) ? uri.AbsolutePath : category;
            return new PageMeta(
                root.Path("sku").AsString().Require("sku"),
                root.Path("brand", "name").AsString().Require("brand.name"),
                path);
        }

        throw new FormatChangedException("JSON-LD block of type Car not found.");
    }

    internal static ScrapeResult ParseApi(string json, string brand, string externalId)
    {
        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.Path("data") ?? throw new FormatChangedException("data node not found.");

        var status = data.Path("status").AsString();
        if (status is not null && !status.Equals("active", StringComparison.OrdinalIgnoreCase))
        {
            return ScrapeResult.Fail(ScrapeFailureKind.Removed, $"Listing status is '{status}'.");
        }

        var parameters = new Dictionary<string, JsonElement>();
        if (data.Path("params") is { ValueKind: JsonValueKind.Array } array)
        {
            foreach (var p in array.EnumerateArray())
            {
                if (p.Path("key").AsString() is { } key)
                {
                    parameters[key] = p;
                }
            }
        }

        (string? Key, string? Label) P(string name) =>
            parameters.TryGetValue(name, out var p)
                ? (p.Path("value", "key").AsString() is { Length: > 0 } k ? k : null, p.Path("value", "label").AsString()?.Trim())
                : (null, null);

        var price = parameters.GetValueOrDefault("price");
        var fuel = P("petrol");
        var gearbox = P("transmission");
        var body = P("car_body");
        var drive = P("drive");
        var condition = P("condition").Key;

        var result = new ListingData
        {
            Brand = brand,
            Model = P("model").Label.Require("model"),
            Year = P("year").Key.AsInt() ?? throw new FormatChangedException("Missing year."),
            FuelType = Mappers.Fuel(fuel.Key.Require("petrol (fuel)")),
            FuelTypeRaw = fuel.Label,
            EngineCapacityCm3 = P("enginesize").Key.AsInt(),
            PowerHp = P("enginepower").Key.AsInt(),
            Gearbox = Mappers.Gearbox(gearbox.Key.Require("transmission (gearbox)")),
            GearboxRaw = gearbox.Label,
            // OLX spells this key "milage".
            MileageKm = P("milage").Key.AsInt(),
            Price = price.ValueKind == JsonValueKind.Undefined
                ? throw new FormatChangedException("Missing price param.")
                : price.Path("value", "value").AsString().AsDecimal() ?? throw new FormatChangedException("Missing price value."),
            Currency = Mappers.NormaliseCurrency(price.Path("value", "currency").AsString()),
            BodyType = body.Key is null ? null : Mappers.Body(body.Key),
            BodyTypeRaw = body.Label,
            Drive = drive.Key is null ? null : Mappers.Drive(drive.Key),
            DriveRaw = drive.Label,
            IsDamaged = condition is null ? null : condition == "damaged",
        };

        return ScrapeResult.Ok(result, externalId, json);
    }
}
