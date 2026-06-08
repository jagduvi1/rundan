namespace Rundan.Server.Data.Entities;

/// <summary>A selectable option for a multiple-choice / true-false question.</summary>
public class AnswerOption
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public Question? Question { get; set; }

    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
}
