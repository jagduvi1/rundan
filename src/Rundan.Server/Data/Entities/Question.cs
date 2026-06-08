using Rundan.Shared;

namespace Rundan.Server.Data.Entities;

/// <summary>A question in a Quiz or Tipspromenad. Geo fields are used only by Tipspromenad.</summary>
public class Question
{
    public int Id { get; set; }
    public int ActivityId { get; set; }
    public Activity? Activity { get; set; }

    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;
    public QuestionKind Kind { get; set; }
    public int Points { get; set; } = 1;
    public string? ImageUrl { get; set; }

    // Geo location (Tipspromenad). Null for plain quizzes.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? RadiusMeters { get; set; }

    /// <summary>Accepted answer for free-text questions (compared case-insensitively, trimmed).</summary>
    public string? AcceptedFreeTextAnswer { get; set; }

    public List<AnswerOption> Options { get; set; } = new();
    public List<Answer> Answers { get; set; } = new();
}
