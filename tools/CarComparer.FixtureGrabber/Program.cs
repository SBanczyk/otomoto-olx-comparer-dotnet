using CarComparer.Core.Listings;
using CarComparer.Scraping;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Usage: dotnet run --project tools/CarComparer.FixtureGrabber -- <url> [--name <name>] [--parse]
// Saves the raw response using the same HttpClient configuration as the app.
if (args.Length == 0 || !ListingUrl.TryCreate(args[0], out var url))
{
    Console.Error.WriteLine("Usage: FixtureGrabber <listing url> [--name <name>] [--parse]");
    return 1;
}

var name = args.SkipWhile(a => a != "--name").Skip(1).FirstOrDefault() ?? url!.ExternalId;
var parse = args.Contains("--parse");

using var services = new ServiceCollection()
    .AddLogging()
    .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
    .AddListingScraping()
    .BuildServiceProvider();

var root = FindRepoRoot() ?? Directory.GetCurrentDirectory();
var fixtures = Path.Combine(root, "tests", "CarComparer.Scraping.Tests", "Fixtures");
var http = services.GetRequiredService<IHttpClientFactory>()
    .CreateClient(url!.Portal == Portal.Otomoto ? nameof(CarComparer.Scraping.Otomoto.OtomotoScraper) : nameof(CarComparer.Scraping.Olx.OlxScraper));

if (url.Portal == Portal.Otomoto)
{
    var html = await http.GetStringAsync(url.Uri);
    var path = Path.Combine(fixtures, "otomoto", name + ".html");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllTextAsync(path, html);
    Console.WriteLine($"Saved {path}");
}
else
{
    var page = await http.GetStringAsync(url.Uri);
    var pagePath = Path.Combine(fixtures, "olx", name + ".page.html");
    Directory.CreateDirectory(Path.GetDirectoryName(pagePath)!);
    await File.WriteAllTextAsync(pagePath, page);
    var sku = System.Text.RegularExpressions.Regex.Match(page, "\"sku\"\\s*:\\s*\"(\\d+)\"").Groups[1].Value;
    var api = await http.GetStringAsync($"https://{url.Uri.Host}/api/v1/offers/{sku}/");
    var apiPath = Path.Combine(fixtures, "olx", name + ".api.json");
    await File.WriteAllTextAsync(apiPath, api);
    Console.WriteLine($"Saved {pagePath} and {apiPath}");
}

if (parse)
{
    var scraper = services.GetServices<IListingScraper>().First(s => s.CanHandle(url.Uri));
    var result = await scraper.FetchAsync(url.Uri, CancellationToken.None);
    Console.WriteLine(result.Success is { } s
        ? System.Text.Json.JsonSerializer.Serialize(s.Data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })
        : $"Failed: {result.Failure!.Kind}: {result.Failure.Message}");
}

return 0;

static string? FindRepoRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "CarComparer.sln")))
        {
            return dir.FullName;
        }
    }

    return null;
}
