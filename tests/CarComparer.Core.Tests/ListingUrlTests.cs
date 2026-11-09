using CarComparer.Core.Listings;

namespace CarComparer.Core.Tests;

public class ListingUrlTests
{
    [Theory]
    [InlineData("https://www.otomoto.pl/osobowe/oferta/kia-ev6-ID6IjsIM.html", Portal.Otomoto, "ID6IjsIM")]
    [InlineData("https://otomoto.pl/osobowe/oferta/kia-ev6-ID6IjsIM.html", Portal.Otomoto, "ID6IjsIM")]
    [InlineData("https://WWW.OLX.PL/d/oferta/bmw-535d-biturbo-CID5-ID1cXjgm.html?reason=x#frag", Portal.Olx, "ID1cXjgm")]
    [InlineData("https://olx.pl/d/oferta/bmw-CID5-ID1cXjgm.html", Portal.Olx, "ID1cXjgm")]
    public void Accepts_allowed_hosts_and_normalises(string input, Portal portal, string id)
    {
        Assert.True(ListingUrl.TryCreate(input, out var url));
        Assert.Equal(portal, url!.Portal);
        Assert.Equal(id, url.ExternalId);
        Assert.Equal("", url.Uri.Query);
        Assert.Equal("", url.Uri.Fragment);
        Assert.Equal(url.Uri.Host, url.Uri.Host.ToLowerInvariant());
    }

    [Theory]
    [InlineData("http://www.otomoto.pl/osobowe/oferta/kia-ID6IjsIM.html")]
    [InlineData("https://otomoto.pl.evil.com/osobowe/oferta/kia-ID6IjsIM.html")]
    [InlineData("https://evil.com/otomoto/osobowe/oferta/kia-ID6IjsIM.html")]
    [InlineData("https://evil.com/osobowe/oferta/kia-ID6IjsIM.html?x=olx.pl")]
    [InlineData("https://www.otomoto.pl@evil.com/osobowe/oferta/kia-ID6IjsIM.html")]
    [InlineData("https://user:pw@www.otomoto.pl/osobowe/oferta/kia-ID6IjsIM.html")]
    [InlineData("https://www.otomoto.pl:8443/osobowe/oferta/kia-ID6IjsIM.html")]
    [InlineData("https://169.254.169.254/otomoto/osobowe")]
    [InlineData("https://127.0.0.1/osobowe/oferta/kia-ID6IjsIM.html")]
    [InlineData("https://[::1]/osobowe/oferta/kia-ID6IjsIM.html")]
    [InlineData("https://www.otomoto.pl./osobowe/oferta/kia-ID6IjsIM.html")]
    [InlineData("https://sub.otomoto.pl/osobowe/oferta/kia-ID6IjsIM.html")]
    [InlineData("https://www.otomoto.pl/osobowe")]
    [InlineData("ftp://www.otomoto.pl/osobowe/oferta/kia-ID6IjsIM.html")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_everything_else(string? input)
    {
        Assert.False(ListingUrl.TryCreate(input, out var url));
        Assert.Null(url);
    }

    [Fact]
    public void Same_advert_under_different_query_strings_normalises_equal()
    {
        ListingUrl.TryCreate("https://www.olx.pl/d/oferta/a-CID5-ID1cXjgm.html?x=1", out var a);
        ListingUrl.TryCreate("https://www.olx.pl/d/oferta/a-CID5-ID1cXjgm.html", out var b);
        Assert.Equal(a!.Uri, b!.Uri);
    }
}
