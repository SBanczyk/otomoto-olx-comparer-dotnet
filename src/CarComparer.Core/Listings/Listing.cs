namespace CarComparer.Core.Listings;

/// <summary>Normalised listing attributes, identical for both portals. Raw portal values are kept for display when the enum is Other.</summary>
public sealed record ListingData
{
    public required string Brand { get; init; }
    public required string Model { get; init; }
    public required int Year { get; init; }
    public required FuelType FuelType { get; init; }
    public string? FuelTypeRaw { get; init; }
    public int? EngineCapacityCm3 { get; init; }
    public int? PowerHp { get; init; }
    public required Gearbox Gearbox { get; init; }
    public string? GearboxRaw { get; init; }
    public int? MileageKm { get; init; }
    public required decimal Price { get; init; }
    /// <summary>ISO 4217 code; "zl" is normalised to PLN.</summary>
    public required string Currency { get; init; }
    public BodyType? BodyType { get; init; }
    public string? BodyTypeRaw { get; init; }
    public Drive? Drive { get; init; }
    public string? DriveRaw { get; init; }
    public bool? IsDamaged { get; init; }
    public string? CountryOfOrigin { get; init; }
}

public sealed record ScrapeSuccess(ListingData Data, string ExternalId, string RawPayload);

public sealed record ScrapeFailure(ScrapeFailureKind Kind, string Message);

public sealed class ScrapeResult
{
    private ScrapeResult(ScrapeSuccess? success, ScrapeFailure? failure)
    {
        Success = success;
        Failure = failure;
    }

    public ScrapeSuccess? Success { get; }
    public ScrapeFailure? Failure { get; }
    public bool IsSuccess => Success is not null;

    public static ScrapeResult Ok(ListingData data, string externalId, string rawPayload) =>
        new(new ScrapeSuccess(data, externalId, rawPayload), null);

    public static ScrapeResult Fail(ScrapeFailureKind kind, string message) =>
        new(null, new ScrapeFailure(kind, message));
}

public interface IListingScraper
{
    Portal Portal { get; }
    bool CanHandle(Uri url);
    Task<ScrapeResult> FetchAsync(Uri url, CancellationToken cancellationToken);
}

public sealed record ExchangeRate(decimal Rate, DateOnly TableDate);

public interface IExchangeRateProvider
{
    /// <summary>Latest NBP mid rate for the currency to PLN, or null when unavailable.</summary>
    Task<ExchangeRate?> GetLatestAsync(string currency, CancellationToken cancellationToken);
}
