namespace Rundan.Server.Services;

/// <summary>Server-side great-circle distance (the MapPin score is computed here so the real city
/// location never reaches the client before the player has pinned).</summary>
public static class GeoMath
{
    /// <summary>Haversine distance between two lat/lng points, in kilometres.</summary>
    public static double DistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6371.0; // Earth radius, km
        var dLat = ToRad(lat2 - lat1);
        var dLng = ToRad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return r * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
