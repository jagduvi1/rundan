using Rundan.Shared;

namespace Rundan.Server.Services;

/// <summary>Shared scoring helpers so the live scoreboard and the event standings rank identically.</summary>
internal static class ScoringHelper
{
    /// <summary>
    /// Sort key for a score under a scoring mode (lower key = better rank):
    /// higher-wins → -score, lower-wins → score, closest-to-target → distance from target.
    /// </summary>
    public static double RankKey(ScoringMode mode, double score, double target) => mode switch
    {
        ScoringMode.LowerWins => score,
        ScoringMode.ClosestToTarget => Math.Abs(score - target),
        _ => -score,
    };

    /// <summary>
    /// In lowest/closest games a team that hasn't recorded anything must not outrank real
    /// results with its seeded 0 — these modes push the unscored to the bottom.
    /// </summary>
    public static bool PushesUnscoredLast(ScoringMode mode) =>
        mode is ScoringMode.LowerWins or ScoringMode.ClosestToTarget;
}
