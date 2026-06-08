namespace Rundan.Shared.Contracts;

/// <summary>An activity as returned to clients.</summary>
public class ActivityDto
{
    public int Id { get; set; }

    /// <summary>Parent event, if this activity is part of one.</summary>
    public int? EventId { get; set; }

    /// <summary>Position in the event's running order (1, 2, 3 …).</summary>
    public int Order { get; set; }

    public ActivityType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public ActivityStatus Status { get; set; }

    /// <summary>Score-game point entry mode (team total vs per player).</summary>
    public ScoreEntryMode ScoreEntryMode { get; set; }

    /// <summary>Optional GPS geofence for the activity.</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? RadiusMeters { get; set; }

    public bool HasLocation => Latitude.HasValue && Longitude.HasValue;

    /// <summary>Short human-friendly code people type in to join (e.g. "FOX-417").</summary>
    public string JoinCode { get; set; } = string.Empty;

    public ScoringMode ScoringMode { get; set; }
    public int ParticipantCount { get; set; }
    public int QuestionCount { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }

    /// <summary>True for question-based activities (Quiz / Tipspromenad).</summary>
    public bool UsesQuestions => Type is ActivityType.Quiz or ActivityType.Tipspromenad;

    /// <summary>True for activities that need a map (Tipspromenad).</summary>
    public bool UsesMap => Type is ActivityType.Tipspromenad;

    /// <summary>True for round-based score games (Boule / generic).</summary>
    public bool UsesRounds => Type is ActivityType.Boule or ActivityType.ScoreGame;
}

/// <summary>Admin request to create a new activity.</summary>
public class CreateActivityRequest
{
    public ActivityType Type { get; set; } = ActivityType.Quiz;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public ScoringMode ScoringMode { get; set; } = ScoringMode.HigherWins;

    /// <summary>If set, the activity is added to this event (order auto-assigned to the end).</summary>
    public int? EventId { get; set; }
}

/// <summary>Admin request to update an activity's details (title / rules / picture / scoring / geofence).</summary>
public class UpdateActivityRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public ScoreEntryMode ScoreEntryMode { get; set; } = ScoreEntryMode.Team;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? RadiusMeters { get; set; }
}

/// <summary>Admin request to change activity status (open / start / finish / reset).</summary>
public class UpdateActivityStatusRequest
{
    public ActivityStatus Status { get; set; }
}

/// <summary>Pushed over SignalR when an activity changes lifecycle state.</summary>
public class ActivityStatusChangedDto
{
    public int ActivityId { get; set; }
    public ActivityStatus Status { get; set; }
}
