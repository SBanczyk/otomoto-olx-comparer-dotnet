namespace CarComparer.Core.Comparison;

public static class ComparisonTableBuilder
{
    /// <summary>
    /// Builds the table. "Best" is flagged per row: ties flag all equal values, missing values do not take part,
    /// and a row with fewer than two comparable values gets no highlight.
    /// </summary>
    public static ComparisonTable Build(IReadOnlyList<ComparisonColumn> columns, int currentYear)
    {
        var context = new AttributeContext(currentYear);
        var rows = new List<TableRow>(Attributes.All.Count);

        foreach (var attribute in Attributes.All)
        {
            var values = columns.Select(c => attribute.Accessor(c, context)).ToList();
            var best = BestValue(attribute.Better, values);
            var cells = values
                .Select(v => new TableCell(v?.Display, best is { } b && v?.Comparable == b))
                .ToList();
            rows.Add(new TableRow(attribute, cells));
        }

        return new ComparisonTable(columns, rows);
    }

    private static decimal? BestValue(BetterDirection direction, List<CellValue?> values)
    {
        if (direction == BetterDirection.None)
        {
            return null;
        }

        var comparable = values.Where(v => v?.Comparable is not null).Select(v => v!.Comparable!.Value).ToList();
        if (comparable.Count < 2)
        {
            return null;
        }

        return direction == BetterDirection.Lower ? comparable.Min() : comparable.Max();
    }
}
