using CarComparer.Core.Listings;

namespace CarComparer.Core.Comparison;

public enum BetterDirection { None, Lower, Higher }

/// <summary>One listing in a comparison: the latest snapshot data, or the failure that prevented having any.</summary>
public sealed record ComparisonColumn
{
    public required Portal Portal { get; init; }
    public required string Url { get; init; }
    public ListingData? Data { get; init; }
    public decimal? PricePln { get; init; }
    public DateTimeOffset? FetchedAt { get; init; }
    public ScrapeFailureKind? Failure { get; init; }
    public string? FailureMessage { get; init; }
    public DateTimeOffset? InactiveSince { get; init; }
}

public sealed record CellValue(string Display, decimal? Comparable);

public sealed record AttributeContext(int CurrentYear);

public sealed record AttributeDefinition(
    string Key,
    string Label,
    string? Unit,
    BetterDirection Better,
    Func<ComparisonColumn, AttributeContext, CellValue?> Accessor);

public sealed record TableCell(string? Display, bool IsBest);

public sealed record TableRow(AttributeDefinition Attribute, IReadOnlyList<TableCell> Cells);

public sealed record ComparisonTable(IReadOnlyList<ComparisonColumn> Columns, IReadOnlyList<TableRow> Rows);
