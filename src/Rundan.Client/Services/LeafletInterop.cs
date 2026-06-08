using Microsoft.JSInterop;

namespace Rundan.Client.Services;

/// <summary>A map marker for a Tipspromenad question.</summary>
public sealed class MapMarker
{
    public double Lat { get; set; }
    public double Lng { get; set; }
    public int Radius { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool Done { get; set; }
}

/// <summary>Wraps the Leaflet map (leaflet.js module) with OpenStreetMap tiles.</summary>
public sealed class LeafletInterop(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private IJSObjectReference? _handle;

    public async Task InitAsync(string elementId, double lat, double lng, int zoom = 16)
    {
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/leaflet.js");
        _handle = await _module.InvokeAsync<IJSObjectReference>("createMap", elementId, lat, lng, zoom);
    }

    public async Task SetQuestionMarkersAsync(IEnumerable<MapMarker> markers)
    {
        if (_module is not null && _handle is not null)
        {
            await _module.InvokeVoidAsync("setQuestionMarkers", _handle, markers);
        }
    }

    public async Task SetUserAsync(double lat, double lng, double accuracy)
    {
        if (_module is not null && _handle is not null)
        {
            await _module.InvokeVoidAsync("setUser", _handle, lat, lng, accuracy);
        }
    }

    public async Task FitToMarkersAsync(IEnumerable<MapMarker> markers)
    {
        if (_module is not null && _handle is not null)
        {
            await _module.InvokeVoidAsync("fitToMarkers", _handle, markers);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null && _handle is not null)
            {
                await _module.InvokeVoidAsync("dispose", _handle);
            }

            if (_handle is not null)
            {
                await _handle.DisposeAsync();
            }

            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { /* navigating away */ }
        finally
        {
            // Null out so the not-null guards in the Set*/Fit* methods turn any late
            // call (e.g. an in-flight geolocation callback) into a no-op.
            _handle = null;
            _module = null;
        }
    }
}
