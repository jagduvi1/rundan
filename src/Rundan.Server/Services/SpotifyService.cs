using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Services;

/// <summary>One track read from a Spotify playlist (for bulk import).</summary>
public sealed class PlaylistTrack
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public int? Year { get; init; }
    public string Url { get; init; } = string.Empty;
}

/// <summary>
/// Spotify Premium connections for music-quiz auto-fill. A host completes the browser OAuth (Authorization
/// Code + PKCE — no client secret) and the server exchanges the code for tokens, stores them as a reusable
/// named connection, and uses them to read exact track metadata (title / artist / release year). Access
/// tokens are refreshed on demand. All Spotify calls are best-effort and surface a clear message on failure.
/// </summary>
public sealed class SpotifyService(IHttpClientFactory httpFactory, RundanOptions options, AppDbContext db, TimeProvider clock)
{
    private const string TokenUrl = "https://accounts.spotify.com/api/token";
    private const string ApiBase = "https://api.spotify.com/v1";
    public const string ClientIdSettingKey = "Spotify.ClientId";

    private HttpClient Http => httpFactory.CreateClient("music");

    /// <summary>The effective Spotify app Client ID: the UI-saved setting if present, else the env config.</summary>
    public async Task<string?> ClientIdAsync(CancellationToken ct = default)
    {
        var fromDb = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == ClientIdSettingKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        var id = !string.IsNullOrWhiteSpace(fromDb) ? fromDb : options.SpotifyClientId;
        return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
    }

    /// <summary>Save (or clear, when blank) the Spotify Client ID set from the UI.</summary>
    public async Task SetClientIdAsync(string? clientId, CancellationToken ct = default)
    {
        var val = (clientId ?? string.Empty).Trim();
        var existing = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == ClientIdSettingKey, ct);
        if (val.Length == 0)
        {
            if (existing is not null) db.AppSettings.Remove(existing);
        }
        else if (existing is not null) existing.Value = val;
        else db.AppSettings.Add(new AppSetting { Key = ClientIdSettingKey, Value = val });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Exchanges the OAuth code for tokens (PKCE) and saves a new connection named after the account.</summary>
    public async Task<SpotifyConnectionDto> ConnectAsync(string code, string codeVerifier, string redirectUri, CancellationToken ct = default)
    {
        var clientId = await ClientIdAsync(ct)
            ?? throw new RuleViolationException("Spotify isn't set up yet (no Client ID).");

        var token = await ExchangeAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = codeVerifier,
        }, ct);

        if (string.IsNullOrEmpty(token.RefreshToken))
        {
            throw new RuleViolationException("Spotify didn't return a refresh token — try connecting again.");
        }

        var (userId, displayName) = await MeAsync(token.AccessToken, ct);

        var conn = new SpotifyConnection
        {
            Name = string.IsNullOrWhiteSpace(displayName) ? "Spotify account" : displayName,
            SpotifyUserId = userId ?? string.Empty,
            RefreshToken = token.RefreshToken!,
            AccessToken = token.AccessToken,
            ExpiresUtc = clock.GetUtcNow().AddSeconds(token.ExpiresIn - 30),
            CreatedUtc = clock.GetUtcNow(),
            LastStatus = "valid",
        };
        db.SpotifyConnections.Add(conn);
        await db.SaveChangesAsync(ct);
        return ToDto(conn);
    }

    public async Task<List<SpotifyConnectionDto>> ListAsync(CancellationToken ct = default) =>
        (await db.SpotifyConnections.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await db.SpotifyConnections.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
        // Activities that pointed at it fall back to the free oEmbed path automatically.
        await db.Activities.Where(a => a.SpotifyConnectionId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.SpotifyConnectionId, (int?)null), ct);
    }

    /// <summary>Re-checks a connection: refresh the token and call /me. Records + returns the outcome.</summary>
    public async Task<SpotifyValidateResultDto> ValidateAsync(int id, CancellationToken ct = default)
    {
        var conn = await db.SpotifyConnections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conn is null)
        {
            return new SpotifyValidateResultDto { Valid = false, Message = "Connection not found." };
        }

        try
        {
            await EnsureFreshAsync(conn, ct);
            var (_, name) = await MeAsync(conn.AccessToken, ct);
            conn.LastStatus = "valid";
            if (!string.IsNullOrWhiteSpace(name))
            {
                conn.Name = name;
            }

            await db.SaveChangesAsync(ct);
            return new SpotifyValidateResultDto { Valid = true, Message = "valid" };
        }
        catch (Exception ex)
        {
            conn.LastStatus = Short(ex.Message);
            await db.SaveChangesAsync(ct);
            return new SpotifyValidateResultDto { Valid = false, Message = conn.LastStatus! };
        }
    }

    /// <summary>A fresh access token for the host's browser to drive the Web Playback SDK. Null if the
    /// connection is gone; throws if the refresh fails.</summary>
    public async Task<string?> GetAccessTokenAsync(int connectionId, CancellationToken ct = default)
    {
        var conn = await db.SpotifyConnections.FirstOrDefaultAsync(c => c.Id == connectionId, ct);
        if (conn is null)
        {
            return null;
        }

        await EnsureFreshAsync(conn, ct);
        return conn.AccessToken;
    }

    /// <summary>Exact metadata for a track via a connection, or null if the connection is gone/failed.</summary>
    public async Task<MusicLookupResultDto?> GetTrackAsync(int connectionId, string trackId, CancellationToken ct = default)
    {
        var conn = await db.SpotifyConnections.FirstOrDefaultAsync(c => c.Id == connectionId, ct);
        if (conn is null)
        {
            return null;
        }

        try
        {
            await EnsureFreshAsync(conn, ct);
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/tracks/{trackId}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", conn.AccessToken);
            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            var title = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            string? artist = null;
            if (root.TryGetProperty("artists", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0
                && arr[0].TryGetProperty("name", out var an))
            {
                artist = an.GetString();
            }

            int? year = null;
            if (root.TryGetProperty("album", out var al) && al.TryGetProperty("release_date", out var rd)
                && rd.GetString() is { Length: >= 4 } date && int.TryParse(date[..4], out var y))
            {
                year = y;
            }

            return new MusicLookupResultDto { Title = title, Artist = artist, Year = year, Source = "Spotify" };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Extracts the playlist id from a Spotify playlist link or URI (…/playlist/ID, spotify:playlist:ID).</summary>
    public static string? TryGetPlaylistId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var s = url.Trim();
        var idx = s.IndexOf("playlist:", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return CleanId(s[(idx + "playlist:".Length)..]);
        }

        idx = s.IndexOf("/playlist/", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? CleanId(s[(idx + "/playlist/".Length)..]) : null;

        static string? CleanId(string rest)
        {
            var end = rest.IndexOfAny(new[] { '?', '/', '#', '&' });
            var id = (end >= 0 ? rest[..end] : rest).Trim();
            return id.Length is >= 16 and <= 40 && id.All(char.IsLetterOrDigit) ? id : null;
        }
    }

    /// <summary>Reads a playlist's tracks (title/artist/year + the track link), following pages up to
    /// <paramref name="max"/>. Null if the connection is gone; throws with a clear message if Spotify rejects it.</summary>
    public async Task<List<PlaylistTrack>?> GetPlaylistTracksAsync(int connectionId, string playlistId, int max, CancellationToken ct = default)
    {
        var conn = await db.SpotifyConnections.FirstOrDefaultAsync(c => c.Id == connectionId, ct);
        if (conn is null)
        {
            return null;
        }

        await EnsureFreshAsync(conn, ct);

        var all = new List<PlaylistTrack>();
        var fields = Uri.EscapeDataString("items(track(id,name,is_local,artists(name),album(release_date))),next");
        var url = $"{ApiBase}/playlists/{Uri.EscapeDataString(playlistId)}/tracks?limit=50&fields={fields}";
        for (var guard = 0; url is not null && all.Count < max && guard < 12; guard++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", conn.AccessToken);
            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                throw new RuleViolationException(
                    $"Couldn't read that playlist ({(int)resp.StatusCode}). Check the link and that the connected account can open it.");
            }

            var (tracks, next) = ParsePlaylistPage(await resp.Content.ReadAsStringAsync(ct));
            all.AddRange(tracks);
            url = next;
        }

        return all;
    }

    /// <summary>Parses one page of a playlist-tracks response into tracks (skipping local/empty entries) + the next-page URL.</summary>
    public static (List<PlaylistTrack> Tracks, string? Next) ParsePlaylistPage(string json)
    {
        var list = new List<PlaylistTrack>();
        string? next = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String)
            {
                next = n.GetString();
            }

            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in items.EnumerateArray())
                {
                    if (!it.TryGetProperty("track", out var t) || t.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (t.TryGetProperty("is_local", out var loc) && loc.ValueKind == JsonValueKind.True)
                    {
                        continue;
                    }

                    var id = t.TryGetProperty("id", out var idp) ? idp.GetString() : null;
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    string? artist = null;
                    if (t.TryGetProperty("artists", out var ar) && ar.ValueKind == JsonValueKind.Array && ar.GetArrayLength() > 0
                        && ar[0].TryGetProperty("name", out var an))
                    {
                        artist = an.GetString();
                    }

                    int? year = null;
                    if (t.TryGetProperty("album", out var al) && al.TryGetProperty("release_date", out var rd)
                        && rd.GetString() is { Length: >= 4 } d && int.TryParse(d[..4], out var y) && y is >= 1860 and <= 2100)
                    {
                        year = y;
                    }

                    list.Add(new PlaylistTrack
                    {
                        Title = t.TryGetProperty("name", out var nm) ? nm.GetString() : null,
                        Artist = artist,
                        Year = year,
                        Url = $"https://open.spotify.com/track/{id}",
                    });
                }
            }
        }
        catch
        {
            // malformed page — return whatever parsed
        }

        return (list, next);
    }

    // ---- internals ----

    private async Task EnsureFreshAsync(SpotifyConnection conn, CancellationToken ct)
    {
        if (clock.GetUtcNow() < conn.ExpiresUtc && !string.IsNullOrEmpty(conn.AccessToken))
        {
            return;
        }

        var clientId = await ClientIdAsync(ct)
            ?? throw new RuleViolationException("Spotify isn't set up yet (no Client ID).");
        var token = await ExchangeAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = conn.RefreshToken,
            ["client_id"] = clientId,
        }, ct);

        conn.AccessToken = token.AccessToken;
        conn.ExpiresUtc = clock.GetUtcNow().AddSeconds(token.ExpiresIn - 30);
        if (!string.IsNullOrEmpty(token.RefreshToken))
        {
            conn.RefreshToken = token.RefreshToken!; // Spotify may rotate it
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<(string AccessToken, int ExpiresIn, string? RefreshToken)> ExchangeAsync(
        Dictionary<string, string> form, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(form);
        using var resp = await Http.PostAsync(TokenUrl, content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new RuleViolationException($"Spotify rejected the login ({(int)resp.StatusCode}): {Short(body)}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
        if (string.IsNullOrEmpty(access))
        {
            throw new RuleViolationException("Spotify didn't return an access token.");
        }

        var expires = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var ei) ? ei : 3600;
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        return (access, expires, refresh);
    }

    private async Task<(string? Id, string? DisplayName)> MeAsync(string accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new RuleViolationException($"Spotify profile check failed ({(int)resp.StatusCode}).");
        }

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        return (root.TryGetProperty("id", out var id) ? id.GetString() : null,
                root.TryGetProperty("display_name", out var dn) ? dn.GetString() : null);
    }

    private static SpotifyConnectionDto ToDto(SpotifyConnection c) =>
        new() { Id = c.Id, Name = c.Name, CreatedUtc = c.CreatedUtc, Status = c.LastStatus };

    private static string Short(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "unknown error" : (s.Length > 200 ? s[..200] : s).Trim();
}
