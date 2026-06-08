namespace Rundan.Shared.Contracts;

/// <summary>One row in the live scoreboard.</summary>
public class ScoreboardEntryDto
{
    public int ParticipantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int Rank { get; set; }
    public double TotalPoints { get; set; }

    /// <summary>For question games: how many answered. For score games: how many entries/rounds.</summary>
    public int Entries { get; set; }
}

/// <summary>
/// The full live scoreboard for one activity. Pushed over SignalR whenever a score
/// changes and also available via REST for the initial load.
/// </summary>
public class ScoreboardDto
{
    public int ActivityId { get; set; }
    public ActivityType Type { get; set; }
    public ActivityStatus Status { get; set; }
    public ScoringMode ScoringMode { get; set; }
    public int TotalQuestions { get; set; }
    public List<ScoreboardEntryDto> Entries { get; set; } = new();
    public DateTimeOffset UpdatedUtc { get; set; }
}
