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
    private static string SessionKey(int activityId) => $"rundan.session.{activityId}";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public BootstrapDto? Bootstrap { get; private set; }
    public string? AccessCode { get; private set; }
    public string? AdminCode { get; private set; }
    public bool Initialized { get; private set; }

    public bool HasAdminCode => !string.IsNullOrEmpty(AdminCode);

    /// <summary>When true, a host is previewing the app as an ordinary player (host UI hidden).</summary>
    public bool PreviewAsPlayer { get; private set; }

    /// <summary>True only when the device is the host AND not currently previewing as a player.</summary>
    public bool IsHost => HasAdminCode && !PreviewAsPlayer;

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
        Bootstrap = await api.GetBootstrapAsync();

        // Only consider ourselves initialized once we actually reached the server. A failed
        // bootstrap must NOT be cached as "no access code required" (that would fail open in
        // the UI); leave Initialized false so the next attempt retries.
        Initialized = Bootstrap is not null;
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
    public Guid? ActiveMemberToken { get; set; }
    private static string MemberTokenKey(int eventId) => $"rundan.membertoken.{eventId}";
    public async Task<Guid?> GetEventMemberTokenAsync(int eventId)
        => Guid.TryParse(await GetItemAsync(MemberTokenKey(eventId)), out var t) ? t : null;
    public Task SaveEventMemberTokenAsync(int eventId, Guid token) => SetItemAsync(MemberTokenKey(eventId), token.ToString());

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
