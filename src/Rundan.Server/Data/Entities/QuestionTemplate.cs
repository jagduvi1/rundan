using Rundan.Shared;

namespace Rundan.Server.Data.Entities;

/// <summary>
/// A reusable question in the pre-generated library. The host pulls random ones (filtered by tag)
/// into a quiz/tipspromenad so they don't author — or memorise — them. Not AI-generated at runtime.
/// </summary>
public class QuestionTemplate
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public QuestionKind Kind { get; set; } = QuestionKind.MultipleChoice;
    public int Points { get; set; } = 1;
    public string? AcceptedFreeTextAnswer { get; set; }

    public List<QuestionTemplateOption> Options { get; set; } = new();
    public List<QuestionTemplateTag> Tags { get; set; } = new();
}

/// <summary>An answer option for a library question.</summary>
public class QuestionTemplateOption
{
    public int Id { get; set; }
    public int QuestionTemplateId { get; set; }
    public QuestionTemplate? QuestionTemplate { get; set; }
    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
}

/// <summary>A normalised tag on a library question (e.g. "age:family", "topic:geography").</summary>
public class QuestionTemplateTag
{
    public int Id { get; set; }
    public int QuestionTemplateId { get; set; }
    public QuestionTemplate? QuestionTemplate { get; set; }
    public string Tag { get; set; } = string.Empty;
}

/// <summary>Marks a library question as already used by the host, so it isn't picked again.</summary>
public class QuestionTemplateUsage
{
    public int Id { get; set; }
    public int QuestionTemplateId { get; set; }
    public DateTimeOffset UsedUtc { get; set; }
}
