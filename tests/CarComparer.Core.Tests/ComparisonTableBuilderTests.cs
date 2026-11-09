using CarComparer.Core.Comparison;
using CarComparer.Core.Listings;

namespace CarComparer.Core.Tests;

public class ComparisonTableBuilderTests
{
    private static ListingData Data(decimal price = 50_000, int year = 2020, int? km = 100_000, string cur = "PLN") => new()
    {
        Brand = "Kia", Model = "Ceed", Year = year, FuelType = FuelType.Petrol, Gearbox = Gearbox.Manual,
        MileageKm = km, Price = price, Currency = cur,
    };

    private static ComparisonColumn Col(ListingData? data, decimal? pln = null, ScrapeFailureKind? failure = null) => new()
    {
        Portal = Portal.Olx, Url = "https://www.olx.pl/d/oferta/x-ID1.html", Data = data, PricePln = pln ?? (data?.Currency == "PLN" ? data.Price : null),
        Failure = failure,
    };

    private static TableRow Row(ComparisonTable t, string key) => t.Rows.Single(r => r.Attribute.Key == key);

    [Fact]
    public void Lower_price_and_mileage_are_best_and_higher_year_is_best()
    {
        var t = ComparisonTableBuilder.Build([Col(Data(40_000, 2018, 150_000)), Col(Data(50_000, 2021, 90_000))], 2026);
        Assert.Equal([true, false], Row(t, "price").Cells.Select(c => c.IsBest));
        Assert.Equal([false, true], Row(t, "mileage").Cells.Select(c => c.IsBest));
        Assert.Equal([false, true], Row(t, "year").Cells.Select(c => c.IsBest));
    }

    [Fact]
    public void Ties_highlight_all_equal_values()
    {
        var t = ComparisonTableBuilder.Build([Col(Data(40_000)), Col(Data(40_000)), Col(Data(60_000))], 2026);
        Assert.Equal([true, true, false], Row(t, "price").Cells.Select(c => c.IsBest));
    }

    [Fact]
    public void Missing_values_do_not_take_part_and_single_value_gets_no_highlight()
    {
        var t = ComparisonTableBuilder.Build([Col(Data(km: null)), Col(Data(km: 80_000))], 2026);
        Assert.All(Row(t, "mileage").Cells, c => Assert.False(c.IsBest));
        Assert.Null(Row(t, "mileage").Cells[0].Display);
    }

    [Fact]
    public void Failed_column_is_empty_and_does_not_break_highlighting_of_the_rest()
    {
        var t = ComparisonTableBuilder.Build([Col(null, failure: ScrapeFailureKind.Removed), Col(Data(40_000)), Col(Data(45_000))], 2026);
        Assert.Equal([false, true, false], Row(t, "price").Cells.Select(c => c.IsBest));
        Assert.Null(Row(t, "price").Cells[0].Display);
    }

    [Fact]
    public void Price_is_compared_in_pln_across_currencies()
    {
        var eur = Col(Data(10_000, cur: "EUR"), pln: 43_925m);
        var pln = Col(Data(45_000));
        var t = ComparisonTableBuilder.Build([eur, pln], 2026);
        Assert.Equal([true, false], Row(t, "price").Cells.Select(c => c.IsBest));
        Assert.Contains("EUR", Row(t, "price").Cells[0].Display);
        Assert.Contains("PLN", Row(t, "price").Cells[0].Display);
    }

    [Fact]
    public void Foreign_price_without_rate_does_not_take_part()
    {
        var t = ComparisonTableBuilder.Build([Col(Data(10_000, cur: "EUR")), Col(Data(45_000))], 2026);
        Assert.All(Row(t, "price").Cells, c => Assert.False(c.IsBest));
    }

    [Fact]
    public void Km_per_year_uses_at_least_one_year()
    {
        var t = ComparisonTableBuilder.Build([Col(Data(year: 2026, km: 5_000)), Col(Data(year: 2016, km: 100_000))], 2026);
        var cells = Row(t, "km_per_year").Cells;
        Assert.StartsWith("5", cells[0].Display!.Replace("\u00a0", ""));
        Assert.True(cells[0].IsBest);Assert.False(cells[1].IsBest);
    }

    [Fact]
    public void Attributes_without_direction_never_highlight()
    {
        var t = ComparisonTableBuilder.Build([Col(Data()), Col(Data())], 2026);
        Assert.All(Row(t, "fuel").Cells, c => Assert.False(c.IsBest));
    }

    [Fact]
    public void Rows_follow_registry_order()
    {
        var t = ComparisonTableBuilder.Build([Col(Data()), Col(Data())], 2026);
        Assert.Equal(Attributes.All.Select(a => a.Key), t.Rows.Select(r => r.Attribute.Key));
    }
}

