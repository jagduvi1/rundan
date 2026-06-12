namespace Rundan.Shared.Contracts;

/// <summary>A saved Spotify connection (a logged-in Premium account) — reusable across music quizzes.
/// Tokens are kept server-side and never sent to the client.</summary>
public class SpotifyConnectionDto
{
    public int Id { get; set; }

    /// <summary>Friendly name (the Spotify display name, editable).</summary>
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Result of the last validation check, if one has run ("valid" / an error message / null).</summary>
    public string? Status { get; set; }
}

/// <summary>Browser hands the server the OAuth code (PKCE) to exchange for tokens and save a connection.</summary>
public class SpotifyConnectRequest
{
    public string Code { get; set; } = string.Empty;
    public string CodeVerifier { get; set; } = string.Empty;

    /// <summary>The exact redirect URI used in the authorize request (must match for the token exchange).</summary>
    public string RedirectUri { get; set; } = string.Empty;
}

/// <summary>Result of validating a connection (refresh the token + a quick API call).</summary>
public class SpotifyValidateResultDto
{
    public bool Valid { get; set; }

    /// <summary>"valid", or a short human-readable reason it failed.</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>Host sets (or clears) the Spotify app Client ID from the UI — stored server-side, overrides env.</summary>
public class SetSpotifyClientIdRequest
{
    public string? ClientId { get; set; }
}

/// <summary>A short-lived Spotify access token handed to the host's browser for the Web Playback SDK
/// (full-track playback in-app). Admin-only; the long-lived refresh token never leaves the server.</summary>
public class SpotifyTokenDto
{
    public string AccessToken { get; set; } = string.Empty;
}
