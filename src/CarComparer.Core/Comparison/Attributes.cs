using System.Globalization;
using CarComparer.Core.Listings;

namespace CarComparer.Core.Comparison;

/// <summary>Single registry of every compared attribute; table row order follows this list.</summary>
public static class Attributes
{
    private static readonly CultureInfo Pl = new("pl-PL");

    private static readonly Dictionary<FuelType, string> FuelLabels = new()
    {
        [FuelType.Petrol] = "Benzyna", [FuelType.Diesel] = "Diesel", [FuelType.Lpg] = "Benzyna + LPG",
        [FuelType.Electric] = "Elektryczny", [FuelType.Hybrid] = "Hybryda",
        [FuelType.PlugInHybrid] = "Hybryda plug-in", [FuelType.Cng] = "Benzyna + CNG",
    };

    private static readonly Dictionary<Gearbox, string> GearboxLabels = new()
    {
        [Gearbox.Manual] = "Manualna", [Gearbox.Automatic] = "Automatyczna",
    };

    private static readonly Dictionary<BodyType, string> BodyLabels = new()
    {
        [BodyType.City] = "Miejski", [BodyType.Sedan] = "Sedan", [BodyType.Wagon] = "Kombi",
        [BodyType.Hatchback] = "Hatchback", [BodyType.Coupe] = "Coupe", [BodyType.Convertible] = "Kabriolet",
        [BodyType.Suv] = "SUV", [BodyType.Minivan] = "Minivan", [BodyType.Compact] = "Kompakt",
    };

    private static readonly Dictionary<Drive, string> DriveLabels = new()
    {
        [Drive.Front] = "Przedni", [Drive.Rear] = "Tylny", [Drive.AllWheel] = "4x4",
    };

    public static IReadOnlyList<AttributeDefinition> All { get; } =
    [
        Text("brand", "Marka", d => d.Brand),
        Text("model", "Model", d => d.Model),
        new("year", "Rok produkcji", null, BetterDirection.Higher,
            (c, _) => c.Data is { } d ? new CellValue(d.Year.ToString(Pl), d.Year) : null),
        new("price", "Cena", "PLN", BetterDirection.Lower, (c, _) => Price(c)),
        new("mileage", "Przebieg", "km", BetterDirection.Lower,
            (c, _) => c.Data?.MileageKm is { } m ? new CellValue($"{m.ToString("N0", Pl)} km", m) : null),
        new("km_per_year", "Przebieg na rok", "km/rok", BetterDirection.Lower, (c, ctx) => KmPerYear(c, ctx)),
        Text("fuel", "Paliwo", d => EnumText(d.FuelType, d.FuelTypeRaw, FuelLabels)),
        new("engine", "Pojemnosc silnika", "cm³", BetterDirection.None,
            (c, _) => c.Data?.EngineCapacityCm3 is { } e ? new CellValue($"{e.ToString("N0", Pl)} cm³", e) : null),
        new("power", "Moc", "KM", BetterDirection.None,
            (c, _) => c.Data?.PowerHp is { } p ? new CellValue($"{p} KM", p) : null),
        Text("gearbox", "Skrzynia biegów", d => EnumText(d.Gearbox, d.GearboxRaw, GearboxLabels)),
        Text("body", "Nadwozie", d => d.BodyType is { } b ? EnumText(b, d.BodyTypeRaw, BodyLabels) : null),
        Text("drive", "Naped", d => d.Drive is { } v ? EnumText(v, d.DriveRaw, DriveLabels) : null),
        Text("damaged", "Uszkodzony", d => d.IsDamaged is { } b ? (b ? "Tak" : "Nie") : null),
        Text("origin", "Kraj pochodzenia", d => d.CountryOfOrigin),
    ];

    private static AttributeDefinition Text(string key, string label, Func<ListingData, string?> get) =>
        new(key, label, null, BetterDirection.None,
            (c, _) => c.Data is { } d && get(d) is { Length: > 0 } s ? new CellValue(s, null) : null);

    private static CellValue? Price(ComparisonColumn c)
    {
        if (c.Data is not { } d)
        {
            return null;
        }

        var original = $"{d.Price.ToString("N0", Pl)} {d.Currency}";
        if (d.Currency == "PLN")
        {
            return new CellValue(original, d.Price);
        }

        return c.PricePln is { } pln
            ? new CellValue($"{original} (˜ {pln.ToString("N0", Pl)} PLN)", pln)
            : new CellValue(original, null);
    }

    private static CellValue? KmPerYear(ComparisonColumn c, AttributeContext ctx)
    {
        if (c.Data is not { MileageKm: { } km } d)
        {
            return null;
        }

        var perYear = Math.Round((decimal)km / Math.Max(1, ctx.CurrentYear - d.Year), 0);
        return new CellValue($"{perYear.ToString("N0", Pl)} km/rok", perYear);
    }

    private static string EnumText<T>(T value, string? raw, IReadOnlyDictionary<T, string> labels) where T : struct, Enum =>
        value.Equals(default(T)) ? raw ?? "Inne" : labels[value];

}

