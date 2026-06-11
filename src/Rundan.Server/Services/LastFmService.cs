using System.Collections.Concurrent;
using System.Text.Json;

namespace Rundan.Server.Services;

/// <summary>
/// Optional Last.fm lookup of "similar artists", used to make the wrong options in a multiple-choice
/// music quiz feel like real near-misses. Entirely opt-in: with no <c>LastFmApiKey</c> configured it's
/// disabled and the quiz falls back to its own artists + the built-in pool. Results are cached in memory
/// (this is a singleton) so a busy quiz hits the network at most once per artist, and every call is
/// best-effort — any failure just yields no suggestions.
/// </summary>
public sealed class LastFmService(IHttpClientFactory httpFactory, RundanOptions options)
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public bool Enabled => options.HasLastFm;

    public async Task<IReadOnlyList<string>> SimilarAsync(string artist, CancellationToken ct = default)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(artist))
        {
            return Array.Empty<string>();
        }

        if (_cache.TryGetValue(artist, out var cached))
        {
            return cached;
        }

        IReadOnlyList<string> result;
        try
        {
            var http = httpFactory.CreateClient("music");
            var url = "https://ws.audioscrobbler.com/2.0/?method=artist.getsimilar"
                      + $"&artist={Uri.EscapeDataString(artist)}&api_key={Uri.EscapeDataString(options.LastFmApiKey!)}"
                      + "&autocorrect=1&format=json&limit=12";
            var json = await http.GetStringAsync(url, ct);
            result = ParseSimilar(json);
        }
        catch
        {
            result = Array.Empty<string>(); // network/parse failure — cache the empty so we don't retry in a loop
        }

        _cache[artist] = result;
        return result;
    }

    /// <summary>Artist names from a Last.fm artist.getsimilar response (similarartists.artist[].name).</summary>
    public static IReadOnlyList<string> ParseSimilar(string json)
    {
        var names = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("similarartists", out var sa)
                && sa.TryGetProperty("artist", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in arr.EnumerateArray())
                {
                    if (a.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                        && n.GetString() is { Length: > 0 } name)
                    {
                        names.Add(name.Trim());
                    }
                }
            }
        }
        catch
        {
            // malformed response — return whatever we managed to read (usually nothing)
        }

        return names;
    }
}
