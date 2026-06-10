using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Endpoints;

internal static class BracketEndpoints
{
    public static void MapBracketEndpoints(this IEndpointRouteBuilder app)
    {
        // Players can see the tree (access-gated by middleware).
        app.MapGet("/api/activities/{id:int}/bracket", async (int id, BracketService svc, CancellationToken ct) =>
        {
            var dto = await svc.BuildAsync(id, ct);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });

        // Host / event admin: draw, record a result, reset.
        app.MapPost("/api/activities/{id:int}/bracket/draw", async (
            int id, BracketService svc, ScoreboardNotifier notifier, CancellationToken ct) =>
        {
            await svc.GenerateAsync(id, ct);
            await notifier.PushScoreboardAsync(id, ct);
            return Results.Ok(await svc.BuildAsync(id, ct));
        }).AddEndpointFilter<ActivityManagerFilter>();

        app.MapPost("/api/activities/{id:int}/bracket/result", async (
            int id, RecordBracketResultRequest req, BracketService svc, ScoreboardNotifier notifier, PushService push, CancellationToken ct) =>
        {
            var sets = req.Sets.Select(s => (s.A, s.B)).ToList();
            await svc.RecordResultAsync(id, req.MatchId, sets, explicitWinnerId: null, ct);
            await notifier.PushScoreboardAsync(id, ct);

            // Auto-finalize once the bracket has a champion (so the slap ceremony fires on its own).
            if (await svc.TryAutoFinishAsync(id, ct))
            {
                await notifier.PushStatusAsync(id, ActivityStatus.Finished);
                _ = push.NotifyActivityFinishedAsync(id);
            }

            return Results.Ok(await svc.BuildAsync(id, ct));
        }).AddEndpointFilter<ActivityManagerFilter>();

        app.MapPost("/api/activities/{id:int}/bracket/reset", async (
            int id, BracketService svc, ScoreboardNotifier notifier, CancellationToken ct) =>
        {
            await svc.ResetAsync(id, ct);
            await notifier.PushScoreboardAsync(id, ct);
            return Results.Ok(await svc.BuildAsync(id, ct));
        }).AddEndpointFilter<ActivityManagerFilter>();
    }
}
