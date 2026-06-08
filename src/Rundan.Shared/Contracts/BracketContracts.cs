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

    public bool Ready => AId.HasValue && BId.HasValue && !IsBye;
    public bool Decided => WinnerParticipantId.HasValue;
}

/// <summary>Records the winner of a single bracket match (host / event admin).</summary>
public class RecordBracketResultRequest
{
    public int MatchId { get; set; }
    public int WinnerParticipantId { get; set; }
}
