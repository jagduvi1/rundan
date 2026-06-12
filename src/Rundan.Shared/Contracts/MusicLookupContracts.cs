namespace Rundan.Shared.Contracts;

/// <summary>Host asks the server to look up a Spotify track's metadata for auto-fill.</summary>
public class MusicLookupRequest
{
    /// <summary>A Spotify track link or URI (e.g. https://open.spotify.com/track/… or spotify:track:…).</summary>
    public string SpotifyUrl { get; set; } = string.Empty;
}

/// <summary>What auto-fill found — any field may be null. The host confirms/edits before saving.</summary>
public class MusicLookupResultDto
{
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public int? Year { get; set; }

    /// <summary>Where the data came from, for a small UI hint ("Spotify", "oEmbed + MusicBrainz", or "none").</summary>
    public string Source { get; set; } = "none";

    public bool Found => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Artist) || Year.HasValue;
}

/// <summary>Host bulk-imports tracks from a Spotify playlist (via a saved Spotify connection).</summary>
public class MusicImportRequest
{
    /// <summary>A Spotify playlist link or URI.</summary>
    public string PlaylistUrl { get; set; } = string.Empty;

    /// <summary>How many tracks to add (a random pick from the playlist).</summary>
    public int Count { get; set; } = 10;
}

/// <summary>Result of a playlist import.</summary>
public class MusicImportResultDto
{
    public int Imported { get; set; }
}
