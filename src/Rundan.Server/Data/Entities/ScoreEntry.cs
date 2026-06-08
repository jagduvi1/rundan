namespace Rundan.Server.Data.Entities;

/// <summary>
/// One recorded score line for a round-based game (boule, generic score game).
/// A participant's total is the sum of their score entries.
/// </summary>
public class ScoreEntry
{
    public int Id { get; set; }

    public int ActivityId { get; set; }
    public Activity? Activity { get; set; }

    public int ParticipantId { get; set; }
    public Participant? Participant { get; set; }

    public int Round { get; set; } = 1;
    public int Points { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset RecordedUtc { get; set; }
}
