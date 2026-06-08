using System.Globalization;
using Rundan.Shared;

namespace Rundan.Client.Services;

/// <summary>Display helpers for decimal scores: whole numbers show plain, halves show one/two decimals.</summary>
public static class Fmt
{
    public static string Num(double v) =>
        v % 1 == 0 ? ((long)v).ToString() : v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Human label for an activity type (used across the welcome, event, activity and manage views).</summary>
    public static string TypeLabel(ActivityType type) => type switch
    {
        ActivityType.Quiz => "Quiz",
        ActivityType.Tipspromenad => "Tipspromenad",
        ActivityType.Boule => "Boule",
        ActivityType.ScoreGame => "Score game",
        ActivityType.WordGame => "Word game",
        _ => type.ToString(),
    };
}
