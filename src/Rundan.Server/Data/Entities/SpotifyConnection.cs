namespace Rundan.Server.Data.Entities;

/// <summary>
/// A saved Spotify login (OAuth tokens for a Premium account) used to auto-fill music-quiz tracks with
/// exact title/artist/year. Created once from the UI and reused across activities. The tokens are
/// server-only and never leave the API.
/// </summary>
public class SpotifyConnection
{
    public int Id { get; set; }

    /// <summary>Friendly name — defaults to the Spotify display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The Spotify account id (for reference / dedupe).</summary>
    public string SpotifyUserId { get; set; } = string.Empty;

    /// <summary>Long-lived refresh token used to mint new access tokens.</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>The current access token and when it expires (refreshed on demand).</summary>
    public string AccessToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresUtc { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Outcome of the last validation ("valid" / an error), shown in the connections list.</summary>
    public string? LastStatus { get; set; }
}
