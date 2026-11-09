using System.Globalization;
using System.Text.Json;
using CarComparer.Core.Listings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CarComparer.Scraping.Nbp;

/// <summary>
/// Latest NBP table A mid rate (GET /api/exchangerates/rates/a/{code}/). NBP publishes only on working days,
/// so the endpoint's "latest" is used rather than today's date. Cached until the end of the (UTC) day.
/// </summary>
public sealed class NbpExchangeRateProvider(HttpClient http, IMemoryCache cache, TimeProvider time, ILogger<NbpExchangeRateProvider> logger)
    : IExchangeRateProvider
{
    public async Task<ExchangeRate?> GetLatestAsync(string currency, CancellationToken cancellationToken)
    {
        var code = currency.Trim().ToLowerInvariant();
        if (code is "pln" or "" || code.Length != 3 || !code.All(char.IsAsciiLetter))
        {
            return code == "pln" ? new ExchangeRate(1m, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)) : null;
        }

        if (cache.TryGetValue(code, out ExchangeRate? cached))
        {
            return cached;
        }

        try
        {
            var body = await http.GetStringAsync($"https://api.nbp.pl/api/exchangerates/rates/a/{code}/?format=json", cancellationToken)
                .ConfigureAwait(false);
            var rate = Parse(body);
            var now = time.GetUtcNow();
            cache.Set(code, rate, now.Date.AddDays(1) - now.UtcDateTime);
            return rate;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not get NBP rate for {Currency}", code);
            return null;
        }
    }

    internal static ExchangeRate Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var rate = doc.RootElement.GetProperty("rates")[0];
        return new ExchangeRate(
            rate.GetProperty("mid").GetDecimal(),
            DateOnly.ParseExact(rate.GetProperty("effectiveDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}
