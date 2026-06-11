using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Endpoints;

/// <summary>Music-quiz helpers: auto-fill a track's metadata, and the live "start track" pacing.</summary>
internal static class MusicEndpoints
{
    public static void MapMusicEndpoints(this IEndpointRouteBuilder app)
    {
        // Host design-time helper — look up a track's title/artist/year for auto-fill.
        app.MapPost("/api/music/lookup", async (MusicLookupRequest req, MusicLookupService svc, CancellationToken ct) =>
            Results.Ok(await svc.LookupAsync(req.SpotifyUrl, ct)))
           .AddEndpointFilter<AdminEndpointFilter>();

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
    }
}
