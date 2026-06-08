using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Endpoints;

internal static class WordGameEndpoints
{
    public static void MapWordGameEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/activities/{id:int}/wordgame", async (
            int id, AppDbContext db, WordGameService svc, HttpContext http, CancellationToken ct) =>
        {
            var participant = await http.ResolveForActivityAsync(db, id, ct);
            return Results.Ok(await svc.GetAsync(participant, ct));
        });

        app.MapPost("/api/activities/{id:int}/wordgame", async (
            int id, SubmitWordRequest req, AppDbContext db, WordGameService svc,
            ScoreboardNotifier notifier, HttpContext http, CancellationToken ct) =>
        {
            var participant = await http.ResolveForActivityAsync(db, id, ct);

            var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == id, ct)
                ?? throw new RuleViolationException("Activity not found.", StatusCodes.Status404NotFound);
            if (activity.Status != ActivityStatus.Live)
            {
                throw new RuleViolationException("This game isn't running right now.", StatusCodes.Status409Conflict);
            }

            var dto = await svc.SubmitAsync(participant, req, ct);
            await notifier.PushScoreboardAsync(id, ct);
            return Results.Ok(dto);
        });
    }
}
