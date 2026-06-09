namespace Rundan.Server.Services;

/// <summary>Built-in pool of Swedish cities (name + approximate coordinates) for the MapPin game.
/// Coordinates only need to be good enough that relative pin distances are fair.</summary>
public static class SwedishCities
{
    public static readonly IReadOnlyList<(string Name, double Lat, double Lng)> All = new[]
    {
        ("Stockholm", 59.3293, 18.0686),
        ("Göteborg", 57.7089, 11.9746),
        ("Malmö", 55.6050, 13.0038),
        ("Uppsala", 59.8586, 17.6389),
        ("Västerås", 59.6099, 16.5448),
        ("Örebro", 59.2741, 15.2066),
        ("Linköping", 58.4109, 15.6216),
        ("Helsingborg", 56.0465, 12.6945),
        ("Jönköping", 57.7826, 14.1618),
        ("Norrköping", 58.5877, 16.1924),
        ("Lund", 55.7047, 13.1910),
        ("Umeå", 63.8258, 20.2630),
        ("Gävle", 60.6749, 17.1413),
        ("Borås", 57.7210, 12.9401),
        ("Eskilstuna", 59.3666, 16.5077),
        ("Karlstad", 59.4022, 13.5115),
        ("Växjö", 56.8777, 14.8091),
        ("Halmstad", 56.6745, 12.8568),
        ("Sundsvall", 62.3908, 17.3069),
        ("Luleå", 65.5848, 22.1567),
        ("Trollhättan", 58.2837, 12.2886),
        ("Östersund", 63.1792, 14.6357),
        ("Borlänge", 60.4858, 15.4371),
        ("Falun", 60.6065, 15.6355),
        ("Kalmar", 56.6634, 16.3566),
        ("Kristianstad", 56.0294, 14.1567),
        ("Skövde", 58.3912, 13.8451),
        ("Karlskrona", 56.1612, 15.5869),
        ("Visby", 57.6348, 18.2948),
        ("Kiruna", 67.8558, 20.2253),
        ("Skellefteå", 64.7507, 20.9528),
        ("Örnsköldsvik", 63.2909, 18.7152),
        ("Nyköping", 58.7528, 17.0086),
        ("Varberg", 57.1057, 12.2502),
        ("Uddevalla", 58.3498, 11.9424),
        ("Motala", 58.5371, 15.0364),
        ("Ängelholm", 56.2428, 12.8620),
        ("Härnösand", 62.6323, 17.9379),
        ("Piteå", 65.3172, 21.4794),
        ("Ystad", 55.4297, 13.8204),
        ("Lidköping", 58.5052, 13.1576),
        ("Sandviken", 60.6175, 16.7763),
        ("Hudiksvall", 61.7274, 17.1059),
        ("Mora", 61.0050, 14.5380),
    };
}
