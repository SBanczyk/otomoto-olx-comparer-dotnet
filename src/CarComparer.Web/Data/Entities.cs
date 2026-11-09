using CarComparer.Core.Listings;

namespace CarComparer.Web.Data;

public class Listing
{
    public int Id { get; set; }
    public Portal Portal { get; set; }
    /// <summary>The portal's ID code from the URL. A listing's identity is this, not the URL.</summary>
    public string ExternalId { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastFetchAttemptAt { get; set; }
    public ScrapeFailureKind? LastFetchFailure { get; set; }
    public string? LastFetchMessage { get; set; }
    public DateTimeOffset? InactiveSince { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<ListingSnapshot> Snapshots { get; set; } = [];
}

public class ListingSnapshot
{
    public int Id { get; set; }
    public int ListingId { get; set; }
    public Listing? Listing { get; set; }
    public DateTimeOffset FetchedAt { get; set; }

    public string Brand { get; set; } = "";
    public string Model { get; set; } = "";
    public int Year { get; set; }
    public FuelType FuelType { get; set; }
    public string? FuelTypeRaw { get; set; }
    public int? EngineCapacityCm3 { get; set; }
    public int? PowerHp { get; set; }
    public Gearbox Gearbox { get; set; }
    public string? GearboxRaw { get; set; }
    public int? MileageKm { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "PLN";
    public decimal? PricePln { get; set; }
    public decimal? ExchangeRate { get; set; }
    public DateOnly? ExchangeRateDate { get; set; }
    public BodyType? BodyType { get; set; }
    public string? BodyTypeRaw { get; set; }
    public Drive? Drive { get; set; }
    public string? DriveRaw { get; set; }
    public bool? IsDamaged { get; set; }
    public string? CountryOfOrigin { get; set; }
    /// <summary>Raw portal payload, so old snapshots can be re-parsed after a mapping fix.</summary>
    public string RawPayload { get; set; } = "";

    public ListingData ToListingData() => new()
    {
        Brand = Brand, Model = Model, Year = Year,
        FuelType = FuelType, FuelTypeRaw = FuelTypeRaw,
        EngineCapacityCm3 = EngineCapacityCm3, PowerHp = PowerHp,
        Gearbox = Gearbox, GearboxRaw = GearboxRaw,
        MileageKm = MileageKm, Price = Price, Currency = Currency,
        BodyType = BodyType, BodyTypeRaw = BodyTypeRaw,
        Drive = Drive, DriveRaw = DriveRaw,
        IsDamaged = IsDamaged, CountryOfOrigin = CountryOfOrigin,
    };

    public static ListingSnapshot From(ListingData d, string rawPayload, DateTimeOffset fetchedAt, ExchangeRate? rate) => new()
    {
        FetchedAt = fetchedAt,
        Brand = d.Brand, Model = d.Model, Year = d.Year,
        FuelType = d.FuelType, FuelTypeRaw = d.FuelTypeRaw,
        EngineCapacityCm3 = d.EngineCapacityCm3, PowerHp = d.PowerHp,
        Gearbox = d.Gearbox, GearboxRaw = d.GearboxRaw,
        MileageKm = d.MileageKm, Price = d.Price, Currency = d.Currency,
        PricePln = d.Currency == "PLN" ? d.Price : rate is null ? null : Math.Round(d.Price * rate.Rate, 2),
        ExchangeRate = d.Currency == "PLN" ? null : rate?.Rate,
        ExchangeRateDate = d.Currency == "PLN" ? null : rate?.TableDate,
        BodyType = d.BodyType, BodyTypeRaw = d.BodyTypeRaw,
        Drive = d.Drive, DriveRaw = d.DriveRaw,
        IsDamaged = d.IsDamaged, CountryOfOrigin = d.CountryOfOrigin,
        RawPayload = rawPayload,
    };
}

public class Comparison
{
    /// <summary>12 random characters, lower-case [a-z0-9] (SQL Server's default collation is case-insensitive).</summary>
    public string Id { get; set; } = "";
    public string? OwnerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<ComparisonItem> Items { get; set; } = [];
}

public class ComparisonItem
{
    public string ComparisonId { get; set; } = "";
    public Comparison? Comparison { get; set; }
    public int Position { get; set; }
    public int ListingId { get; set; }
    public Listing? Listing { get; set; }
}
