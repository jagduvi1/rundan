using Microsoft.AspNetCore.SignalR;
using Rundan.Shared.Realtime;

namespace Rundan.Server.Hubs;

/// <summary>Helpers for the per-activity SignalR group naming.</summary>
public static class ScoreboardGroups
{
    public static string For(int activityId) => $"activity-{activityId}";
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
}
