using Rundan.Shared;

namespace Rundan.Server.Data.Entities;

/// <summary>One match in a knockout bracket (winners' or losers' side) for a Boule activity.</summary>
public class BracketMatch
{
    public int Id { get; set; }

    public int ActivityId { get; set; }
    public Activity? Activity { get; set; }

    /// <summary>Which playoff bracket this belongs to: 0 = Playoff A / the single knockout, 1 = Playoff B (plate).</summary>
    public int Pool { get; set; }

    /// <summary>Set for round-robin group matches (the 0-based group index); null for knockout matches.</summary>
    public int? GroupIndex { get; set; }

    public BracketSide Side { get; set; }

    /// <summary>1-based round within the side.</summary>
    public int Round { get; set; }

    /// <summary>0-based position within the round (top-to-bottom order in the tree).</summary>
    public int Slot { get; set; }

    public int? ParticipantAId { get; set; }
    public int? ParticipantBId { get; set; }

    /// <summary>Set once the result is recorded.</summary>
    public int? WinnerParticipantId { get; set; }

    /// <summary>The recorded score, if any — comma-separated set scores like "13-7,9-13,13-10"
    /// (a single "3-1" for free scoring). Null for byes or winner-only (simulated) results.</summary>
    public string? SetScores { get; set; }

    /// <summary>A walkover (only one team) — auto-advances and scores no win.</summary>
    public bool IsBye { get; set; }

    /// <summary>The court this match is played on, if assigned.</summary>
    public int? CourtId { get; set; }
    public Court? Court { get; set; }
}
