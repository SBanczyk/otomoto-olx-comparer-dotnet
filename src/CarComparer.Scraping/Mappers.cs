using CarComparer.Core.Listings;

namespace CarComparer.Scraping;

/// <summary>Maps portal-specific keys to the shared enums. Unknown keys become Other, with the portal label kept for display.</summary>
internal static class Mappers
{
    public static FuelType Fuel(string? key) => key switch
    {
        "petrol" => FuelType.Petrol,
        "diesel" => FuelType.Diesel,
        "lpg" or "petrol-lpg" => FuelType.Lpg,
        "cng" or "petrol-cng" => FuelType.Cng,
        "electric" => FuelType.Electric,
        "hybrid" => FuelType.Hybrid,
        "plugin-hybrid" => FuelType.PlugInHybrid,
        _ => FuelType.Other,
    };

    public static Gearbox Gearbox(string? key) => key switch
    {
        "manual" => Core.Listings.Gearbox.Manual,
        "automatic" => Core.Listings.Gearbox.Automatic,
        _ => Core.Listings.Gearbox.Other,
    };

    public static BodyType Body(string? key) => key switch
    {
        "sedan" => BodyType.Sedan,
        "combi" or "estate-car" => BodyType.Wagon,
        "suv" or "off-road-vehicle" => BodyType.Suv,
        "coupe" => BodyType.Coupe,
        "cabrio" or "convertible" => BodyType.Convertible,
        "compact" => BodyType.Compact,
        "minivan" => BodyType.Minivan,
        "city_car" or "city-car" or "small-cars" => BodyType.City,
        _ => BodyType.Other,
    };

    public static Drive Drive(string? key) => key switch
    {
        "front-wheel" => Core.Listings.Drive.Front,
        "rear-wheel" => Core.Listings.Drive.Rear,
        not null when key.StartsWith("all-wheel", StringComparison.Ordinal) => Core.Listings.Drive.AllWheel,
        _ => Core.Listings.Drive.Other,
    };

    public static string NormaliseCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) || currency.Trim() is "zł" or "zl" ? "PLN" : currency.Trim().ToUpperInvariant();
}

