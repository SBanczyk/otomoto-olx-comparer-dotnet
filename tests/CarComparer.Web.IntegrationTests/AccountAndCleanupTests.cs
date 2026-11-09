using System.Net;
using System.Text.RegularExpressions;
using CarComparer.Web.Data;
using CarComparer.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CarComparer.Web.IntegrationTests;

[Collection("db")]
public sealed partial class AccountAndCleanupTests(DatabaseFixture database) : IAsyncLifetime
{
    private const string Password = "Passw0rd-test";
    private AppFactory _factory = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await database.ResetAsync();
        _factory = new AppFactory(database.ConnectionString);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    private async Task<string> CreateUserAsync(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await users.CreateAsync(user, Password);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        return user.Id;
    }

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<string> TokenFromAsync(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url, Ct);
        return TokenPattern().Match(html).Groups[1].Value;
    }

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = NewClient();
        var token = await TokenFromAsync(client, "/Identity/Account/Login");
        var response = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
            ["__RequestVerificationToken"] = token,
        }), Ct);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private async Task<string> CreateComparisonAsync(string? ownerId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ComparisonService>()
            .CreateAsync([Sample.Url("IDa1"), Sample.Url("IDb2")], ownerId, Ct);
        return result.Id!;
    }

    private async Task<int> CountComparisonsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Comparisons.CountAsync(Ct);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string pageUrl, string postUrl, Dictionary<string, string>? form = null)
    {
        form ??= [];
        form["__RequestVerificationToken"] = await TokenFromAsync(client, pageUrl);
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(form), Ct);
    }

    [Fact]
    public async Task My_comparisons_requires_sign_in()
    {
        var response = await NewClient().GetAsync("/moje-porownania", Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Identity/Account/Login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Signed_in_user_sees_only_their_own_comparisons_and_loses_access_after_logout()
    {
        var userId = await CreateUserAsync("owner@example.com");
        var mine = await CreateComparisonAsync(userId);
        await CreateComparisonAsync(null);
        var client = await LoginAsync("owner@example.com");

        var page = await client.GetAsync("/moje-porownania", Ct);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains(mine, await page.Content.ReadAsStringAsync(Ct));

        var logout = await PostAsync(client, "/moje-porownania", "/Identity/Account/Logout");
        Assert.True(logout.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK);

        var after = await client.GetAsync("/moje-porownania", Ct);
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
    }

    [Fact]
    public async Task Another_user_gets_404_when_deleting_and_the_comparison_survives()
    {
        var ownerId = await CreateUserAsync("owner@example.com");
        await CreateUserAsync("intruder@example.com");
        var id = await CreateComparisonAsync(ownerId);
        var intruder = await LoginAsync("intruder@example.com");

        var response = await PostAsync(intruder, $"/porownanie/{id}", $"/porownanie/{id}?handler=Delete");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, await CountComparisonsAsync());
    }

    [Fact]
    public async Task Anonymous_comparison_cannot_be_deleted_by_a_signed_in_user()
    {
        await CreateUserAsync("someone@example.com");
        var id = await CreateComparisonAsync(null);
        var client = await LoginAsync("someone@example.com");

        var response = await PostAsync(client, $"/porownanie/{id}", $"/porownanie/{id}?handler=Delete");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, await CountComparisonsAsync());
    }

    [Fact]
    public async Task Owner_can_delete()
    {
        var ownerId = await CreateUserAsync("owner@example.com");
        var id = await CreateComparisonAsync(ownerId);
        var client = await LoginAsync("owner@example.com");

        var response = await PostAsync(client, $"/porownanie/{id}", $"/porownanie/{id}?handler=Delete");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(0, await CountComparisonsAsync());
    }

    [Fact]
    public async Task Delete_without_antiforgery_token_is_rejected()
    {
        var ownerId = await CreateUserAsync("owner@example.com");
        var id = await CreateComparisonAsync(ownerId);
        var client = await LoginAsync("owner@example.com");

        var response = await client.PostAsync($"/porownanie/{id}?handler=Delete", new FormUrlEncodedContent([]), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, await CountComparisonsAsync());
    }

    [Fact]
    public async Task Saving_creates_a_copy_owned_by_the_user_without_scraping()
    {
        var userId = await CreateUserAsync("saver@example.com");
        var id = await CreateComparisonAsync(null);
        _factory.Scraper.Calls.Clear();
        var client = await LoginAsync("saver@example.com");

        var response = await PostAsync(client, $"/porownanie/{id}", $"/porownanie/{id}?handler=Save");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(_factory.Scraper.Calls);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var comparisons = await db.Comparisons.Include(c => c.Items).ToListAsync(Ct);
        Assert.Equal(2, comparisons.Count);
        var copy = comparisons.Single(c => c.OwnerId == userId);
        Assert.NotEqual(id, copy.Id);
        Assert.Null(comparisons.Single(c => c.Id == id).OwnerId);
        Assert.Equal(comparisons.Single(c => c.Id == id).Items.Select(i => i.ListingId), copy.Items.Select(i => i.ListingId));
    }

    [Fact]
    public async Task Saving_while_signed_out_redirects_to_login_with_return_url()
    {
        var id = await CreateComparisonAsync(null);
        var client = NewClient();

        var response = await PostAsync(client, $"/porownanie/{id}", $"/porownanie/{id}?handler=Save");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Contains("/Identity/Account/Login", location);
        Assert.Contains(Uri.EscapeDataString($"/porownanie/{id}"), location);
        Assert.Equal(1, await CountComparisonsAsync());
    }

    [Fact]
    public async Task Deleting_an_account_deletes_its_comparisons()
    {
        var userId = await CreateUserAsync("leaving@example.com");
        await CreateComparisonAsync(userId);
        await CreateComparisonAsync(null);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            Assert.True((await users.DeleteAsync((await users.FindByIdAsync(userId))!)).Succeeded);
        }

        Assert.Equal(1, await CountComparisonsAsync());
    }

    [Fact]
    public async Task Changing_the_password_requires_the_current_password()
    {
        await CreateUserAsync("changer@example.com");
        var client = await LoginAsync("changer@example.com");

        var response = await PostAsync(client, "/Identity/Account/Manage/ChangePassword", "/Identity/Account/Manage/ChangePassword", new()
        {
            ["Input.OldPassword"] = "",
            ["Input.NewPassword"] = "Another-Passw0rd",
            ["Input.ConfirmPassword"] = "Another-Passw0rd",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // form redisplayed with a validation error
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        Assert.True(await users.CheckPasswordAsync((await users.FindByEmailAsync("changer@example.com"))!, Password));
    }

    [Fact]
    public async Task Registration_with_mismatched_passwords_does_not_create_the_account()
    {
        var client = NewClient();

        var response = await PostAsync(client, "/Identity/Account/Register", "/Identity/Account/Register", new()
        {
            ["Input.Email"] = "new@example.com",
            ["Input.Password"] = "Passw0rd-one",
            ["Input.ConfirmPassword"] = "Passw0rd-two",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>().FindByEmailAsync("new@example.com"));
    }

    [Fact]
    public async Task Cleanup_removes_old_anonymous_comparisons_and_orphan_listings_but_keeps_owned_ones()
    {
        var ownerId = await CreateUserAsync("keeper@example.com");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<ComparisonService>();
            await svc.CreateAsync([Sample.Url("IDa1"), Sample.Url("IDb2")], null, Ct);   // anonymous, will be old
            await svc.CreateAsync([Sample.Url("IDc3"), Sample.Url("IDd4")], ownerId, Ct); // owned, also old
        }

        _factory.Time.Advance(TimeSpan.FromDays(10));
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ComparisonService>()
                .CreateAsync([Sample.Url("IDe5"), Sample.Url("IDf6")], null, Ct);         // anonymous, recent
        }

        _factory.Time.Advance(TimeSpan.FromDays(21)); // first anonymous is now 31 days old, second anonymous 21

        await using var cleanupScope = _factory.Services.CreateAsyncScope();
        var result = await cleanupScope.ServiceProvider.GetRequiredService<ComparisonCleanup>().RunAsync(Ct);

        Assert.Equal(new CleanupResult(1, 2), result);
        var db = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Comparisons.CountAsync(Ct));
        Assert.Equal(4, await db.Listings.CountAsync(Ct));
        Assert.Equal(4, await db.Snapshots.CountAsync(Ct));
    }

    [Fact]
    public async Task Cleanup_keeps_a_listing_still_used_by_a_surviving_comparison()
    {
        var ownerId = await CreateUserAsync("keeper@example.com");
        await using var scope = _factory.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ComparisonService>();
        await svc.CreateAsync([Sample.Url("IDa1"), Sample.Url("IDb2")], null, Ct);
        await svc.CreateAsync([Sample.Url("IDa1"), Sample.Url("IDc3")], ownerId, Ct);
        _factory.Time.Advance(TimeSpan.FromDays(31));

        var result = await scope.ServiceProvider.GetRequiredService<ComparisonCleanup>().RunAsync(Ct);

        Assert.Equal(new CleanupResult(1, 1), result); // only IDb2 is orphaned; IDa1 is shared
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Listings.CountAsync(Ct));
    }
}
