using System.Globalization;
using System.Text.Json;

namespace CarComparer.Scraping;

/// <summary>Raised inside a parser when an expected node is missing; mapped to FormatChanged.</summary>
internal sealed class FormatChangedException(string message) : Exception(message);

internal static class JsonExtensions
{
    public static JsonElement? Path(this JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.Null ? null : current;
    }

    public static JsonElement? Path(this JsonElement? element, params string[] path) =>
        element?.Path(path);

    public static string? AsString(this JsonElement? element) => element?.ValueKind switch
    {
        JsonValueKind.String => element.Value.GetString(),
        JsonValueKind.Number => element.Value.GetRawText(),
        _ => null,
    };

    public static int? AsInt(this string? s) =>
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? (int)Math.Round(d) : null;

    public static decimal? AsDecimal(this string? s) =>
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    public static string Require(this string? value, string what) =>
        string.IsNullOrWhiteSpace(value) ? throw new FormatChangedException($"Missing {what}.") : value;
}
