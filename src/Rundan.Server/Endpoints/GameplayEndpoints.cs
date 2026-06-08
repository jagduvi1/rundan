using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Endpoints;

internal static class GameplayEndpoints
{
    public static void MapGameplayEndpoints(this IEndpointRouteBuilder app)
    {
        // --- Answers (Quiz / Tipspromenad) --------------------------------------

        app.MapPost("/api/activities/{id:int}/answers", async (
            int id, SubmitAnswerRequest req, AppDbContext db, GameService game,
            ScoreboardNotifier notifier, HttpContext http, CancellationToken ct) =>
        {
            var participant = await http.ResolveForActivityAsync(db, id, ct);

            var result = await game.SubmitAnswerAsync(participant, req, ct);
            await notifier.PushScoreboardAsync(id, ct);
            return Results.Ok(result);
        });

        app.MapGet("/api/activities/{id:int}/my-answers", async (
            int id, AppDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var participant = await http.ResolveForActivityAsync(db, id, ct);

            var answers = await db.Answers.AsNoTracking()
                .Where(a => a.ParticipantId == participant.Id)
                .Select(a => new MyAnswerDto
                {
                    QuestionId = a.QuestionId,
                    SelectedOptionId = a.SelectedOptionId,
                    FreeText = a.FreeText,
                    IsCorrect = a.IsCorrect,
                    AwardedPoints = a.AwardedPoints,
                })
                .ToListAsync(ct);

            return Results.Ok(answers);
        });

        // --- Scores (Boule / generic score game) --------------------------------

        app.MapPost("/api/activities/{id:int}/scores", async (
            int id, RecordScoreRequest req, AppDbContext db, GameService game,
            ScoreboardNotifier notifier, HttpContext http, CancellationToken ct) =>
        {
            await http.ResolveForActivityAsync(db, id, ct); // must be a participant of this activity

            var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == id, ct)
                ?? throw new RuleViolationException("Activity not found.", StatusCodes.Status404NotFound);

            var dto = await game.RecordScoreAsync(activity, req, ct);
            await notifier.PushScoreboardAsync(id, ct);
            return Results.Ok(dto);
        });

        app.MapGet("/api/activities/{id:int}/scores", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var entries = await db.ScoreEntries.AsNoTracking()
                .Include(s => s.Participant)
                .Include(s => s.User)
                .Where(s => s.ActivityId == id)
                .OrderBy(s => s.Round)
                .ThenBy(s => s.Id) // SQLite can't ORDER BY DateTimeOffset; Id ≈ recorded order
                .ToListAsync(ct);
            return Results.Ok(entries.Select(s => s.ToDto()));
        });

        app.MapDelete("/api/activities/{id:int}/scores/{scoreId:int}", async (
            int id, int scoreId, AppDbContext db, ScoreboardNotifier notifier, CancellationToken ct) =>
        {
            var entry = await db.ScoreEntries.FirstOrDefaultAsync(s => s.Id == scoreId && s.ActivityId == id, ct);
            if (entry is null)
            {
                return Results.NotFound();
            }

            db.ScoreEntries.Remove(entry);
            await db.SaveChangesAsync(ct);
            await notifier.PushScoreboardAsync(id, ct);
            return Results.NoContent();
        }).AddEndpointFilter<ActivityManagerFilter>();

        // --- Scoreboard ---------------------------------------------------------

        app.MapGet("/api/activities/{id:int}/scoreboard", async (
            int id, ScoreboardService scoreboard, CancellationToken ct) =>
        {
            var board = await scoreboard.BuildAsync(id, ct);
            return board is null ? Results.NotFound() : Results.Ok(board);
        });
    }
}
