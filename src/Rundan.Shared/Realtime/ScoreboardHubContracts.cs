using Rundan.Shared.Contracts;

namespace Rundan.Shared.Realtime;

/// <summary>Well-known hub endpoint paths.</summary>
public static class HubRoutes
{
    public const string Scoreboard = "/hub/scoreboard";
}

/// <summary>
/// Server → client message names. The strongly-typed <see cref="IScoreboardClient"/>
/// methods must match these exactly; the WASM client subscribes by these strings.
/// </summary>
public static class ScoreboardMessages
{
    public const string ScoreboardUpdated = nameof(IScoreboardClient.ScoreboardUpdated);
    public const string ParticipantJoined = nameof(IScoreboardClient.ParticipantJoined);
    public const string ActivityStatusChanged = nameof(IScoreboardClient.ActivityStatusChanged);
}

/// <summary>Client → server method names (hub invocations).</summary>
public static class ScoreboardHubMethods
{
    public const string JoinActivity = "JoinActivity";
    public const string LeaveActivity = "LeaveActivity";
}

/// <summary>
/// Strongly-typed contract for messages the server pushes to connected clients.
/// Used by <c>Hub&lt;IScoreboardClient&gt;</c> on the server for compile-checked sends.
/// </summary>
public interface IScoreboardClient
{
    /// <summary>The scoreboard for an activity changed.</summary>
    Task ScoreboardUpdated(ScoreboardDto scoreboard);

    /// <summary>Someone joined the activity (lobby update).</summary>
    Task ParticipantJoined(ParticipantDto participant);

    /// <summary>The activity moved between lifecycle states (e.g. started or finished).</summary>
    Task ActivityStatusChanged(ActivityStatusChangedDto status);
}
