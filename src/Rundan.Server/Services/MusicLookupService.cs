using System.Text.Json;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Services;

/// <summary>
/// Auto-fills a Spotify track's title/artist/year for the host, using only free public sources —
/// Spotify's oEmbed endpoint (the title, no login) and MusicBrainz (the artist + release year). Every
/// call is best-effort with a short timeout: anything it can't find is left null for the host to type.
/// (A logged-in-Spotify path can be layered on later; this needs no account.)
/// </summary>
public sealed class MusicLookupService(IHttpClientFactory httpFactory)
{
    public async Task<MusicLookupResultDto> LookupAsync(string spotifyUrl, CancellationToken ct = default)
    {
        var result = new MusicLookupResultDto();
        if (TryGetTrackId(spotifyUrl) is not { } trackId)
        {
            return result; // not a recognizable Spotify track link — nothing to look up
        }

        var http = httpFactory.CreateClient("music");

        // 1) Title via Spotify oEmbed (public, no auth).
        try
        {
            var canonical = $"https://open.spotify.com/track/{trackId}";
            var json = await http.GetStringAsync($"https://open.spotify.com/oembed?url={Uri.EscapeDataString(canonical)}", ct);
            result.Title = ParseOEmbedTitle(json);
        }
        catch
        {
            // oEmbed unavailable — carry on, MusicBrainz may still find the title via the artist.
        }

        // 2) Artist + year via MusicBrainz (needs a title to search on).
        if (!string.IsNullOrWhiteSpace(result.Title))
        {
            try
            {
                var query = Uri.EscapeDataString($"recording:\"{result.Title}\"");
                var json = await http.GetStringAsync(
                    $"https://musicbrainz.org/ws/2/recording/?query={query}&fmt=json&limit=5", ct);
                var (artist, year) = ParseMusicBrainz(json);
                result.Artist = artist;
                result.Year = year;
            }
            catch
            {
                // MusicBrainz unavailable — keep the title we found.
            }
        }

        result.Source = result.Found ? "oEmbed + MusicBrainz" : "none";
        return result;
    }

    /// <summary>Pulls the 22-char track id out of a Spotify link or URI (open.spotify.com/track/…,
    /// spotify:track:…, with optional /intl-xx/ locale and ?si= query). Null if it isn't one.</summary>
    public static string? TryGetTrackId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var s = url.Trim();

        // spotify:track:ID  or  …/track/ID
        var idx = s.IndexOf("track:", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return Clean(s[(idx + "track:".Length)..]);
        }

        idx = s.IndexOf("/track/", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return Clean(s[(idx + "/track/".Length)..]);
        }

        return null;

        // Trim a trailing query/fragment/path and validate the base-62 id shape.
        static string? Clean(string rest)
        {
            var end = rest.IndexOfAny(new[] { '?', '/', '#', '&' });
            var id = (end >= 0 ? rest[..end] : rest).Trim();
            return id.Length is >= 16 and <= 40 && id.All(char.IsLetterOrDigit) ? id : null;
        }
    }

    /// <summary>The "title" field of a Spotify oEmbed response (the track name).</summary>
    public static string? ParseOEmbedTitle(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
                ? NullIfBlank(t.GetString())
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Best recording match's artist + release year from a MusicBrainz recording search.</summary>
    public static (string? Artist, int? Year) ParseMusicBrainz(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("recordings", out var recs) || recs.ValueKind != JsonValueKind.Array
                || recs.GetArrayLength() == 0)
            {
                return (null, null);
            }

            var best = recs[0]; // results come back sorted by score
            string? artist = null;
            if (best.TryGetProperty("artist-credit", out var ac) && ac.ValueKind == JsonValueKind.Array && ac.GetArrayLength() > 0
                && ac[0].TryGetProperty("name", out var an))
            {
                artist = NullIfBlank(an.GetString());
            }

            int? year = null;
            if (best.TryGetProperty("first-release-date", out var d) && d.ValueKind == JsonValueKind.String
                && d.GetString() is { Length: >= 4 } date && int.TryParse(date[..4], out var y) && y is >= 1860 and <= 2100)
            {
                year = y;
            }

            return (artist, year);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
