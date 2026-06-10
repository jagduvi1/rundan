namespace Rundan.Shared.Contracts;

/// <summary>A single recorded score line for a round-based game (boule, generic).</summary>
public class ScoreEntryDto
{
    public int Id { get; set; }
    public int ParticipantId { get; set; }
    public string ParticipantName { get; set; } = string.Empty;

    /// <summary>For per-player team scoring: the player who scored, if any.</summary>
    public int? UserId { get; set; }
    public string? UserName { get; set; }

    public int Round { get; set; }
    public double Points { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset RecordedUtc { get; set; }
}

/// <summary>Ephemeral live-stopwatch state relayed between scorekeepers so everyone watching an
/// activity sees the running time-counter tick. Not persisted — only the final recorded time is.</summary>
public class TimerStateDto
{
    public int ActivityId { get; set; }

    /// <summary>Which row the timer belongs to (the BouleBoard unit key, e.g. a team's participant id).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Server time the timer started (so all viewers tick from the same reference).</summary>
    public DateTimeOffset StartedUtc { get; set; }
}

/// <summary>Request to record (or correct) a score line. Acts as a scorekeeper action.</summary>
public class RecordScoreRequest
{
    public int ParticipantId { get; set; }

    /// <summary>For per-player team scoring: which player (a member of the team) these points are for.</summary>
    public int? UserId { get; set; }

    public int Round { get; set; } = 1;
    public double Points { get; set; }
    public string? Note { get; set; }
}
