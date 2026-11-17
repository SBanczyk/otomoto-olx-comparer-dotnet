namespace CarComparer.Scraping;

public sealed class ScrapingOptions
{
    public const string SectionName = "Scraping";

    public string UserAgent { get; set; } = "Mozilla/5.0 (compatible; CarComparer/1.0; +https://github.com/SBanczyk/otomoto-olx-comparer-dotnet)";
    public TimeSpan MinRefreshInterval { get; set; } = TimeSpan.FromMinutes(10);
    public int MaxConcurrentRequests { get; set; } = 4;
}
