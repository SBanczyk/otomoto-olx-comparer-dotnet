using CarComparer.Core.Listings;

namespace CarComparer.Web.Services;

/// <summary>Polish UI texts for each failure kind.</summary>
public static class FailureMessages
{
    public const string InvalidUrl = "Nieprawidłowy link. Dozwolone są adresy https ogłoszeń z otomoto.pl i olx.pl.";

    public static string For(ScrapeFailureKind? kind, DateTimeOffset? inactiveSince = null) => kind switch
    {
        ScrapeFailureKind.UnsupportedUrl => InvalidUrl,
        ScrapeFailureKind.NotPassengerCar => "To ogłoszenie nie dotyczy samochodu osobowego.",
        ScrapeFailureKind.Removed => inactiveSince is { } since
            ? $"Ogłoszenie zostało usunięte (nieaktywne od {since:dd.MM.yyyy})."
            : "Ogłoszenie zostało usunięte.",
        ScrapeFailureKind.Blocked => "Portal odrzucił zapytanie. Spróbuj później.",
        ScrapeFailureKind.Unreachable => "Portal nie odpowiada.",
        ScrapeFailureKind.FormatChanged => "Nie udało się odczytać danych ogłoszenia (portal mógł zmienić format).",
        _ => "",
    };
}
