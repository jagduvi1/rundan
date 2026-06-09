namespace Rundan.Shared.Contracts;

/// <summary>A player's answer submission for one question.</summary>
public class SubmitAnswerRequest
{
    public int QuestionId { get; set; }

    /// <summary>Chosen option id for multiple-choice / true-false.</summary>
    public int? SelectedOptionId { get; set; }

    /// <summary>Free-text answer; for a MusicQuiz track this is the SONG guess.</summary>
    public string? FreeText { get; set; }

    /// <summary>MusicQuiz: the artist guess.</summary>
    public string? ArtistText { get; set; }
}

/// <summary>A participant's own previously-submitted answer (used to restore UI state on reconnect).</summary>
public class MyAnswerDto
{
    public int QuestionId { get; set; }
    public int? SelectedOptionId { get; set; }
    public string? FreeText { get; set; }
    public string? ArtistText { get; set; }
    public bool IsCorrect { get; set; }
    public int AwardedPoints { get; set; }
}

/// <summary>Immediate result of a submission (instant casual feedback).</summary>
public class AnswerResultDto
{
    public int QuestionId { get; set; }
    public bool IsCorrect { get; set; }
    public int AwardedPoints { get; set; }

    /// <summary>How many of this activity's questions this participant has now answered.</summary>
    public int AnsweredCount { get; set; }
    public int TotalQuestions { get; set; }

    /// <summary>MusicQuiz: per-field result + the revealed answers (returned only to the team that answered).</summary>
    public bool SongCorrect { get; set; }
    public bool ArtistCorrect { get; set; }
    public string? CorrectSong { get; set; }
    public string? CorrectArtist { get; set; }
}
