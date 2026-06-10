using Rundan.Server.Data;
using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared;

namespace Rundan.Server.Endpoints;

/// <summary>Dry-run endpoints: simulate / clear results for an activity or a whole event.</summary>
internal static class SimulationEndpoints
{
    public static void MapSimulationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/activities/{id:int}/simulate", async (
            int id, SimulationService sim, ScoreboardNotifier notifier, AppDbContext db, CancellationToken ct) =>
        {
            await sim.SimulateAsync(id, ct);
            await notifier.PushStatusAsync(id, ActivityStatus.Finished);
            await notifier.PushScoreboardAsync(id, ct);
            return Results.Ok(await ActivityEndpoints.LoadDtoAsync(db, id, ct));
        }).AddEndpointFilter<ActivityManagerFilter>();

        app.MapPost("/api/activities/{id:int}/reset-results", async (
            int id, SimulationService sim, ScoreboardNotifier notifier, AppDbContext db, CancellationToken ct) =>
        {
            await sim.ResetAsync(id, ct);
            await notifier.PushStatusAsync(id, ActivityStatus.Draft);
            await notifier.PushScoreboardAsync(id, ct);
            return Results.Ok(await ActivityEndpoints.LoadDtoAsync(db, id, ct));
        }).AddEndpointFilter<ActivityManagerFilter>();

        app.MapPost("/api/events/{id:int}/simulate", async (
            int id, SimulationService sim, ScoreboardNotifier notifier, CancellationToken ct) =>
        {
            foreach (var activityId in await sim.EventActivityIdsAsync(id, ct))
            {
                await sim.SimulateAsync(activityId, ct);
                await notifier.PushStatusAsync(activityId, ActivityStatus.Finished);
                await notifier.PushScoreboardAsync(activityId, ct);
            }

            return Results.Ok(new { ok = true });
        }).AddEndpointFilter<EventManagerFilter>();

        app.MapPost("/api/events/{id:int}/reset-results", async (
            int id, bool clearChat, SimulationService sim, ScoreboardNotifier notifier, CancellationToken ct) =>
            // clearChat comes from the query string (?clearChat=true); the client always sends it.
        {
            foreach (var activityId in await sim.EventActivityIdsAsync(id, ct))
            {
                await sim.ResetAsync(activityId, ct);
                await notifier.PushStatusAsync(activityId, ActivityStatus.Draft);
                await notifier.PushScoreboardAsync(activityId, ct);
            }

            if (clearChat)
            {
                await sim.ClearEventChatAsync(id, ct);
            }

            return Results.Ok(new { ok = true });
        }).AddEndpointFilter<EventManagerFilter>();
    }
}
