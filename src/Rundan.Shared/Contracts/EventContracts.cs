namespace Rundan.Shared.Contracts;

/// <summary>An event/day: an ordered set of activities played in sequence.</summary>
public class EventDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public int TeamSize { get; set; } = 2;
    public EventScoring Scoring { get; set; } = EventScoring.Cumulative;

    /// <summary>How teams are formed across the event's activities (re-mix each activity vs fixed for the event).</summary>
    public TeamShuffle TeamShuffle { get; set; } = TeamShuffle.EveryActivity;

    /// <summary>The "slap" twist mode (Off by default).</summary>
    public SlapMode SlapMode { get; set; } = SlapMode.Off;

    /// <summary>A slap that must be resolved before the next activity can start (null if none).</summary>
    public PendingSlapDto? PendingSlap { get; set; }

    public string JoinCode { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Optional availability window (local wall-clock). Outside it, players can't join/play; the host can.</summary>
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    /// <summary>Estimated metres covered walking the geolocated route (stations + activity points), if any.</summary>
    public int? EstimatedMeters { get; set; }

    /// <summary>Activities in running order.</summary>
    public List<ActivityDto> Activities { get; set; } = new();

    /// <summary>The roster users selected into this event.</summary>
    public List<UserDto> Members { get; set; } = new();

    /// <summary>User ids among the members who are event admins (can manage this event).</summary>
    public List<int> AdminUserIds { get; set; } = new();

    /// <summary>Names of people currently watching the event (recently seen).</summary>
    public List<string> Viewers { get; set; } = new();

    public bool HasRoster => Members.Count > 0;

    /// <summary>True once the event has activities and every one of them is finished.</summary>
    public bool IsComplete => Activities.Count > 0 && Activities.TrueForAll(a => a.Status == ActivityStatus.Finished);
}

/// <summary>A person who can be picked as a slap target or recipient.</summary>
public class SlapPersonDto
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Details of a slap that's waiting to be resolved before the next activity.</summary>
public class PendingSlapDto
{
    public int ActivityId { get; set; }
    public string ActivityTitle { get; set; } = string.Empty;

    /// <summary>The winning team's display name.</summary>
    public string WinnerName { get; set; } = string.Empty;

    /// <summary>The winning team's member user ids — none of them can be slapped (your own team).</summary>
    public List<int> WinnerUserIds { get; set; } = new();

    /// <summary>The one winning-team member who takes the slap: the lowest overall score (ties random).</summary>
    public int SlapperUserId { get; set; }

    /// <summary>That designated slapper's name (for the "waiting for …" line).</summary>
    public string? SlapperName { get; set; }

    /// <summary>The resolved effect for this activity (Vanish or SendToPlayer; Random is decided here).</summary>
    public SlapMode EffectiveMode { get; set; }

    /// <summary>All event members, for the target / recipient pickers.</summary>
    public List<SlapPersonDto> Members { get; set; } = new();
}

/// <summary>The slap for one finished activity, as the player flow sees it — pending (to take) or a
/// resolved result (who slapped whom, and who got the points).</summary>
public class ActivitySlapDto
{
    public int EventId { get; set; }
    public int ActivityId { get; set; }
    public string ActivityTitle { get; set; } = string.Empty;
    public SlapState State { get; set; }
    public SlapMode EffectiveMode { get; set; }

    // --- Pending: who owes the slap + who they can pick ---
    public string? WinnerName { get; set; }
    public List<int> WinnerUserIds { get; set; } = new();
    public List<SlapPersonDto> Members { get; set; } = new();

    // --- Taken / AwaitingRecipient: the people involved ---
    public int? SlapperUserId { get; set; }
    public string? SlapperName { get; set; }
    public int? SlappedUserId { get; set; }
    public string? SlappedName { get; set; }

    /// <summary>Who received the points on a "send" slap (null = the points vanished).</summary>
    public string? RecipientName { get; set; }

    /// <summary>Points removed from the slapped player.</summary>
    public double Penalty { get; set; }
}

/// <summary>The slapped player passes their lost points to a recipient (SlappedSends mode).</summary>
public class SendSlapPointsRequest
{
    public int ActivityId { get; set; }
    public int RecipientUserId { get; set; }
}

/// <summary>The winning player performs the slap.</summary>
public class PerformSlapRequest
{
    public int ActivityId { get; set; }
    public int SlappedUserId { get; set; }

    /// <summary>For a "send" slap: who receives the points.</summary>
    public int? RecipientUserId { get; set; }
}

/// <summary>Host skips the pending slap (e.g. the winner isn't around).</summary>
public class SkipSlapRequest
{
    public int ActivityId { get; set; }
}

/// <summary>Registers (or heartbeats) a viewer of an event. Pass the existing token to update.</summary>
public class RegisterViewerRequest
{
    public string Name { get; set; } = string.Empty;
    public Guid? Token { get; set; }
}

/// <summary>A registered viewer's identity.</summary>
public class ViewerDto
{
    public Guid Token { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Pushed over SignalR when an event's viewer list changes.</summary>
public class EventViewersDto
{
    public int EventId { get; set; }
    public List<string> Viewers { get; set; } = new();
}

/// <summary>Admin request to create an event.</summary>
public class CreateEventRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public int TeamSize { get; set; } = 2;
}

/// <summary>Admin request to update an event's details.</summary>
public class UpdateEventRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public int TeamSize { get; set; } = 2;
    public EventScoring Scoring { get; set; } = EventScoring.Cumulative;
    public TeamShuffle TeamShuffle { get; set; } = TeamShuffle.EveryActivity;
    public SlapMode SlapMode { get; set; } = SlapMode.Off;

    /// <summary>Optional availability window (local wall-clock). Either end may be null.</summary>
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
}

/// <summary>Request to set a custom event code, or regenerate one when Code is empty.</summary>
public class SetEventCodeRequest
{
    /// <summary>Custom code (uppercased). Leave empty to auto-generate a new unique code.</summary>
    public string? Code { get; set; }
}

/// <summary>Admin request to set which roster users are in an event (and which are event admins).</summary>
public class SetEventMembersRequest
{
    public List<int> UserIds { get; set; } = new();

    /// <summary>Subset of UserIds who should be event admins.</summary>
    public List<int> AdminUserIds { get; set; } = new();
}

/// <summary>Player picks their pre-registered name to claim their identity in the event.</summary>
public class ClaimRequest
{
    public int UserId { get; set; }
}

/// <summary>One team in an activity (the partner mixer output).</summary>
public class TeamDto
{
    public int ActivityId { get; set; }
    public int ParticipantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<UserDto> Members { get; set; } = new();
}

/// <summary>Admin request to set the running order of an event's activities.</summary>
public class ReorderActivitiesRequest
{
    /// <summary>Activity ids in the desired order.</summary>
    public List<int> ActivityIds { get; set; } = new();
}

/// <summary>One row in the combined event standings (summed across all activities).</summary>
public class EventStandingEntryDto
{
    /// <summary>Roster user id this row belongs to (0 for free-name events, which don't use it).</summary>
    public int UserId { get; set; }

    public string DisplayName { get; set; } = string.Empty;
    public int Rank { get; set; }
    public double TotalPoints { get; set; }

    /// <summary>How many of the event's activities this player has scored in.</summary>
    public int ActivitiesPlayed { get; set; }
}

/// <summary>Combined event standings — every player's total across all activities.</summary>
public class EventStandingsDto
{
    public int EventId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<EventStandingEntryDto> Entries { get; set; } = new();
    public DateTimeOffset UpdatedUtc { get; set; }
}

/// <summary>Player request to join the whole event with one name.</summary>
public class EventJoinRequest
{
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>The per-activity playing identity for a player (their team, in roster events).</summary>
public class EventJoinSlotDto
{
    public int ActivityId { get; set; }
    public int ParticipantId { get; set; }
    public Guid Token { get; set; }
    public string TeamName { get; set; } = string.Empty;
}

/// <summary>Result of free-name joining a rosterless event: a session for each joinable activity.</summary>
public class EventJoinResultDto
{
    public int EventId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public List<EventJoinSlotDto> Slots { get; set; } = new();
}

/// <summary>Result of claiming a roster identity: a team session for each joinable activity.</summary>
public class ClaimResultDto
{
    public int EventId { get; set; }
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Secret the device keeps to re-claim the user (e.g. when the next activity opens).</summary>
    public Guid MemberToken { get; set; }

    /// <summary>True if this player is an event admin (can manage the event).</summary>
    public bool IsEventAdmin { get; set; }

    public List<EventJoinSlotDto> Slots { get; set; } = new();
}
