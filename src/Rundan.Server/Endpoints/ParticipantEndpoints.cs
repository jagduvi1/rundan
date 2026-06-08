using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Endpoints;

internal static class ParticipantEndpoints
{
    public static void MapParticipantEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/activities/by-code/{code}/join", async (
            string code, JoinActivityRequest req, AppDbContext db, ScoreboardNotifier notifier,
            RundanOptions options, TimeProvider clock, HttpContext http, CancellationToken ct) =>
        {
            var normalized = code.Trim().ToUpperInvariant();
            var activity = await db.Activities.FirstOrDefaultAsync(a => a.JoinCode == normalized, ct)
                ?? throw new RuleViolationException("No activity with that code.", StatusCodes.Status404NotFound);

            if (activity.Status == ActivityStatus.Draft)
            {
                throw new RuleViolationException("This activity hasn't opened yet.", StatusCodes.Status409Conflict);
            }

            if (activity.Status == ActivityStatus.Finished)
            {
                throw new RuleViolationException("This activity has already finished.", StatusCodes.Status409Conflict);
            }

            var name = (req.DisplayName ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                throw new RuleViolationException("Enter a name to join with.");
            }

            if (name.Length > 60)
            {
                name = name[..60];
            }

            // Reconnect with a previously issued token (same device returning).
            if (req.ExistingToken is { } token && token != Guid.Empty)
            {
                var mine = await db.Participants
                    .FirstOrDefaultAsync(p => p.Token == token && p.ActivityId == activity.Id, ct);
                if (mine is not null)
                {
                    if (!string.Equals(mine.DisplayName, name, StringComparison.Ordinal))
                    {
                        var clash = await db.Participants.AnyAsync(
                            p => p.ActivityId == activity.Id && p.Id != mine.Id && p.DisplayName == name, ct);
                        if (!clash)
                        {
                            mine.DisplayName = name;
                            await db.SaveChangesAsync(ct);
                        }
                    }

                    return Results.Ok(await BuildJoinResultAsync(db, activity, mine, ct));
                }
            }

            var taken = await db.Participants.AnyAsync(p => p.ActivityId == activity.Id && p.DisplayName == name, ct);
            if (taken)
            {
                throw new RuleViolationException("That name is already taken here — pick another.",
                    StatusCodes.Status409Conflict);
            }

            // If the joining device also holds the admin code, mark them as an admin participant.
            var isAdmin = options.RequiresAdminCode && SecurityHelpers.FixedEquals(
                http.Request.Headers[AdminEndpointFilter.HeaderName].ToString(), options.AdminCode);

            var participant = new Participant
            {
                ActivityId = activity.Id,
                DisplayName = name,
                Token = Guid.NewGuid(),
                IsAdmin = isAdmin,
                JoinedUtc = clock.GetUtcNow(),
            };
            db.Participants.Add(participant);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                throw new RuleViolationException("That name was just taken — pick another.",
                    StatusCodes.Status409Conflict);
            }

            await notifier.PushParticipantJoinedAsync(activity.Id, participant.ToDto());
            await notifier.PushScoreboardAsync(activity.Id, ct);

            return Results.Ok(await BuildJoinResultAsync(db, activity, participant, ct));
        });

        app.MapGet("/api/activities/{id:int}/participants", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var list = await db.Participants.AsNoTracking()
                .Where(p => p.ActivityId == id)
                .OrderBy(p => p.Id) // SQLite can't ORDER BY DateTimeOffset; Id ≈ join order
                .ToListAsync(ct);
            return Results.Ok(list.Select(p => p.ToDto()));
        });

        app.MapDelete("/api/activities/{id:int}/participants/{participantId:int}", async (
            int id, int participantId, AppDbContext db, ScoreboardNotifier notifier, CancellationToken ct) =>
        {
            var participant = await db.Participants
                .FirstOrDefaultAsync(p => p.Id == participantId && p.ActivityId == id, ct);
            if (participant is null)
            {
                return Results.NotFound();
            }

            db.Participants.Remove(participant);
            await db.SaveChangesAsync(ct);
            await notifier.PushScoreboardAsync(id, ct);
            return Results.NoContent();
        }).AddEndpointFilter<ActivityManagerFilter>();
    }

    private static async Task<JoinResultDto> BuildJoinResultAsync(
        AppDbContext db, Activity activity, Participant participant, CancellationToken ct)
    {
        var pc = await db.Participants.CountAsync(p => p.ActivityId == activity.Id, ct);
        var qc = await db.Questions.CountAsync(q => q.ActivityId == activity.Id, ct);
        return new JoinResultDto
        {
            Token = participant.Token,
            Activity = activity.ToDto(pc, qc),
            Participant = participant.ToDto(),
        };
    }
}
