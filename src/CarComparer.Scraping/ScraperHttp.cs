using System.Net;
using CarComparer.Core.Listings;

namespace CarComparer.Scraping;

internal sealed record HttpOutcome(string? Body, ScrapeFailure? Failure);

internal static class ScraperHttp
{
    /// <summary>GET with the portal-agnostic mapping of HTTP outcomes to failure kinds.</summary>
    public static async Task<HttpOutcome> GetAsync(HttpClient client, Uri uri, string accept, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("Accept", accept);
            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return new HttpOutcome(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), null);
            }

            return new HttpOutcome(null, response.StatusCode switch
            {
                HttpStatusCode.NotFound or HttpStatusCode.Gone => new(ScrapeFailureKind.Removed, "Listing not found."),
                HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests => new(ScrapeFailureKind.Blocked, $"Portal answered {(int)response.StatusCode}."),
                _ => new(ScrapeFailureKind.Unreachable, $"Portal answered {(int)response.StatusCode}."),
            });
        }
        catch (HttpRequestException ex)
        {
            return new HttpOutcome(null, new(ScrapeFailureKind.Unreachable, ex.Message));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new HttpOutcome(null, new(ScrapeFailureKind.Unreachable, "Request timed out."));
        }
        catch (Polly.Timeout.TimeoutRejectedException)
        {
            return new HttpOutcome(null, new(ScrapeFailureKind.Unreachable, "Request timed out."));
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException)
        {
            // Too many recent failures from this portal: treat as it rejecting us rather than crashing the request.
            return new HttpOutcome(null, new(ScrapeFailureKind.Blocked, "Too many recent failures; requests are paused."));
        }
        catch (Polly.RateLimiting.RateLimiterRejectedException)
        {
            return new HttpOutcome(null, new(ScrapeFailureKind.Blocked, "Too many concurrent requests."));
        }
    }
}
