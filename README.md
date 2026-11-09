# Porównywarka ogłoszeń OTOMOTO i OLX (.NET)

Wklej 2 do 4 linków do ogłoszeń samochodów z OTOMOTO lub OLX i zobacz je w jednej tabeli: najlepsze wartości w wierszu są wyróżnione, ceny przeliczone na PLN (kurs NBP), a porównanie ma stały link. UI jest po polsku, kod po angielsku.

Rewrite of the [Flask prototype](https://github.com/SBanczyk/otomoto_olx_comparer) as an ASP.NET Core portfolio project (.NET 10, EF Core 10, Razor Pages, SQL Server). It runs locally; there is no deployment.

## Features

- Compare 2 to 4 listings from OTOMOTO and OLX, mixed freely. Each comparison gets a random 12-character link; anyone with the link can view and refresh it.
- Best value highlighted per row; prices converted to PLN with the latest NBP rate; derived km/year.
- A listing that is removed, blocked or unreachable only affects its own column; earlier data stays visible.
- Refresh is manual and throttled (one fetch per listing per 10 minutes).
- Accounts (ASP.NET Core Identity): e-mail confirmation, password reset, authenticator-app 2FA with recovery codes, personal data download and account deletion. Local accounts only, no external sign-in.
- Signed-in users can save any comparison as their own copy, list their comparisons and delete them. Only the owner can delete.
- Anonymous comparisons are removed after 30 days by a background job (`Comparisons:AnonymousRetention`, run every 6 hours).
- Polish UI with a light and dark theme that follows the system setting.

## Run it

Prerequisites: Docker and the .NET 10 SDK.

```bash
docker compose up -d
```

```bash
dotnet run --project src/CarComparer.Web
```

Open the URL printed on start. In Development the migrations are applied on startup. Mail sent by the app (registration, password reset) lands in Mailpit at http://localhost:8025.

To use another SQL Server (LocalDB, your own instance) override the connection string:

```bash
dotnet user-secrets set "ConnectionStrings:Default" "<your connection string>" --project src/CarComparer.Web
```

Apple Silicon: the SQL Server image is amd64 and needs Rosetta emulation enabled in Docker Desktop.

## Tests

```bash
dotnet test --filter-not-trait "Category=Live"
```

Integration tests start SQL Server in a container (Testcontainers), so Docker must be running. Scraper tests run against saved fixtures. `Category=Live` tests hit the real portals; the weekly `canary` workflow runs them and opens an issue when a portal blocks the request or changes its format. To refresh a fixture: `dotnet run --project tools/CarComparer.FixtureGrabber -- <url> [--name <name>] [--parse]`.

## Structure

`Core` (domain, no dependencies) / `Scraping` (OTOMOTO, OLX, NBP) / `Web` (Razor Pages, Identity, EF Core). There is no MediatR and no repository layer over EF Core: the `DbContext` is already a unit of work, and the services are thin. How the data is read is in [docs/scraping-notes.md](docs/scraping-notes.md): OTOMOTO from the JSON in `__NEXT_DATA__`, OLX from its JSON API, not from CSS classes.

## What changed compared with the prototype

- **XSS:** HTML was built with f-strings and `| safe`. Now Razor's automatic encoding only; no `Html.Raw` on external data.
- **IDOR:** any comparison could be deleted by ID. Now random 12-character IDs and owner-only delete (resource-based authorisation); non-owners get 404.
- **Missing authorisation:** `/my-comparisons` and `/change-password` did not check login, and logout left `user_id` in the session. Now cookie auth with Identity.
- **Password change without the current password:** the current password is required.
- **Sign-up bug:** mismatched passwords still created the account. Now model validation stops it.
- **CSRF:** forms and the delete `fetch` had no token. Now antiforgery on every POST.
- **SSRF:** validation was `"otomoto" in url[0:40]`. Now `ListingUrl`: `https` only, exact host allow-list (`otomoto.pl`, `olx.pl`, with or without `www.`), no credentials or ports.
- **Dead scraper:** hashed CSS classes. Now structured data, with a weekly canary.
- **Inconsistent data:** the list showed the first-save price while details scraped live, and a removed listing broke the comparison. Now each fetch is a snapshot, so saved comparisons survive a listing disappearing.

## Notes

Scraping is low volume (single listings chosen by the user), throttled to one fetch per listing per 10 minutes. Check the portals' terms of service before publishing the repository. See the open questions in [docs/scraping-notes.md](docs/scraping-notes.md).

Known limitations:

- The scraper's HTTP clients follow redirects, so the host allow-list is enforced on the submitted URL only, not on redirect targets.
- Two people creating a comparison for the same brand-new listing at the same moment can hit a unique-index error (one of them gets a 500).
- The SMTP sender is plain, unauthenticated SMTP (enough for Mailpit, not for a real mail relay).
- The weekly canary has not been run on a GitHub Actions runner yet, so it is unknown whether the portals block data-centre IPs.
- The development connection string (with a throwaway SA password) is in `appsettings.Development.json` and `docker-compose.yml`.
