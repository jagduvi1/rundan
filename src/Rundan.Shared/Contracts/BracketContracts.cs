namespace Rundan.Shared.Contracts;

/// <summary>A knockout bracket for a Boule activity (winners' and losers' sides).</summary>
public class BracketDto
{
    public int ActivityId { get; set; }
    public bool Drawn { get; set; }
    public bool Complete { get; set; }
    public string? ChampionName { get; set; }
    public List<BracketMatchDto> Matches { get; set; } = new();
}

/// <summary>One match in the bracket tree.</summary>
public class BracketMatchDto
{
    public int Id { get; set; }
    public BracketSide Side { get; set; }
    public int Round { get; set; }
    public int Slot { get; set; }

    public int? AId { get; set; }
    public string? AName { get; set; }
    public int? BId { get; set; }
    public string? BName { get; set; }

    public int? WinnerParticipantId { get; set; }
    public bool IsBye { get; set; }

    /// <summary>The court/lane this match is played on, if assigned.</summary>
    public string? CourtName { get; set; }

    /// <summary>The recorded score, formatted for display (e.g. "13–7, 9–13, 13–10" or "3–1"); null if none.</summary>
    public string? Score { get; set; }

    public bool Ready => AId.HasValue && BId.HasValue && !IsBye;
    public bool Decided => WinnerParticipantId.HasValue;
}

/// <summary>One set's score in a recorded match (A vs B). A single entry is used for free scoring.</summary>
public class MatchSetInputDto
{
    public int A { get; set; }
    public int B { get; set; }
}

/// <summary>
/// Records a bracket match result (host / event admin). The winner is derived from the set scores
/// per the activity's match format (free single score, or best-of-N sets).
/// </summary>
public class RecordBracketResultRequest
{
    public int MatchId { get; set; }
    public List<MatchSetInputDto> Sets { get; set; } = new();
}
