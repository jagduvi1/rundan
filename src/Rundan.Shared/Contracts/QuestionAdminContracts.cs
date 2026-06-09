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

    /// <summary>This question has a geofence (a placed tipspromenad station).</summary>
    public bool HasLocation => Latitude.HasValue && Longitude.HasValue;

    /// <summary>
    /// True once the question has enough content to be played — some text plus a valid answer key.
    /// Blank stations (created by setting a station count) are incomplete until the host fills them in.
    /// </summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Text) &&
        (Kind == QuestionKind.FreeText
            ? !string.IsNullOrWhiteSpace(AcceptedFreeTextAnswer)
            : Options.Count >= 2
              && Options.Count(o => o.IsCorrect) == 1
              && Options.All(o => !string.IsNullOrWhiteSpace(o.Text)));
}

/// <summary>Sets how many stations (questions) a tipspromenad has; adds blank stations or trims empty ones.</summary>
public class SetStationCountRequest
{
    public int Count { get; set; }
}
