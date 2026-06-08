using Microsoft.AspNetCore.SignalR;
using Rundan.Server.Hubs;
using Rundan.Shared;
using Rundan.Shared.Contracts;
using Rundan.Shared.Realtime;

namespace Rundan.Server.Services;

/// <summary>
/// Pushes real-time updates to everyone watching an activity. Used by the write
/// endpoints after a successful change so the shared scoreboard updates live.
/// </summary>
public sealed class ScoreboardNotifier(
    IHubContext<ScoreboardHub, IScoreboardClient> hub,
    ScoreboardService scoreboard)
{
    /// <summary>Rebuild and broadcast the scoreboard for an activity.</summary>
    public async Task PushScoreboardAsync(int activityId, CancellationToken ct = default)
    {
        var board = await scoreboard.BuildAsync(activityId, ct);
        if (board is not null)
        {
            await hub.Clients.Group(ScoreboardGroups.For(activityId)).ScoreboardUpdated(board);
        }
    }

    public Task PushStatusAsync(int activityId, ActivityStatus status)
        => hub.Clients.Group(ScoreboardGroups.For(activityId))
            .ActivityStatusChanged(new ActivityStatusChangedDto { ActivityId = activityId, Status = status });

    public Task PushParticipantJoinedAsync(int activityId, ParticipantDto participant)
        => hub.Clients.Group(ScoreboardGroups.For(activityId)).ParticipantJoined(participant);

    /// <summary>Broadcast the current viewer list to everyone watching an event.</summary>
    public Task PushViewersAsync(int eventId, List<string> viewers)
        => hub.Clients.Group(ScoreboardGroups.ForEvent(eventId))
            .ViewersChanged(new EventViewersDto { EventId = eventId, Viewers = viewers });
}
