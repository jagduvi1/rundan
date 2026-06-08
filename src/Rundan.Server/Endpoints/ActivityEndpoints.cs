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
                Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
                ImageUrl = string.IsNullOrWhiteSpace(req.ImageUrl) ? null : req.ImageUrl.Trim(),
                ScoringMode = req.ScoringMode,
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

        app.MapPut("/api/activities/{id:int}/status", async (
            int id, UpdateActivityStatusRequest req, AppDbContext db,
            ScoreboardNotifier notifier, TeamService teams, TimeProvider clock, CancellationToken ct) =>
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
            activity.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
            activity.ImageUrl = string.IsNullOrWhiteSpace(req.ImageUrl) ? null : req.ImageUrl.Trim();
            activity.ScoreEntryMode = req.ScoreEntryMode;
            activity.Latitude = req.Latitude;
            activity.Longitude = req.Longitude;
            activity.RadiusMeters = req.RadiusMeters;
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
        return activity.ToDto(pc, qc);
    }
}
