using System.Text.RegularExpressions;

namespace CarComparer.Core.Listings;

/// <summary>
/// The only way to produce a URL that may reach a scraper: https, exact host on the allow-list,
/// no credentials or custom port, query and fragment stripped.
/// </summary>
public sealed partial record ListingUrl
{
    private static readonly Dictionary<string, Portal> AllowedHosts = new(StringComparer.Ordinal)
    {
        ["otomoto.pl"] = Portal.Otomoto,
        ["www.otomoto.pl"] = Portal.Otomoto,
        ["olx.pl"] = Portal.Olx,
        ["www.olx.pl"] = Portal.Olx,
    };

    private ListingUrl(Uri uri, Portal portal, string externalId)
    {
        Uri = uri;
        Portal = portal;
        ExternalId = externalId;
    }

    public Uri Uri { get; }
    public Portal Portal { get; }
    /// <summary>The portal's ID code taken from the URL ("ID6IjsIM"), identical for the same advert under any URL.</summary>
    public string ExternalId { get; }

    public static bool TryCreate(string? input, out ListingUrl? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(input) || input.Length > 2048)
        {
            return false;
        }

        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort
            || !AllowedHosts.TryGetValue(uri.Host, out var portal))
        {
            return false;
        }

        var match = IdPattern().Match(uri.AbsolutePath);
        if (!match.Success)
        {
            return false;
        }

        var normalised = new UriBuilder(Uri.UriSchemeHttps, uri.Host.ToLowerInvariant(), -1, uri.AbsolutePath).Uri;
        result = new ListingUrl(normalised, portal, match.Groups[1].Value);
        return true;
    }

    [GeneratedRegex(@"-(ID[A-Za-z0-9]+)\.html$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
