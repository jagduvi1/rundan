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
    public string JoinCode { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Activities in running order.</summary>
    public List<ActivityDto> Activities { get; set; } = new();

    /// <summary>The roster users selected into this event.</summary>
    public List<UserDto> Members { get; set; } = new();

    /// <summary>User ids among the members who are event admins (can manage this event).</summary>
    public List<int> AdminUserIds { get; set; } = new();

    public bool HasRoster => Members.Count > 0;
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
    public List<string> MemberNames { get; set; } = new();
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
    public string DisplayName { get; set; } = string.Empty;
    public int Rank { get; set; }
    public int TotalPoints { get; set; }

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
