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

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }

    public List<Participant> Participants { get; set; } = new();
    public List<Question> Questions { get; set; } = new();
    public List<ScoreEntry> ScoreEntries { get; set; } = new();
}
