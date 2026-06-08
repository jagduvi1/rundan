namespace Rundan.Shared.Contracts;

/// <summary>An answer option as shown to players (no correctness leaked).</summary>
public class AnswerOptionDto
{
    public int Id { get; set; }
    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// A question as delivered to players. Correctness is deliberately omitted while
/// the activity is live; reveal it with <see cref="QuestionResultDto"/> afterwards.
/// </summary>
public class QuestionDto
{
    public int Id { get; set; }
    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;
    public QuestionKind Kind { get; set; }
    public int Points { get; set; } = 1;
    public string? ImageUrl { get; set; }

    // Geo fields — only set for Tipspromenad questions.
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? RadiusMeters { get; set; }

    public List<AnswerOptionDto> Options { get; set; } = new();

    public bool HasLocation => Latitude.HasValue && Longitude.HasValue;
}

/// <summary>One option in an admin create/update request.</summary>
public class AnswerOptionUpsert
{
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
}

/// <summary>Admin request to create or replace a question.</summary>
public class QuestionUpsertRequest
{
    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;
    public QuestionKind Kind { get; set; } = QuestionKind.MultipleChoice;
    public int Points { get; set; } = 1;
    public string? ImageUrl { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? RadiusMeters { get; set; }

    /// <summary>For multiple-choice / true-false questions.</summary>
    public List<AnswerOptionUpsert> Options { get; set; } = new();

    /// <summary>Accepted answer for free-text questions (compared case-insensitively, trimmed).</summary>
    public string? AcceptedFreeTextAnswer { get; set; }
}

/// <summary>Reveals a question and its correct answer (after the activity is finished).</summary>
public class QuestionResultDto
{
    public int QuestionId { get; set; }
    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;
    public QuestionKind Kind { get; set; }
    public int Points { get; set; }

    public int? CorrectOptionId { get; set; }
    public string? CorrectAnswerText { get; set; }

    /// <summary>All options (no correctness flag; the correct one is <see cref="CorrectOptionId"/>).</summary>
    public List<AnswerOptionDto> Options { get; set; } = new();
}

/// <summary>Sets (or clears) one question's GPS point + radius — the per-station map for a tipspromenad.</summary>
public class SetQuestionLocationRequest
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? RadiusMeters { get; set; }
}

/// <summary>
/// Host correction of a question's answer key after the fact (e.g. the wrong option was marked
/// correct). Applied in place — option ids are preserved — and re-scores every submitted answer.
/// </summary>
public class UpdateAnswerKeyRequest
{
    /// <summary>The option that should be correct (for multiple-choice / true-false).</summary>
    public int? CorrectOptionId { get; set; }

    /// <summary>The accepted answer (for free-text questions).</summary>
    public string? AcceptedFreeTextAnswer { get; set; }
}
