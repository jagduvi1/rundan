using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rundan.Server.Data;
using Rundan.Shared;
using WebPush;
using WebPushSubscription = WebPush.PushSubscription;

namespace Rundan.Server.Services;

/// <summary>
/// Sends Web Push notifications to everyone subscribed to an event. VAPID keys are generated once and
/// persisted next to the database (they must stay stable, or existing subscriptions break). All sends
/// are best-effort and fire-and-forget — notifications must never fail a request.
/// </summary>
public sealed class PushService
{
    private readonly WebPushClient _client = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PushService> _log;
    private readonly VapidDetails _vapid;

    // Last seen overall-standings leader per event (identity string), to fire "new leader" only on change.
    private readonly ConcurrentDictionary<int, string> _lastLeader = new();

    public string PublicKey => _vapid.PublicKey;
    public bool Enabled => !string.IsNullOrEmpty(_vapid.PublicKey);

    public PushService(StoragePaths paths, IServiceScopeFactory scopes, ILogger<PushService> log)
    {
        _scopes = scopes;
        _log = log;

        var dataDir = Directory.GetParent(paths.UploadsDir)?.FullName ?? paths.UploadsDir;
        var file = Path.Combine(dataDir, "vapid.json");
        string publicKey = string.Empty, privateKey = string.Empty;
        try
        {
            if (File.Exists(file))
            {
                var saved = JsonSerializer.Deserialize<VapidFile>(File.ReadAllText(file));
                publicKey = saved?.PublicKey ?? string.Empty;
                privateKey = saved?.PrivateKey ?? string.Empty;
            }

            if (string.IsNullOrEmpty(publicKey) || string.IsNullOrEmpty(privateKey))
            {
                var keys = VapidHelper.GenerateVapidKeys();
                publicKey = keys.PublicKey;
                privateKey = keys.PrivateKey;
                File.WriteAllText(file, JsonSerializer.Serialize(new VapidFile { PublicKey = publicKey, PrivateKey = privateKey }));
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Push disabled — couldn't load/generate VAPID keys.");
            publicKey = string.Empty;
            privateKey = string.Empty;
        }

        _vapid = new VapidDetails("mailto:noreply@rundan.app", publicKey, privateKey);
    }

    private sealed class VapidFile
    {
        public string PublicKey { get; set; } = string.Empty;
        public string PrivateKey { get; set; } = string.Empty;
    }

    /// <summary>Fire-and-forget notify to everyone following the event.</summary>
    public void Notify(int eventId, string title, string body, string url, string? tag = null)
        => _ = SendToEventAsync(eventId, title, body, url, tag);

    public async Task SendToEventAsync(int eventId, string title, string body, string url, string? tag = null)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subs = await db.PushSubscriptions.AsNoTracking().Where(s => s.EventId == eventId).ToListAsync();
            if (subs.Count == 0)
            {
                return;
            }

            var payload = JsonSerializer.Serialize(new { title, body, url, tag });
            var stale = new List<int>();
            foreach (var s in subs)
            {
                try
                {
                    await _client.SendNotificationAsync(new WebPushSubscription(s.Endpoint, s.P256dh, s.Auth), payload, _vapid);
                }
                catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
                {
                    stale.Add(s.Id); // subscription expired — drop it
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Push send failed for one subscription.");
                }
            }

            if (stale.Count > 0)
            {
                await db.PushSubscriptions.Where(s => stale.Contains(s.Id)).ExecuteDeleteAsync();
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Push notify failed for event {EventId}.", eventId);
        }
    }

    /// <summary>Notify when an activity finishes: who won, a slap (if slaps are on), and a new
    /// overall-standings leader if the lead changed hands.</summary>
    public async Task NotifyActivityFinishedAsync(int activityId)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            using var scope = _scopes.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<AppDbContext>();

            var activity = await db.Activities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == activityId);
            if (activity?.EventId is not int eventId)
            {
                return;
            }

            var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId);
            if (ev is null)
            {
                return;
            }

            var board = await sp.GetRequiredService<ScoreboardService>().BuildAsync(activityId);
            var winners = board?.Entries.Where(e => e.Rank == 1).Select(e => e.DisplayName).ToList() ?? new();
            if (winners.Count > 0)
            {
                var who = string.Join(" & ", winners);
                Notify(eventId, "🏆 We have a winner!", $"{who} won “{activity.Title}”.", $"e/{eventId}", $"won-{activityId}");
                if (ev.SlapMode != SlapMode.Off)
                {
                    Notify(eventId, "👋 Slap time!", $"{who} can slap a rival after “{activity.Title}”.", $"e/{eventId}", $"slap-{activityId}");
                }
            }

            var standings = await sp.GetRequiredService<EventStandingsService>().BuildAsync(eventId);
            var leader = standings?.Entries.FirstOrDefault(e => e.Rank == 1);
            if (leader is not null)
            {
                var id = leader.UserId != 0 ? $"u{leader.UserId}" : $"n{leader.DisplayName}";
                if (_lastLeader.TryGetValue(eventId, out var prev) && prev != id)
                {
                    Notify(eventId, "🥇 New leader!", $"{leader.DisplayName} is now top of {ev.Name}.", $"e/{eventId}", $"leader-{eventId}");
                }
                _lastLeader[eventId] = id;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Push notify-finished failed for activity {ActivityId}.", activityId);
        }
    }
}
