namespace Rundan.Shared.Contracts;

/// <summary>The letter tiles and rules for the word game, plus this team's result if submitted.</summary>
public class WordGameDto
{
    /// <summary>The 20 face-down letters (same set every time for this team).</summary>
    public List<string> Tiles { get; set; } = new();

    /// <summary>How many tiles a team may open.</summary>
    public int MaxOpen { get; set; }

    /// <summary>Seconds on the clock.</summary>
    public int Seconds { get; set; }

    /// <summary>The team's submitted word, if they've played.</summary>
    public string? SubmittedWord { get; set; }

    /// <summary>The submitted word's length (= the team's score), if played.</summary>
    public int? SubmittedScore { get; set; }
}

/// <summary>Submits a team's word, built from the tiles they opened.</summary>
public class SubmitWordRequest
{
    public List<int> OpenedIndices { get; set; } = new();
    public string Word { get; set; } = string.Empty;
}
