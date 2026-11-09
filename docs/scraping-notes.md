# Scraping notes (phase 1 spike)

Findings from live requests on 9 November 2026. Fixtures are in `tests/CarComparer.Scraping.Tests/Fixtures/`.

## OTOMOTO

- Listing page contains `<script id="__NEXT_DATA__">`; the advert is at `props.pageProps.advert`.
- `advert.status` is `ACTIVE` for live listings. A removed listing returns HTTP 404.
- Category: `advert.category.code` (`osobowe` for passenger cars; a truck gave `ciezarowe`). The URL path does not decide this, so the check is on the JSON.
- Parameters: `advert.parametersDict.<key>.values[0].{value,label}`.

| Attribute | Key | Notes |
|---|---|---|
| make / model | `make`, `model` | label used for display |
| year | `year` | |
| fuel | `fuel_type` | `petrol`, `diesel`, `electric` seen |
| gearbox | `gearbox` | `manual`, `automatic` |
| engine capacity | `engine_capacity` | absent for electric cars |
| power | `engine_power` | HP |
| mileage | `mileage` | |
| body | `body_type` | `combi`, `suv` seen |
| drive | `transmission` | **the drive, not the gearbox**: `front-wheel`, `all-wheel-permanent` |
| damaged | `damaged` | `0` / `1` |
| country of origin | `country_origin` | label, e.g. "Polska" |

- Price: `advert.price.value` (string) and `advert.price.currency`.
- Listing ID: the `ID6IjsIM` suffix of the URL. The numeric `advert.id` differs and is not used.

## OLX

- The API ID is **not** the code in the URL. `GET /api/v1/offers/ID1d6KJb/` returns 404.
- The listing page carries one `application/ld+json` block (`@type: Car`) with `sku` (the numeric API ID), `brand.name` and `category` (a URL; passenger cars start with `/motoryzacja/samochody`).
- Flow: page, then JSON-LD, then `GET /api/v1/offers/{sku}/`. This needs two requests per listing.
- API `data.status` is `active` for live listings. Unknown ID gives 404.
- Parameters: `data.params[]` with `key` and `value.{key,label}`. Keys: `model`, `year`, `petrol` (fuel), `car_body`, `enginesize`, `enginepower`, `milage` (sic), `transmission` (**gearbox** here), `drive`, `condition` (`damaged` / `notdamaged`), `price` (`value.value`, `value.currency`).
- The make is not in `params`; it comes from JSON-LD `brand.name`. The model is sometimes generic (e.g. "Pozostałe BMW").
- No country of origin was found; the field stays empty for OLX.

## Environment findings

- `curl.exe` gets 403 from `olx.pl` on every URL, including ones that work from .NET (`HttpClient` / `Invoke-WebRequest`). This looks like client fingerprinting. The scrapers use `HttpClient` and work from it.
- A non-existent OLX listing page closed the connection instead of answering 404. This is mapped to `Unreachable`. The `Removed` mapping for pages is therefore unverified; the API 404 path is verified.
- NBP: `GET https://api.nbp.pl/api/exchangerates/rates/a/{code}/?format=json` returns the latest table A rate (`rates[0].mid`, `effectiveDate`).

## Mappings not verified against live data

Body types other than `combi`/`estate-car`/`suv`/`off-road-vehicle`, fuel keys other than `petrol`/`diesel`/`lpg`/`electric`. Unmapped keys become `Other` and the portal label is shown.

## Open questions

- **GitHub Actions runners:** not tested. Whether either portal blocks data-centre IPs decides if the phase 7 canary is viable. Run `canary.yml` manually once to find out.
- **OLX to OTOMOTO redirects:** not observed in the samples taken; no delegation implemented.
- **Terms of service:** to be checked before the repository is made public.
