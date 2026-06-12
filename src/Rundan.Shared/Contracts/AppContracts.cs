namespace Rundan.Shared.Contracts;

/// <summary>Public app info the client fetches before the access-code gate.</summary>
public class BootstrapDto
{
    public string AppName { get; set; } = "Rundan";
    public bool RequiresAccessCode { get; set; }
    public bool RequiresAdminCode { get; set; }

    /// <summary>The configured Spotify app client id (public), or empty when no Spotify app is set up.
    /// The browser uses it to start the Premium login; empty hides the "Connect Spotify" UI.</summary>
    public string SpotifyClientId { get; set; } = string.Empty;
}

/// <summary>Body for the clean-and-seed wipe: the host's typed confirmation code (validated server-side).</summary>
public class CleanAndSeedRequest
{
    public string? Code { get; set; }
}
