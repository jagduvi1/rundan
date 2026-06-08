using Microsoft.JSInterop;

namespace Rundan.Client.Services;

/// <summary>A geolocation reading.</summary>
public readonly record struct GeoPosition(double Latitude, double Longitude, double AccuracyMeters);

/// <summary>
/// Wraps the browser Geolocation API (watchPosition) via the geo.js module.
/// Requires HTTPS, which the app enforces.
/// </summary>
public sealed class GeolocationInterop(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private DotNetObjectReference<GeolocationInterop>? _selfRef;
    private int _watchId = -1;

    public event Action<GeoPosition>? PositionChanged;
    public event Action<string>? ErrorOccurred;

    /// <summary>Starts continuous position updates. Triggers the browser permission prompt.</summary>
    public async Task StartAsync()
    {
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/geo.js");
        _selfRef ??= DotNetObjectReference.Create(this);
        _watchId = await _module.InvokeAsync<int>("startWatch", _selfRef);
    }

    /// <summary>One-shot location read (host placing a question). Throws on denial/timeout.</summary>
    public async Task<GeoPosition> GetCurrentAsync()
    {
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/geo.js");
        var coords = await _module.InvokeAsync<double[]>("getCurrentPosition");
        return new GeoPosition(coords[0], coords[1], coords[2]);
    }

    public async Task StopAsync()
    {
        if (_module is not null && _watchId >= 0)
        {
            try
            {
                await _module.InvokeVoidAsync("stopWatch", _watchId);
            }
            catch (JSDisconnectedException) { /* page going away */ }
            _watchId = -1;
        }
    }

    [JSInvokable]
    public void ReportPosition(double lat, double lng, double accuracy)
        => PositionChanged?.Invoke(new GeoPosition(lat, lng, accuracy));

    [JSInvokable]
    public void ReportError(string message) => ErrorOccurred?.Invoke(message);

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
        }

        _selfRef?.Dispose();
    }

    /// <summary>Great-circle distance between two points, in metres (Haversine).</summary>
    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadius = 6_371_000d;
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
                * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earthRadius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRad(double degrees) => degrees * Math.PI / 180d;
}
