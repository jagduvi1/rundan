namespace Rundan.Client.Services;

/// <summary>
/// Deterministic per-player question shuffle. The order is stable for a given player
/// (seeded from their session token) so it doesn't reshuffle between renders, but differs
/// between players so neighbours can't simply copy answers.
/// </summary>
public static class QuestionShuffle
{
    public static void Shuffle<T>(List<T> list, Guid seedSource)
    {
        var seed = 17;
        foreach (var b in seedSource.ToByteArray())
        {
            seed = unchecked((seed * 31) + b);
        }

        var rng = new Random(seed);
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
