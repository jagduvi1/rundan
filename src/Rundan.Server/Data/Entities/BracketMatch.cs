using Rundan.Shared;

namespace Rundan.Server.Data.Entities;

/// <summary>One match in a knockout bracket (winners' or losers' side) for a Boule activity.</summary>
public class BracketMatch
{
    public int Id { get; set; }

    public int ActivityId { get; set; }
    public Activity? Activity { get; set; }

    public BracketSide Side { get; set; }

    /// <summary>1-based round within the side.</summary>
    public int Round { get; set; }

    /// <summary>0-based position within the round (top-to-bottom order in the tree).</summary>
    public int Slot { get; set; }

    public int? ParticipantAId { get; set; }
    public int? ParticipantBId { get; set; }

    /// <summary>Set once the result is recorded.</summary>
    public int? WinnerParticipantId { get; set; }

    /// <summary>A walkover (only one team) — auto-advances and scores no win.</summary>
    public bool IsBye { get; set; }

    /// <summary>The court this match is played on, if assigned.</summary>
    public int? CourtId { get; set; }
    public Court? Court { get; set; }
}
