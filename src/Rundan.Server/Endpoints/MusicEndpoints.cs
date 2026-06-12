using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Endpoints;

/// <summary>Music-quiz helpers: auto-fill a track's metadata, and the live "start track" pacing.</summary>
internal static class MusicEndpoints
{
    public static void MapMusicEndpoints(this IEndpointRouteBuilder app)
    {
        // Host design-time helper — look up a track's title/artist/year for auto-fill. Activity-scoped so
        // it authorizes the same way as the rest of music management (site host OR a promoted event admin).
        // Uses the activity's saved Spotify connection (exact metadata) when one is selected, otherwise the
        // free oEmbed + MusicBrainz path; a failed Spotify call also falls back.
        app.MapPost("/api/activities/{id:int}/music/lookup", async (
            int id, MusicLookupRequest req, MusicLookupService lookup, SpotifyService spotify, AppDbContext db, CancellationToken ct) =>
        {
            var connId = await db.Activities.Where(a => a.Id == id).Select(a => a.SpotifyConnectionId).FirstOrDefaultAsync(ct);
            if (connId is { } cid && MusicLookupService.TryGetTrackId(req.SpotifyUrl) is { } trackId)
            {
                var viaSpotify = await spotify.GetTrackAsync(cid, trackId, ct);
                if (viaSpotify is { Found: true })
                {
                    return Results.Ok(viaSpotify);
                }
            }

            return Results.Ok(await lookup.LookupAsync(req.SpotifyUrl, ct));
        }).AddEndpointFilter<ActivityManagerFilter>();

        // Bulk-import a random selection of tracks from a Spotify playlist into a music quiz.
        app.MapPost("/api/activities/{id:int}/music/import", async (
            int id, MusicImportRequest req, SpotifyService spotify, AppDbContext db, CancellationToken ct) =>
        {
            var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (activity is null)
            {
                return Results.NotFound();
            }

            if (activity.Type != ActivityType.MusicQuiz)
            {
                throw new RuleViolationException("Only a music quiz can import tracks.");
            }

            var playlistId = SpotifyService.TryGetPlaylistId(req.PlaylistUrl)
                ?? throw new RuleViolationException("That doesn't look like a Spotify playlist link.");

            // Use the quiz's selected Spotify connection, or any saved one.
            var connId = activity.SpotifyConnectionId
                ?? await db.SpotifyConnections.Select(c => (int?)c.Id).FirstOrDefaultAsync(ct)
                ?? throw new RuleViolationException("Connect a Spotify account first (in host settings).");

            var count = Math.Clamp(req.Count, 1, 50);
            var pool = await spotify.GetPlaylistTracksAsync(connId, playlistId, max: Math.Clamp(count * 6, 60, 300), ct)
                ?? throw new RuleViolationException("That Spotify connection no longer exists.");
            if (pool.Count == 0)
            {
                throw new RuleViolationException("No playable tracks found in that playlist.");
            }

            var picked = pool.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
            var order = await db.Questions.Where(q => q.ActivityId == id).MaxAsync(q => (int?)q.Order, ct) ?? 0;
            foreach (var tr in picked)
            {
                order++;
                db.Questions.Add(new Question
                {
                    ActivityId = id,
                    Order = order,
                    Text = $"Track {order}",
                    Kind = QuestionKind.FreeText,
                    Points = 1,
                    SpotifyUrl = tr.Url,
                    AcceptedFreeTextAnswer = tr.Title,
                    AcceptedArtist = tr.Artist,
                    ReleaseYear = tr.Year,
                });
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new MusicImportResultDto { Imported = picked.Count });
        }).AddEndpointFilter<ActivityManagerFilter>();

        // Host starts a track for live, fastest-to-answer play: stamp the start time (drives the speed
        // bonus) and tell everyone playing so their countdown begins.
        app.MapPost("/api/activities/{id:int}/music/start/{questionId:int}", async (
            int id, int questionId, AppDbContext db, ScoreboardNotifier notifier, TimeProvider clock, CancellationToken ct) =>
        {
            var q = await db.Questions.FirstOrDefaultAsync(x => x.Id == questionId && x.ActivityId == id, ct);
            if (q is null)
            {
                return Results.NotFound();
            }

            var now = clock.GetUtcNow();
            q.PlayStartedUtc = now;
            await db.SaveChangesAsync(ct);
            await notifier.PushMusicTrackStartedAsync(id, questionId, now, GameService.SpeedWindowSeconds);
            return Results.Ok(new MusicTrackStartedDto
            {
                ActivityId = id, QuestionId = questionId, StartedUtc = now, WindowSeconds = GameService.SpeedWindowSeconds,
            });
        }).AddEndpointFilter<ActivityManagerFilter>();

        // Time-based finish: the host panel pings this when a track's window ends, so a music quiz wraps
        // up once every track has played and the clock's run out — even if some players never answered.
        app.MapPost("/api/activities/{id:int}/music/maybe-finish", async (
            int id, AppDbContext db, GameService game, ScoreboardNotifier notifier, PushService push, CancellationToken ct) =>
        {
            var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (activity is null)
            {
                return Results.NotFound();
            }

            if (await game.TryAutoFinishQuestionsAsync(activity, ct))
            {
                await notifier.PushStatusAsync(id, ActivityStatus.Finished);
                _ = push.NotifyActivityFinishedAsync(id);
            }

            return Results.Ok();
        }).AddEndpointFilter<ActivityManagerFilter>();
    }
}
