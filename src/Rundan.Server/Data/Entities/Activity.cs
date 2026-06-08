using Rundan.Shared;

namespace Rundan.Server.Data.Entities;

/// <summary>An activity instance (a quiz, a quiz-walk, a boule game, ...).</summary>
public class Activity
{
    public int Id { get; set; }

    /// <summary>Optional parent event. Null for a standalone activity.</summary>
    public int? EventId { get; set; }
    public Event? Event { get; set; }

    /// <summary>Position in the event's running order (1, 2, 3 …). 0 for standalone.</summary>
    public int Order { get; set; }

    public ActivityType Type { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>Free-text rules / info shown to players for this game.</summary>
    public string? Description { get; set; }

    /// <summary>Optional descriptive picture or map for this game.</summary>
    public string? ImageUrl { get; set; }

    public ActivityStatus Status { get; set; } = ActivityStatus.Draft;

    /// <summary>Unique, short, human-typable join code.</summary>
    public string JoinCode { get; set; } = string.Empty;

    public ScoringMode ScoringMode { get; set; } = ScoringMode.HigherWins;

    /// <summary>What a score-game measures (points / time / length).</summary>
    public Measurement Measurement { get; set; } = Measurement.Points;

    /// <summary>Target value for <see cref="ScoringMode.ClosestToTarget"/> (e.g. 147 seconds = 2:27).</summary>
    public int? TargetValue { get; set; }

    /// <summary>For question activities: serve the questions in a random order per player.</summary>
    public bool RandomizeQuestions { get; set; }

    /// <summary>Score-game point entry: one score per team, or per player (summed to the team).</summary>
    public ScoreEntryMode ScoreEntryMode { get; set; } = ScoreEntryMode.Team;

    /// <summary>Optional GPS geofence — when set, the activity unlocks on a player's phone within this radius.</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? RadiusMeters { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }

    public List<Participant> Participants { get; set; } = new();
    public List<Question> Questions { get; set; } = new();
    public List<ScoreEntry> ScoreEntries { get; set; } = new();
}
