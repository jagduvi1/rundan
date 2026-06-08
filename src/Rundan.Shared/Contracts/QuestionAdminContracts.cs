namespace Rundan.Shared.Contracts;

/// <summary>Admin view of an option, including correctness (never sent to players mid-game).</summary>
public class AnswerOptionAdminDto
{
    public int Id { get; set; }
    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
}

/// <summary>Admin view of a question with the answer key, used on the management screen.</summary>
public class QuestionAdminDto
{
    public int Id { get; set; }
    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;
    public QuestionKind Kind { get; set; }
    public int Points { get; set; }
    public string? ImageUrl { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? RadiusMeters { get; set; }
    public string? AcceptedFreeTextAnswer { get; set; }
    public List<AnswerOptionAdminDto> Options { get; set; } = new();
}
