using Rundan.Shared;

namespace Rundan.Server.Data.Entities;

/// <summary>An activity instance (a quiz, a quiz-walk, a boule game, ...).</summary>
public class Activity
{
    public int Id { get; set; }

    /// <summary>Optional parent event. Null for a standalone activity.</summary>
    public int? EventId { get; set; }
    public Event? Event { get; set; }

    /// <summary>Position in the event's running order (1, 2, 3 …). 0 for standalone.</summary>
    public int Order { get; set; }

    public ActivityType Type { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>Free-text rules / info shown to players for this game.</summary>
    public string? Description { get; set; }

    /// <summary>Optional descriptive picture or map for this game.</summary>
    public string? ImageUrl { get; set; }

    public ActivityStatus Status { get; set; } = ActivityStatus.Draft;

    /// <summary>Unique, short, human-typable join code.</summary>
    public string JoinCode { get; set; } = string.Empty;

    public ScoringMode ScoringMode { get; set; } = ScoringMode.HigherWins;

    /// <summary>What a score-game measures (points / time / length).</summary>
    public Measurement Measurement { get; set; } = Measurement.Points;

    /// <summary>Target value for <see cref="ScoringMode.ClosestToTarget"/> (e.g. 147 seconds = 2:27).</summary>
    public int? TargetValue { get; set; }

    /// <summary>Boule: how a match result is entered (free single score vs best-of sets).</summary>
    public MatchFormat MatchFormat { get; set; } = MatchFormat.Free;

    /// <summary>Boule sets mode: how many sets a match is (best of N — 1, 3 or 5).</summary>
    public int BestOfSets { get; set; } = 3;

    /// <summary>Boule sets mode: games needed to win a set (guidance for the result popup, e.g. 13 for pétanque).</summary>
    public int GamesToWinSet { get; set; } = 13;

    // ---- Tournament (knockout) advanced options: optional round-robin group stage + seeding ----

    /// <summary>Play a round-robin group stage before the knockout (the "primary round").</summary>
    public bool UseGroupStage { get; set; }

    /// <summary>How many groups to split the teams into. 0 = let the app suggest the best split.</summary>
    public int GroupCount { get; set; }

    /// <summary>Group-stage match format (can be shorter than the playoffs).</summary>
    public MatchFormat GroupMatchFormat { get; set; } = MatchFormat.Free;
    public int GroupBestOfSets { get; set; } = 1;
    public int GroupGamesToWinSet { get; set; } = 13;

    /// <summary>How many teams from each group advance to Playoff A (championship bracket).</summary>
    public int AdvanceToPlayoffA { get; set; } = 2;

    /// <summary>How many teams from each group advance to Playoff B (plate). 0 = no Playoff B.</summary>
    public int AdvanceToPlayoffB { get; set; }

    /// <summary>Give Playoff A a losers' consolation bracket (the default single-knockout behaviour).</summary>
    public bool PlayoffAConsolation { get; set; } = true;

    /// <summary>Give Playoff B a losers' consolation bracket.</summary>
    public bool PlayoffBConsolation { get; set; }

    /// <summary>Seed teams manually (host ranks them) instead of a random draw.</summary>
    public bool UseManualSeeding { get; set; }

    /// <summary>How tournament results become the activity score (per-win points vs final placement).</summary>
    public TournamentScoring TournamentScoring { get; set; } = TournamentScoring.PerWin;

    /// <summary>For question activities: serve the questions in a random order per player.</summary>
    public bool RandomizeQuestions { get; set; }

    /// <summary>MusicQuiz: play as multiple choice (pick the artist from 4 options, Kahoot-style) instead
    /// of typing the song + artist. Options are generated when the activity starts.</summary>
    public bool MusicChoices { get; set; }

    /// <summary>For question activities: blank out the question text/answers in the host's management
    /// view (so a host who's also playing doesn't spoil library-pulled questions). Players still see them.</summary>
    public bool HideQuestionsFromHost { get; set; }

    /// <summary>If true, this activity's definition is reusable from the library in other events.</summary>
    public bool IsPublic { get; set; }

    /// <summary>What the playing surfaces are called (Court / Field / Track / Lane).</summary>
    public string CourtLabel { get; set; } = "Court";

    /// <summary>The playing surfaces for this activity.</summary>
    public List<Court> Courts { get; set; } = new();

    /// <summary>Score-game format: Team = the whole team plays each round (one score per round);
    /// PerPlayer = each player plays a round (one score per player, rounds = team size).</summary>
    public ScoreEntryMode ScoreEntryMode { get; set; } = ScoreEntryMode.Team;

    /// <summary>Score game (Team format): how many rounds the team plays. PerPlayer derives rounds
    /// from the team size instead, so this is ignored there.</summary>
    public int RoundCount { get; set; } = 1;

    /// <summary>Score game: how many players take part in each round (guidance shown to host/players).</summary>
    public int? PlayersPerRound { get; set; }

    /// <summary>Optional GPS geofence — when set, the activity unlocks on a player's phone within this radius.</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int? RadiusMeters { get; set; }

    /// <summary>MapPin only: how many cities to draw for the game (defaults to 5 when null).</summary>
    public int? MapCityCount { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }

    public List<Participant> Participants { get; set; } = new();
    public List<Question> Questions { get; set; } = new();
    public List<ScoreEntry> ScoreEntries { get; set; } = new();
    public List<MapCity> MapCities { get; set; } = new();
}
