namespace Rundan.Shared.Contracts;

/// <summary>A finished question activity's full breakdown: every question with every player's answer.
/// Revealed once the activity is finished (the game's over, so the answers are no longer secret).</summary>
public class ActivitySummaryDto
{
    public List<SummaryQuestionDto> Questions { get; set; } = new();
}

/// <summary>One question (or music track) with the correct answer and what each player gave.</summary>
public class SummaryQuestionDto
{
    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;

    /// <summary>The correct answer, formatted for display (song — artist · year, the accepted text, or the right option).</summary>
    public string? Correct { get; set; }

    public List<SummaryAnswerDto> Answers { get; set; } = new();
}

/// <summary>What one player answered for one question.</summary>
public class SummaryAnswerDto
{
    public string Player { get; set; } = string.Empty;

    /// <summary>What they gave, formatted for display ("—" if they didn't answer).</summary>
    public string Given { get; set; } = "—";

    /// <summary>Right/wrong for quiz-style answers; null where there's no such thing (a distance, a time).</summary>
    public bool? IsCorrect { get; set; }

    public int Points { get; set; }
}
