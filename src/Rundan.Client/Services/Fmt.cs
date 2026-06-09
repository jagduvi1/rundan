using System.Globalization;
using System.Net;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Client.Services;

/// <summary>Display helpers for decimal scores: whole numbers show plain, halves show one/two decimals.</summary>
public static class Fmt
{
    public static string Num(double v) =>
        v % 1 == 0 ? ((long)v).ToString() : v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// Renders the "Rules / info" text for display. New content is already sanitised HTML (from the
    /// rich-text editor); legacy plain text is HTML-encoded with line breaks preserved. The result is
    /// meant to be wrapped in (MarkupString) inside an element with the .rte-content class.
    /// </summary>
    public static string RichHtml(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return text.Contains('<') ? text : WebUtility.HtmlEncode(text).Replace("\n", "<br>");
    }

    /// <summary>
    /// A short, auto-generated summary of how an activity is played and scored, derived from its
    /// type and scoring settings. Shown to players and previewed by the host under the rules text.
    /// </summary>
    public static List<string> RulesSummary(ActivityDto a)
    {
        var lines = new List<string>();
        switch (a.Type)
        {
            case ActivityType.Tipspromenad:
                lines.Add("Walk the route and answer each station's question when you reach it.");
                if (a.RandomizeQuestions)
                {
                    lines.Add("Stations are served in a random order per team.");
                }

                lines.Add("Your team answers together — each correct answer scores that question's points.");
                break;

            case ActivityType.Quiz:
                lines.Add("Answer each question in turn.");
                if (a.RandomizeQuestions)
                {
                    lines.Add("Questions are served in a random order per team.");
                }

                lines.Add("Your team answers together — each correct answer scores that question's points.");
                break;

            case ActivityType.Boule:
                lines.Add("Knockout bracket: win your match to climb the winners' side; the round-one losers play a losers' bracket.");
                lines.Add(a.MatchFormat == MatchFormat.Sets
                    ? $"Each match is best of {a.BestOfSets} set{(a.BestOfSets == 1 ? "" : "s")} — first to {a.GamesToWinSet} takes a set, and the most sets wins."
                    : "Each match is a single game — the higher score wins.");
                lines.Add("Scoring: 3 points for each winners'-side win, 1 point for each losers'-side win.");
                break;

            case ActivityType.WordGame:
                lines.Add("Open a few of the face-down letter tiles, then build the longest word you can from them.");
                lines.Add("Your score is the length of your word.");
                break;

            case ActivityType.MapPin:
                lines.Add($"You'll get {a.MapCityCount ?? 5} cities to place on a map that has no place names.");
                lines.Add("Drop a pin where you think each city is — the distance from the real spot is your score.");
                lines.Add("Lowest total distance across all the cities wins.");
                break;

            default: // ScoreGame and any future round-based game
                var measure = a.Measurement switch
                {
                    Measurement.TimeSeconds => "a time",
                    Measurement.Millimetres => "a length",
                    _ => "points",
                };
                var win = a.ScoringMode switch
                {
                    ScoringMode.LowerWins => "the lowest wins (e.g. the fastest)",
                    ScoringMode.ClosestToTarget => "closest to the target wins",
                    _ => "the highest wins",
                };
                lines.Add($"Record each round's result as {measure} — {win}.");
                lines.Add(a.ScoreEntryMode == ScoreEntryMode.PerPlayer
                    ? "Each player records their own result; the team's total is the sum."
                    : "One result is recorded per team, per round.");
                break;
        }

        return lines;
    }

    /// <summary>Human label for an activity type (used across the welcome, event, activity and manage views).</summary>
    public static string TypeLabel(ActivityType type) => type switch
    {
        ActivityType.Quiz => "Quiz",
        ActivityType.Tipspromenad => "Tipspromenad",
        ActivityType.Boule => "Boule",
        ActivityType.ScoreGame => "Score game",
        ActivityType.WordGame => "Word game",
        ActivityType.MapPin => "Pin the city (map)",
        _ => type.ToString(),
    };
}
