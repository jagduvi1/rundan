using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Endpoints;

/// <summary>Music-quiz helpers: auto-fill a track's metadata from a Spotify link (free public sources).</summary>
internal static class MusicEndpoints
{
    public static void MapMusicEndpoints(this IEndpointRouteBuilder app)
    {
        // Host design-time helper — look up a track's title/artist/year for auto-fill.
        app.MapPost("/api/music/lookup", async (MusicLookupRequest req, MusicLookupService svc, CancellationToken ct) =>
            Results.Ok(await svc.LookupAsync(req.SpotifyUrl, ct)))
           .AddEndpointFilter<AdminEndpointFilter>();
    }
}
