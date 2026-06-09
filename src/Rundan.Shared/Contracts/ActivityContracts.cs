namespace Rundan.Shared.Contracts;

/// <summary>An activity as returned to clients.</summary>
public class ActivityDto
{
    public int Id { get; set; }

    /// <summary>Parent event, if this activity is part of one.</summary>
    public int? EventId { get; set; }

    /// <summary>True when participants compete as teams (the parent event pairs players up, TeamSize &gt; 1).
    /// False for singles events and standalone activities, where each participant is one player.</summary>
    public bool IsTeamBased { get; set; }

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

    /// <summary>What this score game measures (points / time / length).</summary>
    public Measurement Measurement { get; set; }

    /// <summary>Target value for ClosestToTarget scoring (e.g. 147 = 2:27 in seconds).</summary>
    public int? TargetValue { get; set; }

    /// <summary>Boule: how a match result is entered (free single score vs best-of sets).</summary>
    public MatchFormat MatchFormat { get; set; }

    /// <summary>Boule sets mode: sets per match (best of N — 1, 3 or 5).</summary>
    public int BestOfSets { get; set; } = 3;

    /// <summary>Boule sets mode: games to win a set (e.g. 13 for pétanque).</summary>
    public int GamesToWinSet { get; set; } = 13;

    /// <summary>Serve question activities in a random order per player.</summary>
    public bool RandomizeQuestions { get; set; }

    /// <summary>Hide question text/answers from the host's management view (host plays too). Players still see them.</summary>
    public bool HideQuestionsFromHost { get; set; }

    /// <summary>Reusable from the library in other events.</summary>
    public bool IsPublic { get; set; }

    /// <summary>What the playing surfaces are called (Court / Field / Track / Lane).</summary>
    public string CourtLabel { get; set; } = "Court";

    /// <summary>The playing surfaces for this activity (courts/lanes/…).</summary>
    public List<CourtDto> Courts { get; set; } = new();

    public int ParticipantCount { get; set; }

    /// <summary>Number of individual players: the parent event's roster size for event activities,
    /// or the count of joined participants for a standalone activity.</summary>
    public int PlayerCount { get; set; }

    /// <summary>Number of teams for a team-based event activity (roster ÷ team size); 0 otherwise.</summary>
    public int TeamCount { get; set; }

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

    /// <summary>Score game measured as a duration (stopwatch entry).</summary>
    public bool MeasuresTime => Measurement == Measurement.TimeSeconds;

    /// <summary>Score game measured as a length in millimetres.</summary>
    public bool MeasuresLength => Measurement == Measurement.Millimetres;
}

/// <summary>A playing surface (court / field / track / lane).</summary>
public class CourtDto
{
    public int Id { get; set; }
    public int Order { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Sets an activity's playing surfaces: the noun + the list of names (count = list length).</summary>
public class SetCourtsRequest
{
    public string Label { get; set; } = "Court";
    public List<string> Names { get; set; } = new();
}

/// <summary>Admin request to create a new activity.</summary>
public class CreateActivityRequest
{
    public ActivityType Type { get; set; } = ActivityType.Quiz;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public ScoringMode ScoringMode { get; set; } = ScoringMode.HigherWins;
    public Measurement Measurement { get; set; } = Measurement.Points;
    public int? TargetValue { get; set; }

    /// <summary>If set, the activity is added to this event (order auto-assigned to the end).</summary>
    public int? EventId { get; set; }
}

/// <summary>Admin request to update an activity's details (title / rules / picture / scoring / geofence).</summary>
public class UpdateActivityRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public ScoringMode ScoringMode { get; set; } = ScoringMode.HigherWins;
    public Measurement Measurement { get; set; } = Measurement.Points;
    public int? TargetValue { get; set; }
    public bool RandomizeQuestions { get; set; }
    public bool HideQuestionsFromHost { get; set; }
    public bool IsPublic { get; set; }
    public ScoreEntryMode ScoreEntryMode { get; set; } = ScoreEntryMode.Team;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? RadiusMeters { get; set; }

    /// <summary>Boule match result format + sets settings.</summary>
    public MatchFormat MatchFormat { get; set; }
    public int BestOfSets { get; set; } = 3;
    public int GamesToWinSet { get; set; } = 13;
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
