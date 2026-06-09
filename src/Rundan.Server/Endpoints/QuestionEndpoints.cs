using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Endpoints;

internal static class QuestionEndpoints
{
    public static void MapQuestionEndpoints(this IEndpointRouteBuilder app)
    {
        // --- Players: questions to play, and the answer key once finished --------

        app.MapGet("/api/activities/{id:int}/questions", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var activity = await db.Activities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
                ?? throw new RuleViolationException("Activity not found.", StatusCodes.Status404NotFound);

            if (activity.Status is ActivityStatus.Draft or ActivityStatus.Open)
            {
                throw new RuleViolationException("Questions aren't available until the activity starts.",
                    StatusCodes.Status409Conflict);
            }

            var questions = await LoadOrderedAsync(db, id, ct);
            return Results.Ok(questions.Select(q => q.ToPlayerDto()));
        });

        app.MapGet("/api/activities/{id:int}/results", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var activity = await db.Activities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
                ?? throw new RuleViolationException("Activity not found.", StatusCodes.Status404NotFound);

            if (activity.Status != ActivityStatus.Finished)
            {
                throw new RuleViolationException("Results are revealed once the activity is finished.",
                    StatusCodes.Status409Conflict);
            }

            var questions = await LoadOrderedAsync(db, id, ct);
            return Results.Ok(questions.Select(q => q.ToResultDto()));
        });

        // --- Admin: build the question set (only while Draft) --------------------

        app.MapGet("/api/activities/{id:int}/questions/admin", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var questions = await LoadOrderedAsync(db, id, ct);
            return Results.Ok(questions.Select(q => q.ToAdminDto()));
        }).AddEndpointFilter<ActivityManagerFilter>();

        app.MapPost("/api/activities/{id:int}/questions", async (
            int id, QuestionUpsertRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == id, ct)
                ?? throw new RuleViolationException("Activity not found.", StatusCodes.Status404NotFound);
            EnsureQuestionEditable(activity);
            Validate(req);

            var question = BuildQuestion(id, req);
            if (question.Order <= 0)
            {
                var maxOrder = await db.Questions.Where(q => q.ActivityId == id)
                    .MaxAsync(q => (int?)q.Order, ct) ?? 0;
                question.Order = maxOrder + 1;
            }

            db.Questions.Add(question);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/activities/{id}/questions/{question.Id}", question.ToAdminDto());
        }).AddEndpointFilter<ActivityManagerFilter>();

        app.MapPut("/api/activities/{id:int}/questions/{questionId:int}", async (
            int id, int questionId, QuestionUpsertRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == id, ct)
                ?? throw new RuleViolationException("Activity not found.", StatusCodes.Status404NotFound);
            EnsureQuestionEditable(activity);
            Validate(req);

            var question = await db.Questions.Include(q => q.Options)
                .FirstOrDefaultAsync(q => q.Id == questionId && q.ActivityId == id, ct);
            if (question is null)
            {
                return Results.NotFound();
            }

            question.Order = req.Order > 0 ? req.Order : question.Order;
            question.Text = req.Text.Trim();
            question.Kind = req.Kind;
            question.Points = Math.Max(0, req.Points);
            question.ImageUrl = TextHelpers.Clean(req.ImageUrl);
            question.Latitude = req.Latitude;
            question.Longitude = req.Longitude;
            question.RadiusMeters = req.RadiusMeters;
            question.AcceptedFreeTextAnswer =
                req.Kind == QuestionKind.FreeText ? TextHelpers.Clean(req.AcceptedFreeTextAnswer) : null;

            db.AnswerOptions.RemoveRange(question.Options);
            question.Options.Clear();
            AddOptions(question, req);

            await db.SaveChangesAsync(ct);
            return Results.Ok(question.ToAdminDto());
        }).AddEndpointFilter<ActivityManagerFilter>();

        // Set/clear a single question's GPS station (tipspromenad). Geo only — allowed any time,
        // including after a question was pulled from the library (which carries no location).
        app.MapPut("/api/activities/{id:int}/questions/{questionId:int}/location", async (
            int id, int questionId, SetQuestionLocationRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var question = await db.Questions.Include(q => q.Options)
                .FirstOrDefaultAsync(q => q.Id == questionId && q.ActivityId == id, ct);
            if (question is null)
            {
                return Results.NotFound();
            }

            question.Latitude = req.Latitude;
            question.Longitude = req.Longitude;
            question.RadiusMeters = req.RadiusMeters is > 0 ? req.RadiusMeters : null;
            await db.SaveChangesAsync(ct);
            return Results.Ok(question.ToAdminDto());
        }).AddEndpointFilter<ActivityManagerFilter>();

        // Host fixes a wrong answer key after the fact and re-scores everyone (works post-finish).
        app.MapPut("/api/activities/{id:int}/questions/{questionId:int}/answer-key", async (
            int id, int questionId, UpdateAnswerKeyRequest req, AppDbContext db, GameService game,
            ScoreboardNotifier notifier, CancellationToken ct) =>
        {
            var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == id, ct)
                ?? throw new RuleViolationException("Activity not found.", StatusCodes.Status404NotFound);
            if (activity.Type is not (ActivityType.Quiz or ActivityType.Tipspromenad))
            {
                throw new RuleViolationException("This activity type does not use questions.");
            }

            var result = await game.UpdateAnswerKeyAsync(activity, questionId, req, ct);
            await notifier.PushScoreboardAsync(id, ct);
            return Results.Ok(result);
        }).AddEndpointFilter<ActivityManagerFilter>();

        app.MapDelete("/api/activities/{id:int}/questions/{questionId:int}", async (
            int id, int questionId, AppDbContext db, CancellationToken ct) =>
        {
            var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == id, ct)
                ?? throw new RuleViolationException("Activity not found.", StatusCodes.Status404NotFound);
            EnsureQuestionEditable(activity);

            var question = await db.Questions.FirstOrDefaultAsync(q => q.Id == questionId && q.ActivityId == id, ct);
            if (question is null)
            {
                return Results.NotFound();
            }

            db.Questions.Remove(question);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).AddEndpointFilter<ActivityManagerFilter>();

        // Set how many stations a tipspromenad (or quiz) has. Adds blank stations — placed on the map
        // and filled with a question later — or trims trailing blank ones. Authored questions (anything
        // with real content) are never auto-deleted. Returns the renumbered station list.
        app.MapPut("/api/activities/{id:int}/stations/count", async (
            int id, SetStationCountRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == id, ct)
                ?? throw new RuleViolationException("Activity not found.", StatusCodes.Status404NotFound);
            EnsureQuestionEditable(activity);

            var target = Math.Clamp(req.Count, 0, 100);
            var questions = await db.Questions.Include(q => q.Options)
                .Where(q => q.ActivityId == id).OrderBy(q => q.Order).ToListAsync(ct);

            if (target > questions.Count)
            {
                for (var i = questions.Count; i < target; i++)
                {
                    db.Questions.Add(new Question
                    {
                        ActivityId = id,
                        Order = i + 1,
                        Text = string.Empty, // blank station — content added later
                        Kind = QuestionKind.MultipleChoice,
                        Points = 1,
                    });
                }
            }
            else
            {
                // Trim from the end, but stop at the first authored question so nothing real is lost.
                for (var i = questions.Count - 1; i >= target; i--)
                {
                    if (IsPlayable(questions[i]))
                    {
                        break;
                    }

                    db.Questions.Remove(questions[i]);
                }
            }

            await db.SaveChangesAsync(ct);

            // Renumber to a clean, contiguous 1..N.
            var remaining = await db.Questions.Include(q => q.Options)
                .Where(q => q.ActivityId == id).OrderBy(q => q.Order).ToListAsync(ct);
            for (var i = 0; i < remaining.Count; i++)
            {
                remaining[i].Order = i + 1;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(remaining.Select(q => q.ToAdminDto()));
        }).AddEndpointFilter<ActivityManagerFilter>();
    }

    /// <summary>True once a question has enough content to be played: some text plus a valid answer key.</summary>
    internal static bool IsPlayable(Question q) =>
        !string.IsNullOrWhiteSpace(q.Text) &&
        (q.Kind == QuestionKind.FreeText
            ? !string.IsNullOrWhiteSpace(q.AcceptedFreeTextAnswer)
            : q.Options.Count >= 2
              && q.Options.Count(o => o.IsCorrect) == 1
              && q.Options.All(o => !string.IsNullOrWhiteSpace(o.Text)));

    private static Task<List<Question>> LoadOrderedAsync(AppDbContext db, int activityId, CancellationToken ct) =>
        db.Questions.AsNoTracking()
            .Include(q => q.Options)
            .Where(q => q.ActivityId == activityId)
            .OrderBy(q => q.Order)
            .ToListAsync(ct);

    private static void EnsureQuestionEditable(Activity activity)
    {
        if (activity.Type is not (ActivityType.Quiz or ActivityType.Tipspromenad))
        {
            throw new RuleViolationException("This activity type does not use questions.");
        }

        if (activity.Status != ActivityStatus.Draft)
        {
            throw new RuleViolationException(
                "Questions can only be edited while the activity is a draft (before it opens).",
                StatusCodes.Status409Conflict);
        }
    }

    private static void Validate(QuestionUpsertRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Text))
        {
            throw new RuleViolationException("A question needs some text.");
        }

        if (req.Kind == QuestionKind.FreeText)
        {
            if (string.IsNullOrWhiteSpace(req.AcceptedFreeTextAnswer))
            {
                throw new RuleViolationException("A free-text question needs an accepted answer.");
            }

            return;
        }

        var options = req.Options ?? new();
        if (options.Count < 2)
        {
            throw new RuleViolationException("Add at least two options.");
        }

        if (options.Count(o => o.IsCorrect) != 1)
        {
            throw new RuleViolationException("Mark exactly one option as correct.");
        }

        if (options.Any(o => string.IsNullOrWhiteSpace(o.Text)))
        {
            throw new RuleViolationException("Every option needs some text.");
        }
    }

    private static Question BuildQuestion(int activityId, QuestionUpsertRequest req)
    {
        var question = new Question
        {
            ActivityId = activityId,
            Order = req.Order,
            Text = req.Text.Trim(),
            Kind = req.Kind,
            Points = Math.Max(0, req.Points),
            ImageUrl = TextHelpers.Clean(req.ImageUrl),
            Latitude = req.Latitude,
            Longitude = req.Longitude,
            RadiusMeters = req.RadiusMeters,
            AcceptedFreeTextAnswer = req.Kind == QuestionKind.FreeText ? TextHelpers.Clean(req.AcceptedFreeTextAnswer) : null,
        };
        AddOptions(question, req);
        return question;
    }

    private static void AddOptions(Question question, QuestionUpsertRequest req)
    {
        if (req.Kind == QuestionKind.FreeText)
        {
            return;
        }

        var order = 0;
        foreach (var option in req.Options)
        {
            question.Options.Add(new AnswerOption
            {
                Order = order++,
                Text = option.Text.Trim(),
                IsCorrect = option.IsCorrect,
            });
        }
    }
}
