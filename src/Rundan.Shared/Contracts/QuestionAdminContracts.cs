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

    /// <summary>MusicQuiz: the Spotify link the host plays, and the correct artist (host-only).</summary>
    public string? SpotifyUrl { get; set; }
    public string? AcceptedArtist { get; set; }

    /// <summary>MusicQuiz (Hitster mode): the track's release year (host-only).</summary>
    public int? ReleaseYear { get; set; }

    public List<AnswerOptionAdminDto> Options { get; set; } = new();

    /// <summary>The text/answers are blanked because the activity hides questions from the host (host plays too).</summary>
    public bool Hidden { get; set; }

    /// <summary>This question has a geofence (a placed tipspromenad station).</summary>
    public bool HasLocation => Latitude.HasValue && Longitude.HasValue;

    /// <summary>
    /// True once the question has enough content to be played — some text plus a valid answer key.
    /// Blank stations (created by setting a station count) are incomplete until the host fills them in.
    /// A hidden question is assumed complete (its content is just blanked for the host).
    /// </summary>
    public bool IsComplete =>
        Hidden ||
        (!string.IsNullOrWhiteSpace(Text) &&
         (Kind == QuestionKind.FreeText
             ? !string.IsNullOrWhiteSpace(AcceptedFreeTextAnswer)
             : Options.Count >= 2
               && Options.Count(o => o.IsCorrect) == 1
               && Options.All(o => !string.IsNullOrWhiteSpace(o.Text))));
}

/// <summary>Sets how many stations (questions) a tipspromenad has; adds blank stations or trims empty ones.</summary>
public class SetStationCountRequest
{
    public int Count { get; set; }
}
