using System.Text.Json;
using Microsoft.JSInterop;
using Rundan.Shared.Contracts;

namespace Rundan.Client.Services;

/// <summary>
/// Holds the device's session: the shared access code, the admin code (if used), and
/// the per-activity player identities. Everything is persisted to localStorage so a
/// reload (common on flaky mobile signal) keeps you logged in.
/// </summary>
public sealed class AppState(IJSRuntime js)
{
    private const string AccessKey = "rundan.access";
    private const string AdminKey = "rundan.admin";
    private const string PreviewKey = "rundan.preview";
    private const string ProxyKey = "rundan.proxy";
    private static string SessionKey(int activityId) => $"rundan.session.{activityId}";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public BootstrapDto? Bootstrap { get; private set; }
    public string? AccessCode { get; private set; }
    public string? AdminCode { get; private set; }
    public bool Initialized { get; private set; }

    public bool HasAdminCode => !string.IsNullOrEmpty(AdminCode);

    /// <summary>
    /// True when the server runs "open admin" (no admin code configured — dev/trial), so any
    /// device may administer. In production an admin code is set, and only entering it grants host.
    /// </summary>
    public bool AdminOpen => Bootstrap is { RequiresAdminCode: false };

    /// <summary>When true, a host is previewing the app as an ordinary player (host UI hidden).</summary>
    public bool PreviewAsPlayer { get; private set; }

    /// <summary>
    /// True when this device may use host controls — it holds the admin code, or admin is open —
    /// and isn't currently previewing as a player.
    /// </summary>
    public bool IsHost => (HasAdminCode || AdminOpen) && !PreviewAsPlayer;

    /// <summary>The roster player a host is currently "playing as" (their phone died), or null.</summary>
    public ProxyIdentity? Proxy { get; private set; }

    public bool IsProxying => Proxy is not null;

    /// <summary>Raised when device state that a shared layout shows (e.g. the proxy) changes.</summary>
    public event Action? Changed;

    /// <summary>Start playing as another roster player (overlays their sessions + member token).</summary>
    public async Task SetProxyAsync(ProxyIdentity proxy)
    {
        Proxy = proxy;
        await SetItemAsync(ProxyKey, JsonSerializer.Serialize(proxy, JsonOptions));
        Changed?.Invoke();
    }

    /// <summary>Stop playing as another player and return to the host's own identity.</summary>
    public async Task ClearProxyAsync()
    {
        if (Proxy is null)
        {
            return;
        }

        Proxy = null;
        await RemoveItemAsync(ProxyKey);
        Changed?.Invoke();
    }

    public async Task SetPreviewAsync(bool on)
    {
        PreviewAsPlayer = on;
        if (on)
        {
            await SetItemAsync(PreviewKey, "1");
        }
        else
        {
            await RemoveItemAsync(PreviewKey);
        }

        Changed?.Invoke();
    }

    /// <summary>Loads persisted codes and the public bootstrap info. Safe to call repeatedly.</summary>
    public async Task EnsureInitializedAsync(RundanApi api)
    {
        if (Initialized)
        {
            return;
        }

        AccessCode = await GetItemAsync(AccessKey);
        AdminCode = await GetItemAsync(AdminKey);
        PreviewAsPlayer = await GetItemAsync(PreviewKey) == "1";
        Proxy = await LoadProxyAsync();
        Bootstrap = await api.GetBootstrapAsync();

        // Only consider ourselves initialized once we actually reached the server. A failed
        // bootstrap must NOT be cached as "no access code required" (that would fail open in
        // the UI); leave Initialized false so the next attempt retries.
        Initialized = Bootstrap is not null;
    }

    /// <summary>Re-fetch the public bootstrap (e.g. after changing the Spotify Client ID from the UI).</summary>
    public async Task RefreshBootstrapAsync(RundanApi api)
    {
        var b = await api.GetBootstrapAsync();
        if (b is not null)
        {
            Bootstrap = b;
            Changed?.Invoke();
        }
    }

    public async Task SetAccessCodeAsync(string code)
    {
        AccessCode = code;
        await SetItemAsync(AccessKey, code);
    }

    public async Task SetAdminCodeAsync(string? code)
    {
        AdminCode = code;
        if (string.IsNullOrEmpty(code))
        {
            await RemoveItemAsync(AdminKey);
        }
        else
        {
            await SetItemAsync(AdminKey, code);
        }
    }

    public async Task<PlayerSession?> GetSessionAsync(int activityId)
    {
        // When a host is playing as another player, that player's team session wins for
        // any activity they're in — so the host answers/scores/plays as them.
        if (Proxy is { } proxy && proxy.Sessions.TryGetValue(activityId, out var proxySession))
        {
            return proxySession;
        }

        var json = await GetItemAsync(SessionKey(activityId));
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PlayerSession>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveSessionAsync(int activityId, PlayerSession session)
    {
        await SetItemAsync(SessionKey(activityId), JsonSerializer.Serialize(session, JsonOptions));
    }

    public Task ClearSessionAsync(int activityId) => RemoveItemAsync(SessionKey(activityId));

    private async Task<ProxyIdentity?> LoadProxyAsync()
    {
        var json = await GetItemAsync(ProxyKey);
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProxyIdentity>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    // The event this device last opened — used to send a returning visitor straight back to it
    // from the landing page (so they don't have to re-pick it every time).
    private const string LastEventKey = "rundan.lastevent";
    public async Task<int?> GetLastEventIdAsync()
        => int.TryParse(await GetItemAsync(LastEventKey), out var v) ? v : null;
    public Task SaveLastEventIdAsync(int eventId) => SetItemAsync(LastEventKey, eventId.ToString());

    // The name a device joined an event with (used to prefill and to re-join newly opened activities).
    private static string EventNameKey(int eventId) => $"rundan.eventname.{eventId}";
    public Task<string?> GetEventNameAsync(int eventId) => GetItemAsync(EventNameKey(eventId));
    public Task SaveEventNameAsync(int eventId, string name) => SetItemAsync(EventNameKey(eventId), name);

    // For roster events: which pre-registered user this device claimed (to re-claim on new activities).
    private static string EventUserKey(int eventId) => $"rundan.eventuser.{eventId}";
    public async Task<int?> GetEventUserIdAsync(int eventId)
        => int.TryParse(await GetItemAsync(EventUserKey(eventId)), out var v) ? v : null;
    public Task SaveEventUserIdAsync(int eventId, int userId) => SetItemAsync(EventUserKey(eventId), userId.ToString());

    // The event-member token (proves an event admin) for management calls. Set by the active event page.
    // While a host is "playing as" another player, the proxy's token shadows it app-wide.
    private Guid? _ownMemberToken;
    public Guid? ActiveMemberToken
    {
        get => Proxy?.MemberToken ?? _ownMemberToken;
        set => _ownMemberToken = value;
    }
    private static string MemberTokenKey(int eventId) => $"rundan.membertoken.{eventId}";
    public async Task<Guid?> GetEventMemberTokenAsync(int eventId)
        => Guid.TryParse(await GetItemAsync(MemberTokenKey(eventId)), out var t) ? t : null;
    public Task SaveEventMemberTokenAsync(int eventId, Guid token) => SetItemAsync(MemberTokenKey(eventId), token.ToString());

    // Viewer (spectator) role for an event — sees everything but doesn't compete.
    private static string ViewerKey(int eventId) => $"rundan.viewer.{eventId}";
    private static string ViewerNameKey(int eventId) => $"rundan.viewername.{eventId}";
    private static string ViewerTokenKey(int eventId) => $"rundan.viewertoken.{eventId}";

    public async Task<bool> GetViewerAsync(int eventId) => await GetItemAsync(ViewerKey(eventId)) == "1";
    public Task SetViewerAsync(int eventId, bool on) =>
        on ? SetItemAsync(ViewerKey(eventId), "1") : RemoveItemAsync(ViewerKey(eventId));

    public Task<string?> GetViewerNameAsync(int eventId) => GetItemAsync(ViewerNameKey(eventId));
    public async Task<Guid?> GetViewerTokenAsync(int eventId)
        => Guid.TryParse(await GetItemAsync(ViewerTokenKey(eventId)), out var t) ? t : null;

    public async Task SaveViewerAsync(int eventId, string name, Guid token)
    {
        await SetItemAsync(ViewerKey(eventId), "1");
        await SetItemAsync(ViewerNameKey(eventId), name);
        await SetItemAsync(ViewerTokenKey(eventId), token.ToString());
    }

    public async Task ClearViewerAsync(int eventId)
    {
        await RemoveItemAsync(ViewerKey(eventId));
        await RemoveItemAsync(ViewerNameKey(eventId));
        await RemoveItemAsync(ViewerTokenKey(eventId));
    }

    private async Task<string?> GetItemAsync(string key)
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", key);
        }
        catch
        {
            return null;
        }
    }

    private async Task SetItemAsync(string key, string value)
    {
        // Best-effort: localStorage can fail (private mode / disabled / quota). Don't let a
        // persistence failure break a successful gate unlock or join — in-memory state still drives the page.
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", key, value);
        }
        catch
        {
            // ignore
        }
    }

    private async Task RemoveItemAsync(string key)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", key);
        }
        catch
        {
            // ignore
        }
    }
}
