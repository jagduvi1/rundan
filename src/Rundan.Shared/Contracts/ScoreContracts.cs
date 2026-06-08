namespace Rundan.Shared.Contracts;

/// <summary>A single recorded score line for a round-based game (boule, generic).</summary>
public class ScoreEntryDto
{
    public int Id { get; set; }
    public int ParticipantId { get; set; }
    public string ParticipantName { get; set; } = string.Empty;
    public int Round { get; set; }
    public int Points { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset RecordedUtc { get; set; }
}

/// <summary>Request to record (or correct) a score line. Acts as a scorekeeper action.</summary>
public class RecordScoreRequest
{
    public int ParticipantId { get; set; }
    public int Round { get; set; } = 1;
    public int Points { get; set; }
    public string? Note { get; set; }
}
