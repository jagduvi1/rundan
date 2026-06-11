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

    /// <summary>Tell everyone watching an event that its roster/admins changed, so they re-claim
    /// (e.g. a player's admin flag was toggled and their host controls should appear/disappear live).</summary>
    public Task PushEventChangedAsync(int eventId)
        => hub.Clients.Group(ScoreboardGroups.ForEvent(eventId)).EventChanged(eventId);

    /// <summary>Broadcast a new chat message to everyone watching the event.</summary>
    public Task PushChatAsync(int eventId, ChatMessageDto message)
        => hub.Clients.Group(ScoreboardGroups.ForEvent(eventId)).ChatPosted(message);

    /// <summary>Tell everyone playing the activity that the host just started a track (fastest-to-answer).</summary>
    public Task PushMusicTrackStartedAsync(int activityId, int questionId, DateTimeOffset startedUtc, int windowSeconds)
        => hub.Clients.Group(ScoreboardGroups.For(activityId)).MusicTrackStarted(new MusicTrackStartedDto
        {
            ActivityId = activityId,
            QuestionId = questionId,
            StartedUtc = startedUtc,
            WindowSeconds = windowSeconds,
        });
}
