namespace Rundan.Shared;

/// <summary>
/// The kind of activity. New activity types can be added here; the generic
/// score-game machinery (rounds + score entries) supports most new games
/// without schema changes, and <see cref="Quiz"/>/<see cref="Tipspromenad"/>
/// share the question/answer machinery.
/// </summary>
public enum ActivityType
{
    /// <summary>Classic sit-down quiz: ordered questions, answered in sequence.</summary>
    Quiz = 1,

    /// <summary>Geo-located quiz walk. Questions are placed on a map and unlock when you reach them.</summary>
    Tipspromenad = 2,

    /// <summary>Boule / pétanque style: round-based points, summed into a total.</summary>
    Boule = 3,

    /// <summary>Generic score-tracked game (round-based points). Use for anything new.</summary>
    ScoreGame = 4,

    /// <summary>Word game: flip letter tiles and form the longest word against a timer.</summary>
    WordGame = 5,
}

/// <summary>Lifecycle of an activity.</summary>
public enum ActivityStatus
{
    /// <summary>Being set up by an admin (questions/players added). Not yet joinable by code.</summary>
    Draft = 0,

    /// <summary>Open lobby: people can join with the code and wait for the start.</summary>
    Open = 1,

    /// <summary>In progress: answering questions / recording scores.</summary>
    Live = 2,

    /// <summary>Finished: results are final, answers may be revealed.</summary>
    Finished = 3,
}

/// <summary>How a question is answered.</summary>
public enum QuestionKind
{
    /// <summary>One correct option out of several.</summary>
    MultipleChoice = 0,

    /// <summary>True / false (a two-option multiple choice).</summary>
    TrueFalse = 1,

    /// <summary>Free text, compared case-insensitively to the accepted answer.</summary>
    FreeText = 2,
}

/// <summary>How an event's combined total is calculated from its activities.</summary>
public enum EventScoring
{
    /// <summary>Sum the actual points each team scored across activities.</summary>
    Cumulative = 0,

    /// <summary>
    /// Placement points: in each finished activity, teams are ranked and awarded
    /// points by position (1st = number of teams, then 3, 2, 1), credited to each
    /// member. Every activity counts equally.
    /// </summary>
    Placement = 1,
}

/// <summary>
/// For score-game activities (boule etc.): how points are entered. Question activities
/// (quiz / tipspromenad) are always team-collaborative and ignore this.
/// </summary>
public enum ScoreEntryMode
{
    /// <summary>One score per team per round (the team plays as a unit).</summary>
    Team = 0,

    /// <summary>A score per player per round; the team's total is the sum of its players' points.</summary>
    PerPlayer = 1,
}

/// <summary>Which side of a knockout bracket a match belongs to.</summary>
public enum BracketSide
{
    /// <summary>Keep winning to reach the final.</summary>
    Winners = 0,

    /// <summary>First-round losers play here for the lower placements.</summary>
    Losers = 1,
}

/// <summary>What a score-game measures — drives the entry UI and the displayed unit.</summary>
public enum Measurement
{
    /// <summary>Plain count / points (a +/- stepper, summed over rounds).</summary>
    Points = 0,

    /// <summary>A duration in seconds (stopwatch or mm:ss entry, single value).</summary>
    TimeSeconds = 1,

    /// <summary>A length in millimetres (number entry, single value).</summary>
    Millimetres = 2,
}

/// <summary>For score games: how results are ranked.</summary>
public enum ScoringMode
{
    /// <summary>Highest total wins (boule to 13, most quiz points, ...).</summary>
    HigherWins = 0,

    /// <summary>Lowest total wins (fastest time, golf-like games).</summary>
    LowerWins = 1,

    /// <summary>Closest to a target value wins (e.g. closest to 2:27).</summary>
    ClosestToTarget = 2,
}
