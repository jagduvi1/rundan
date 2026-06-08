using Microsoft.JSInterop;

namespace Rundan.Client.Services;

/// <summary>Wraps the browser Vibration API (no-op where unsupported, e.g. iOS Safari).</summary>
public sealed class VibrationInterop(IJSRuntime js)
{
    public async Task BuzzAsync(int milliseconds = 200)
    {
        try
        {
            await js.InvokeVoidAsync("rundanVibrate", milliseconds);
        }
        catch (JSDisconnectedException) { }
    }

    /// <summary>Vibrate with a pattern of on/off durations, e.g. [80, 40, 120].</summary>
    public async Task PatternAsync(params int[] pattern)
    {
        try
        {
            await js.InvokeVoidAsync("rundanVibrate", pattern);
        }
        catch (JSDisconnectedException) { }
    }
}
