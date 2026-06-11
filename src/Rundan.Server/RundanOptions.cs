namespace Rundan.Server;

/// <summary>
/// App configuration, bound from the "Rundan" section of configuration
/// (appsettings.json / environment variables / App Service settings).
/// </summary>
public class RundanOptions
{
    public const string SectionName = "Rundan";

    /// <summary>Friendly app name shown in the UI.</summary>
    public string AppName { get; set; } = "Rundan";

    /// <summary>
    /// Shared site password. If set, every API/SignalR call must present it
    /// (header <c>X-Rundan-Access</c> or SignalR access token). Empty = open (dev only).
    /// </summary>
    public string? AccessCode { get; set; }

    /// <summary>
    /// Admin password required to create/manage activities. Empty = anyone may
    /// administer (dev only).
    /// </summary>
    public string? AdminCode { get; set; }

    /// <summary>
    /// Optional explicit path to the SQLite file. If empty, the path is derived
    /// from the App Service persistent <c>HOME</c> directory.
    /// </summary>
    public string? DatabasePath { get; set; }

    /// <summary>If true, populates sample data (one event with several activities) on startup when the database is empty.</summary>
    public bool SeedOnStartup { get; set; }

    /// <summary>Confirmation code the host types to run the destructive clean-and-seed wipe. Defaults
    /// to "CALLE"; override it (e.g. <c>Rundan__SeedCode</c>) for a real secret that isn't in the source.</summary>
    public string SeedCode { get; set; } = "CALLE";

    // --- Optional music-quiz integrations (all degrade gracefully when unset; never required) ---

    /// <summary>Optional Last.fm API key. When set, multiple-choice music quizzes use real "similar
    /// artists" as wrong options; otherwise the other artists in the quiz (plus a built-in pool) are used.</summary>
    public string? LastFmApiKey { get; set; }

    /// <summary>Optional Spotify app client id (public; used by the browser PKCE login so a host with
    /// Spotify Premium can auto-fill track title/artist/year). When unset, auto-fill falls back to the
    /// free public oEmbed + MusicBrainz lookup, and finally to typing it manually.</summary>
    public string? SpotifyClientId { get; set; }

    public bool RequiresAccessCode => !string.IsNullOrWhiteSpace(AccessCode);
    public bool RequiresAdminCode => !string.IsNullOrWhiteSpace(AdminCode);

    /// <summary>True when a Last.fm key is configured (enables the "similar artists" distractors).</summary>
    public bool HasLastFm => !string.IsNullOrWhiteSpace(LastFmApiKey);
}
