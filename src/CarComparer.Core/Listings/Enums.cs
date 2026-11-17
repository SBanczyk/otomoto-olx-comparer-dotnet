namespace CarComparer.Core.Listings;

public enum Portal : byte
{
    Otomoto = 1,
    Olx = 2,
}

public enum FuelType { Other = 0, Petrol, Diesel, Lpg, Electric, Hybrid, PlugInHybrid, Cng }

public enum Gearbox { Other = 0, Manual, Automatic }

public enum BodyType { Other = 0, City, Sedan, Wagon, Hatchback, Coupe, Convertible, Suv, Minivan, Compact }

public enum Drive { Other = 0, Front, Rear, AllWheel }

public enum ScrapeFailureKind : byte
{
    UnsupportedUrl = 1,
    NotPassengerCar,
    Removed,
    Blocked,
    Unreachable,
    FormatChanged,
}
