using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared;
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

            // Auto-finalize once every participant has answered every question.
            var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (activity is not null && await game.TryAutoFinishQuestionsAsync(activity, ct))
            {
                await notifier.PushStatusAsync(id, ActivityStatus.Finished);
            }

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
                    ArtistText = a.ArtistText,
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

            // Auto-finalize once every expected score is in (so the slap ceremony fires on its own).
            if (await game.TryAutoFinishScoreGameAsync(activity, ct))
            {
                await notifier.PushStatusAsync(id, ActivityStatus.Finished);
            }

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

        // --- Activity photo wall (players take/upload photos) -------------------

        app.MapGet("/api/activities/{id:int}/photos", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var photos = await db.ActivityPhotos.AsNoTracking()
                .Where(p => p.ActivityId == id)
                .OrderByDescending(p => p.Id)
                .Select(p => new ActivityPhotoDto { Id = p.Id, Author = p.Author, Url = p.Url, CreatedUtc = p.CreatedUtc })
                .ToListAsync(ct);
            return Results.Ok(photos);
        });

        app.MapPost("/api/activities/{id:int}/photos", async (
            int id, IFormFile file, AppDbContext db, StoragePaths paths,
            TimeProvider clock, HttpContext http, CancellationToken ct) =>
        {
            var participant = await http.ResolveForActivityAsync(db, id, ct); // must be in the activity

            if (file is null || file.Length == 0)
            {
                throw new RuleViolationException("No photo was uploaded.");
            }
            if (file.Length > 8 * 1024 * 1024)
            {
                throw new RuleViolationException("Photo is too large (max 8 MB).");
            }

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            string[] allowed = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic" };
            if (!allowed.Contains(ext))
            {
                throw new RuleViolationException("Use a JPG, PNG, GIF, WEBP or HEIC photo.");
            }

            var name = $"{Guid.NewGuid():N}{ext}";
            await using (var stream = File.Create(Path.Combine(paths.UploadsDir, name)))
            {
                await file.CopyToAsync(stream, ct);
            }

            var photo = new Rundan.Server.Data.Entities.ActivityPhoto
            {
                ActivityId = id,
                Author = participant.DisplayName,
                Url = $"/uploads/{name}",
                CreatedUtc = clock.GetUtcNow(),
            };
            db.ActivityPhotos.Add(photo);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new ActivityPhotoDto { Id = photo.Id, Author = photo.Author, Url = photo.Url, CreatedUtc = photo.CreatedUtc });
        }).DisableAntiforgery();

        app.MapDelete("/api/activities/{id:int}/photos/{photoId:int}", async (
            int id, int photoId, AppDbContext db, StoragePaths paths, RundanOptions options,
            HttpContext http, CancellationToken ct) =>
        {
            var photo = await db.ActivityPhotos.FirstOrDefaultAsync(p => p.Id == photoId && p.ActivityId == id, ct);
            if (photo is null)
            {
                return Results.NotFound();
            }

            // Allowed for whoever can manage the event (host / event admin) …
            var activity = await db.Activities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
            var canManage = activity?.EventId is int evId
                && await EventAuthorization.CanManageEventAsync(http, db, options, evId, ct);

            // … or the player who uploaded it (their participant token's name matches; names are
            // unique within an activity).
            var isUploader = false;
            if (!canManage)
            {
                var raw = http.Request.Headers[ParticipantContext.HeaderName].ToString();
                if (Guid.TryParse(raw, out var token) && token != Guid.Empty)
                {
                    var p = await db.Participants.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.ActivityId == id && x.Token == token, ct);
                    isUploader = p is not null && p.DisplayName == photo.Author;
                }
            }

            if (!canManage && !isUploader)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            db.ActivityPhotos.Remove(photo);
            await db.SaveChangesAsync(ct);

            try // best-effort: drop the file too (an orphan is harmless otherwise)
            {
                var full = Path.Combine(paths.UploadsDir, Path.GetFileName(photo.Url));
                if (File.Exists(full))
                {
                    File.Delete(full);
                }
            }
            catch { /* ignore */ }

            return Results.NoContent();
        });

        // --- Scoreboard ---------------------------------------------------------

        app.MapGet("/api/activities/{id:int}/scoreboard", async (
            int id, ScoreboardService scoreboard, CancellationToken ct) =>
        {
            var board = await scoreboard.BuildAsync(id, ct);
            return board is null ? Results.NotFound() : Results.Ok(board);
        });
    }
}
