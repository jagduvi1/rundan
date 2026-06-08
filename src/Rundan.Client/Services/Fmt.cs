using System.Globalization;
using System.Net;
using Rundan.Shared;

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
