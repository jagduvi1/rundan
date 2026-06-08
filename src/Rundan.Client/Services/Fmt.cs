using System.Globalization;

namespace Rundan.Client.Services;

/// <summary>Display helpers for decimal scores: whole numbers show plain, halves show one/two decimals.</summary>
public static class Fmt
{
    public static string Num(double v) =>
        v % 1 == 0 ? ((long)v).ToString() : v.ToString("0.##", CultureInfo.InvariantCulture);
}
