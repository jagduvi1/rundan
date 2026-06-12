using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Endpoints;

/// <summary>Site-level Spotify connections (logged-in Premium accounts) for music-quiz auto-fill.</summary>
internal static class SpotifyEndpoints
{
    public static void MapSpotifyEndpoints(this IEndpointRouteBuilder app)
    {
        // Set (or clear) the Spotify app Client ID from the UI — stored server-side, overrides env config.
        app.MapPut("/api/spotify/client-id", async (SetSpotifyClientIdRequest req, SpotifyService svc, CancellationToken ct) =>
        {
            await svc.SetClientIdAsync(req.ClientId, ct);
            return Results.NoContent();
        }).AddEndpointFilter<AdminEndpointFilter>();

        // Finish the browser OAuth: exchange the code (PKCE) for tokens and save a named connection.
        app.MapPost("/api/spotify/connect", async (SpotifyConnectRequest req, SpotifyService svc, CancellationToken ct) =>
            Results.Ok(await svc.ConnectAsync(req.Code, req.CodeVerifier, req.RedirectUri, ct)))
           .AddEndpointFilter<AdminEndpointFilter>();

        app.MapGet("/api/spotify/connections", async (SpotifyService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListAsync(ct)))
           .AddEndpointFilter<AdminEndpointFilter>();

        // Validate: refresh the token + a quick /me call, report whether it still works.
        app.MapPost("/api/spotify/connections/{id:int}/validate", async (int id, SpotifyService svc, CancellationToken ct) =>
            Results.Ok(await svc.ValidateAsync(id, ct)))
           .AddEndpointFilter<AdminEndpointFilter>();

        app.MapDelete("/api/spotify/connections/{id:int}", async (int id, SpotifyService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, ct);
            return Results.NoContent();
        }).AddEndpointFilter<AdminEndpointFilter>();

        // A short-lived access token for the host's browser to run the Web Playback SDK (full playback).
        app.MapGet("/api/spotify/connections/{id:int}/token", async (int id, SpotifyService svc, CancellationToken ct) =>
        {
            var token = await svc.GetAccessTokenAsync(id, ct);
            return token is null ? Results.NotFound() : Results.Ok(new SpotifyTokenDto { AccessToken = token });
        }).AddEndpointFilter<AdminEndpointFilter>();
    }
}
