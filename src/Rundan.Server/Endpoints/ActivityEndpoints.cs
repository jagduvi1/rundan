using Microsoft.EntityFrameworkCore;
using Rundan.Server;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Endpoints;

internal static class ActivityEndpoints
{
    public static void MapActivityEndpoints(this IEndpointRouteBuilder app)
    {
        // --- Admin: create / list / manage --------------------------------------

        app.MapPost("/api/activities", async (
            CreateActivityRequest req, AppDbContext db, JoinCodeGenerator codes,
            RundanOptions options, HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            // Host, or an event admin of the target event (for standalone, host only).
            if (!await EventAuthorization.CanManageEventAsync(http, db, options, req.EventId, ct))
            {
                return EventManagerFilter.Forbidden();
            }

            if (string.IsNullOrWhiteSpace(req.Title))
            {
                throw new RuleViolationException("Give the activity a title.");
            }

            var order = 0;
            if (req.EventId is { } eventId)
            {
                if (!await db.Events.AnyAsync(e => e.Id == eventId, ct))
                {
                    throw new RuleViolationException("Event not found.", StatusCodes.Status404NotFound);
                }

                order = (await db.Activities.Where(a => a.EventId == eventId).MaxAsync(a => (int?)a.Order, ct) ?? 0) + 1;
            }

            var activity = new Activity
            {
                EventId = req.EventId,
                Order = order,
                Type = req.Type,
                Title = req.Title.Trim(),
                Description = TextHelpers.Clean(req.Description),
                ImageUrl = TextHelpers.Clean(req.ImageUrl),
                ScoringMode = req.ScoringMode,
                Measurement = req.Measurement,
                TargetValue = req.TargetValue,
                Status = ActivityStatus.Draft,
                JoinCode = await codes.NextAsync(db, ct),
                CreatedUtc = clock.GetUtcNow(),
            };
            db.Activities.Add(activity);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/activities/{activity.Id}", activity.ToDto(0, 0));
        });

        app.MapGet("/api/activities", async (AppDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Activities
                .AsNoTracking()
                .OrderByDescending(a => a.Id) // SQLite can't ORDER BY DateTimeOffset; Id ≈ creation order
                .Select(a => new { Activity = a, Pc = a.Participants.Count, Qc = a.Questions.Count })
                .ToListAsync(ct);

            return Results.Ok(rows.Select(r => r.Activity.ToDto(r.Pc, r.Qc)));
        }).AddEndpointFilter<AdminEndpointFilter>();

        // The library: public activity definitions, reusable in any event.
        app.MapGet("/api/activities/library", async (AppDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Activities.AsNoTracking()
                .Where(a => a.IsPublic)
                .OrderBy(a => a.Title)
                .Select(a => new { Activity = a, Qc = a.Questions.Count })
                .ToListAsync(ct);
            return Results.Ok(rows.Select(r => r.Activity.ToDto(0, r.Qc)));
        }).AddEndpointFilter<AdminEndpointFilter>();

        // Deep-copy a library activity into an event as a fresh Draft instance.
        app.MapPost("/api/events/{id:int}/activities/from-library/{sourceId:int}", async (
            int id, int sourceId, AppDbContext db, ActivityLibraryService lib, CancellationToken ct) =>
        {
            if (!await db.Events.AnyAsync(e => e.Id == id, ct))
            {
                return Results.NotFound();
            }

            var newId = await lib.CopyToEventAsync(sourceId, id, ct);
            return Results.Ok(await LoadDtoAsync(db, newId, ct));
        }).AddEndpointFilter<EventManagerFilter>();

        app.MapPut("/api/activities/{id:int}/status", async (
            int id, UpdateActivityStatusRequest req, AppDbContext db,
            ScoreboardNotifier notifier, TeamService teams, SlapService slaps, TimeProvider clock, CancellationToken ct) =>
        {
            var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == id, ct)
                ?? throw new RuleViolationException("Activity not found.", StatusCodes.Status404NotFound);

            if (!Enum.IsDefined(req.Status))
            {
                throw new RuleViolationException("Unknown activity status.");
            }

            if (!IsAllowedTransition(activity.Status, req.Status))
            {
                throw new RuleViolationException(
                    $"Cannot change status from {activity.Status} to {req.Status}.",
                    StatusCodes.Status409Conflict);
            }

            // Leaving Draft locks the questions, so every station must be filled in first
            // (blank stations are created by setting a station count).
            if (activity.Status == ActivityStatus.Draft && req.Status is ActivityStatus.Open or ActivityStatus.Live
                && activity.Type is ActivityType.Quiz or ActivityType.Tipspromenad)
            {
                var blanks = (await db.Questions.Include(q => q.Options)
                        .Where(q => q.ActivityId == id).ToListAsync(ct))
                    .Count(q => !QuestionEndpoints.IsPlayable(q));
                if (blanks > 0)
                {
                    throw new RuleViolationException(
                        $"{blanks} station{(blanks == 1 ? "" : "s")} still need a question — fill them in before starting.",
                        StatusCodes.Status409Conflict);
                }
            }

            // Slap twist: a pending slap must be resolved before the next activity starts.
            if (req.Status == ActivityStatus.Live && activity.EventId is { } slapEventId)
            {
                var pending = await slaps.PendingAsync(slapEventId, ct);
                if (pending is not null && pending.ActivityId != id)
                {
                    throw new RuleViolationException(
                        $"{pending.WinnerName} still owes a slap from “{pending.ActivityTitle}” — resolve it first.",
                        StatusCodes.Status409Conflict);
                }
            }

            if (req.Status == ActivityStatus.Live && activity.StartedUtc is null)
            {
                activity.StartedUtc = clock.GetUtcNow();
            }

            // Entering Finished stamps the finish time; leaving it (a host re-open) clears it.
            activity.FinishedUtc = req.Status == ActivityStatus.Finished ? clock.GetUtcNow() : null;
            activity.Status = req.Status;
            await db.SaveChangesAsync(ct);

            // Opening/starting an event activity generates its teams (the partner mixer).
            if (activity.EventId is not null && activity.Status is ActivityStatus.Open or ActivityStatus.Live)
            {
                await teams.EnsureTeamsAsync(activity, ct);
            }

            await notifier.PushStatusAsync(id, activity.Status);
            await notifier.PushScoreboardAsync(id, ct);

            return Results.Ok(await LoadDtoAsync(db, id, ct));
        }).AddEndpointFilter<ActivityManagerFilter>();

        app.MapPut("/api/activities/{id:int}", async (
            int id, UpdateActivityRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (activity is null)
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(req.Title))
            {
                throw new RuleViolationException("Give the activity a title.");
            }

            activity.Title = req.Title.Trim();
            activity.Description = TextHelpers.Clean(req.Description);
            activity.ImageUrl = TextHelpers.Clean(req.ImageUrl);
            activity.ScoringMode = req.ScoringMode;
            activity.Measurement = req.Measurement;
            activity.TargetValue = req.Measurement == Measurement.TimeSeconds || req.ScoringMode == ScoringMode.ClosestToTarget
                ? req.TargetValue
                : null;
            activity.RandomizeQuestions = req.RandomizeQuestions;
            activity.HideQuestionsFromHost = req.HideQuestionsFromHost;
            activity.IsPublic = req.IsPublic;
            activity.ScoreEntryMode = req.ScoreEntryMode;
            activity.Latitude = req.Latitude;
            activity.Longitude = req.Longitude;
            activity.RadiusMeters = req.RadiusMeters;
            activity.MatchFormat = req.MatchFormat;
            activity.BestOfSets = req.BestOfSets is 1 or 3 or 5 ? req.BestOfSets : 3;
            activity.GamesToWinSet = Math.Clamp(req.GamesToWinSet, 1, 100);
            await db.SaveChangesAsync(ct);
            return Results.Ok(await LoadDtoAsync(db, id, ct));
        }).AddEndpointFilter<ActivityManagerFilter>();

        app.MapPut("/api/activities/{id:int}/courts", async (
            int id, SetCourtsRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var activity = await db.Activities.Include(a => a.Courts).FirstOrDefaultAsync(a => a.Id == id, ct);
            if (activity is null)
            {
                return Results.NotFound();
            }

            var label = string.IsNullOrWhiteSpace(req.Label) ? "Court" : req.Label.Trim();
            activity.CourtLabel = label;

            var names = req.Names ?? new List<string>();
            var count = Math.Clamp(names.Count, 0, 50);
            var existing = activity.Courts.OrderBy(c => c.Order).ToList();

            // Drop surplus courts (clearing any bracket references first).
            for (var i = count; i < existing.Count; i++)
            {
                var court = existing[i];
                var orphans = await db.BracketMatches.Where(m => m.CourtId == court.Id).ToListAsync(ct);
                foreach (var m in orphans)
                {
                    m.CourtId = null;
                }

                db.Courts.Remove(court);
            }

            // Update / add the rest with the given (or default) names.
            for (var i = 0; i < count; i++)
            {
                var name = string.IsNullOrWhiteSpace(names[i]) ? $"{label} {i + 1}" : names[i].Trim();
                if (i < existing.Count)
                {
                    existing[i].Order = i + 1;
                    existing[i].Name = name;
                }
                else
                {
                    activity.Courts.Add(new Court { ActivityId = id, Order = i + 1, Name = name });
                }
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(await LoadDtoAsync(db, id, ct));
        }).AddEndpointFilter<ActivityManagerFilter>();

        app.MapDelete("/api/activities/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var activity = await db.Activities.FindAsync([id], ct);
            if (activity is null)
            {
                return Results.NotFound();
            }

            // Slaps reference the activity by a loose int (no FK), so the event cascade won't
            // clean them — remove them here or a deleted activity's penalty haunts the standings.
            await db.Slaps.Where(s => s.ActivityId == id).ExecuteDeleteAsync(ct);

            db.Activities.Remove(activity);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).AddEndpointFilter<ActivityManagerFilter>();

        // --- Players: look up the activity --------------------------------------

        app.MapGet("/api/activities/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var dto = await LoadDtoAsync(db, id, ct);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });

        app.MapGet("/api/activities/by-code/{code}", async (string code, AppDbContext db, CancellationToken ct) =>
        {
            var normalized = code.Trim().ToUpperInvariant();
            var activity = await db.Activities.AsNoTracking()
                .FirstOrDefaultAsync(a => a.JoinCode == normalized, ct);
            if (activity is null)
            {
                return Results.NotFound();
            }

            var pc = await db.Participants.CountAsync(p => p.ActivityId == activity.Id, ct);
            var qc = await db.Questions.CountAsync(q => q.ActivityId == activity.Id, ct);
            return Results.Ok(activity.ToDto(pc, qc));
        });
    }

    private static bool IsAllowedTransition(ActivityStatus from, ActivityStatus to) => from == to || (from, to) switch
    {
        (ActivityStatus.Draft, ActivityStatus.Open) => true,
        (ActivityStatus.Open, ActivityStatus.Draft) => true,
        (ActivityStatus.Open, ActivityStatus.Live) => true,
        (ActivityStatus.Live, ActivityStatus.Open) => true,
        (ActivityStatus.Live, ActivityStatus.Finished) => true,
        // A host may deliberately re-open a finished activity (admin-gated).
        (ActivityStatus.Finished, ActivityStatus.Live) => true,
        (ActivityStatus.Finished, ActivityStatus.Open) => true,
        // "Pause": deactivate an activity back to Draft (hidden from players) but keep it in the event.
        (ActivityStatus.Live, ActivityStatus.Draft) => true,
        (ActivityStatus.Finished, ActivityStatus.Draft) => true,
        _ => false,
    };

    internal static async Task<ActivityDto?> LoadDtoAsync(AppDbContext db, int id, CancellationToken ct)
    {
        var activity = await db.Activities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (activity is null)
        {
            return null;
        }

        var pc = await db.Participants.CountAsync(p => p.ActivityId == id, ct);
        var qc = await db.Questions.CountAsync(q => q.ActivityId == id, ct);
        var courts = await db.Courts.AsNoTracking()
            .Where(c => c.ActivityId == id).OrderBy(c => c.Order)
            .Select(c => new CourtDto { Id = c.Id, Order = c.Order, Name = c.Name })
            .ToListAsync(ct);

        var dto = activity.ToDto(pc, qc, courts);
        if (activity.EventId is { } evId)
        {
            var teamSize = await db.Events.AsNoTracking()
                .Where(e => e.Id == evId).Select(e => (int?)e.TeamSize).FirstOrDefaultAsync(ct) ?? 1;
            // Participants are teams when the event pairs players up (TeamSize > 1).
            dto.IsTeamBased = teamSize > 1;

            // For a roster event, the player/team counts come from the roster (stable even before
            // teams are generated when the activity opens). Free-name events keep the joined count.
            var memberCount = await db.EventMembers.CountAsync(m => m.EventId == evId, ct);
            if (memberCount > 0)
            {
                dto.PlayerCount = memberCount;
                dto.TeamCount = teamSize > 1 ? (int)Math.Ceiling(memberCount / (double)teamSize) : 0;
            }
        }

        return dto;
    }
}
