namespace Rundan.Server.Data.Entities;

/// <summary>A participant's submission for one question (one row per participant per question).</summary>
public class Answer
{
    public int Id { get; set; }

    public int QuestionId { get; set; }
    public Question? Question { get; set; }

    public int ParticipantId { get; set; }
    public Participant? Participant { get; set; }

    public int? SelectedOptionId { get; set; }
    public AnswerOption? SelectedOption { get; set; }

    /// <summary>Free-text answer; for a MusicQuiz track this is the SONG guess.</summary>
    public string? FreeText { get; set; }

    /// <summary>MusicQuiz: the artist guess.</summary>
    public string? ArtistText { get; set; }

    /// <summary>MusicQuiz (Hitster mode): the player's guessed release year.</summary>
    public int? GuessedYear { get; set; }

    public bool IsCorrect { get; set; }
    public int AwardedPoints { get; set; }
    public DateTimeOffset SubmittedUtc { get; set; }
}
