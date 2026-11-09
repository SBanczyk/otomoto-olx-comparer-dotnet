using System.Net;
using CarComparer.Core.Listings;
using CarComparer.Scraping.Nbp;
using CarComparer.Scraping.Olx;
using CarComparer.Scraping.Otomoto;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly.Timeout;

namespace CarComparer.Scraping;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddListingScraping(this IServiceCollection services, Action<ScrapingOptions>? configure = null)
    {
        var options = services.AddOptions<ScrapingOptions>().BindConfiguration(ScrapingOptions.SectionName);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);

        Configure(services.AddHttpClient<OtomotoScraper>());
        Configure(services.AddHttpClient<OlxScraper>());
        Configure(services.AddHttpClient<NbpExchangeRateProvider>());

        services.AddTransient<IListingScraper>(sp => sp.GetRequiredService<OtomotoScraper>());
        services.AddTransient<IListingScraper>(sp => sp.GetRequiredService<OlxScraper>());
        services.AddTransient<IExchangeRateProvider>(sp => sp.GetRequiredService<NbpExchangeRateProvider>());
        return services;
    }

    private static void Configure(IHttpClientBuilder builder)
    {
        builder.ConfigureHttpClient((sp, client) =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(sp.GetRequiredService<IOptions<ScrapingOptions>>().Value.UserAgent);
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("pl-PL,pl;q=0.9");
        });
        builder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        });
        builder.AddStandardResilienceHandler(o =>
        {
            // Retry transient failures only; never retry 403/429, which would hammer a portal that is blocking us.
            o.Retry.MaxRetryAttempts = 2;
            o.Retry.ShouldHandle = args => ValueTask.FromResult(args.Outcome switch
            {
                { Exception: HttpRequestException or TimeoutRejectedException } => true,
                { Result.StatusCode: >= HttpStatusCode.InternalServerError or HttpStatusCode.RequestTimeout } => true,
                _ => false,
            });
        });
    }
}

