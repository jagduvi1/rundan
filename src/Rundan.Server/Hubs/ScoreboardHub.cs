using Microsoft.AspNetCore.SignalR;
using Rundan.Shared.Contracts;
using Rundan.Shared.Realtime;

namespace Rundan.Server.Hubs;

/// <summary>Helpers for the SignalR group naming.</summary>
public static class ScoreboardGroups
{
    public static string For(int activityId) => $"activity-{activityId}";
    public static string ForEvent(int eventId) => $"event-{eventId}";
}

/// <summary>
/// In-process SignalR hub for live scoreboards. Strongly typed against
/// <see cref="IScoreboardClient"/> so server-side sends are compile-checked.
/// Clients join the group for the activity they are watching; the server pushes
/// scoreboard/status updates to that group after each write.
/// </summary>
public sealed class ScoreboardHub : Hub<IScoreboardClient>
{
    /// <summary>Subscribe this connection to live updates for one activity.</summary>
    public Task JoinActivity(int activityId)
        => Groups.AddToGroupAsync(Context.ConnectionId, ScoreboardGroups.For(activityId));

    /// <summary>Stop receiving updates for one activity.</summary>
    public Task LeaveActivity(int activityId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, ScoreboardGroups.For(activityId));

    /// <summary>Subscribe to event-level updates (e.g. who is watching).</summary>
    public Task JoinEvent(int eventId)
        => Groups.AddToGroupAsync(Context.ConnectionId, ScoreboardGroups.ForEvent(eventId));

    /// <summary>Stop receiving event-level updates.</summary>
    public Task LeaveEvent(int eventId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, ScoreboardGroups.ForEvent(eventId));

    /// <summary>Relay a live-stopwatch start to everyone watching the activity, stamped with the
    /// server time so they all tick from the same reference.</summary>
    public Task StartTimer(int activityId, string key)
        => Clients.Group(ScoreboardGroups.For(activityId)).TimerStarted(
            new TimerStateDto { ActivityId = activityId, Key = key, StartedUtc = DateTimeOffset.UtcNow });

    /// <summary>Relay a live-stopwatch stop.</summary>
    public Task StopTimer(int activityId, string key)
        => Clients.Group(ScoreboardGroups.For(activityId)).TimerStopped(
            new TimerStateDto { ActivityId = activityId, Key = key });
}
