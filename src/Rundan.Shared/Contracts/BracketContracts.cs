namespace Rundan.Shared.Contracts;

/// <summary>
/// A knockout tournament: an optional round-robin group stage feeding up to two seeded playoff
/// brackets (A = championship, B = plate), each with its own winners'/losers' sides.
/// </summary>
public class BracketDto
{
    public int ActivityId { get; set; }
    public bool Drawn { get; set; }

    /// <summary>The whole tournament has crowned its champion(s).</summary>
    public bool Complete { get; set; }

    /// <summary>Playoff A champion (the overall winner), once decided.</summary>
    public string? ChampionName { get; set; }

    /// <summary>Playoff B (plate) champion, if a Playoff B is configured and decided.</summary>
    public string? PlayoffBChampionName { get; set; }

    /// <summary>True when a round-robin group stage is in play (vs a straight knockout).</summary>
    public bool HasGroupStage { get; set; }

    /// <summary>True once every group match is decided and the playoffs have been seeded.</summary>
    public bool GroupStageComplete { get; set; }

    /// <summary>The group standings (empty for a straight knockout).</summary>
    public List<GroupStandingDto> Groups { get; set; } = new();

    public List<BracketMatchDto> Matches { get; set; } = new();
}

/// <summary>One group's round-robin standings.</summary>
public class GroupStandingDto
{
    /// <summary>0-based group index; shown as Group A / B / C…</summary>
    public int GroupIndex { get; set; }
    public List<GroupRowDto> Rows { get; set; } = new();
}

/// <summary>A team's row in a group table.</summary>
public class GroupRowDto
{
    public int TeamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Played { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }

    /// <summary>Total games/points scored for and against (for the difference tiebreak).</summary>
    public int PointsFor { get; set; }
    public int PointsAgainst { get; set; }
    public int Diff => PointsFor - PointsAgainst;

    /// <summary>1-based finishing position within the group.</summary>
    public int Rank { get; set; }

    /// <summary>Which playoff this position advances to: 0 = Playoff A, 1 = Playoff B, null = eliminated.</summary>
    public int? AdvancesToPool { get; set; }
}

/// <summary>One match in the bracket tree (or a round-robin group match when <see cref="GroupIndex"/> is set).</summary>
public class BracketMatchDto
{
    public int Id { get; set; }

    /// <summary>Which playoff bracket: 0 = Playoff A / the single knockout, 1 = Playoff B (plate).</summary>
    public int Pool { get; set; }

    /// <summary>Set for round-robin group matches (the 0-based group index); null for knockout matches.</summary>
    public int? GroupIndex { get; set; }

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

/// <summary>A team with its manual seed (1 = top seed). Used both to set and to display seeds.</summary>
public class TeamSeedDto
{
    public int TeamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Seed { get; set; }
}

/// <summary>Host sets the manual seeding order for a tournament's teams (before the draw).</summary>
public class SetSeedsRequest
{
    /// <summary>Team ids in rank order (first = seed 1).</summary>
    public List<int> TeamIdsInOrder { get; set; } = new();
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
