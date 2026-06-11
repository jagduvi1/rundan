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

            // MapPin is scored by distance — lowest total wins — and draws 5 cities by default.
            if (req.Type == ActivityType.MapPin)
            {
                activity.ScoringMode = ScoringMode.LowerWins;
                activity.MapCityCount = 5;
            }

            // Memory races each team's board — fastest time (or fewest flips) wins.
            if (req.Type == ActivityType.Memory)
            {
                activity.ScoringMode = ScoringMode.LowerWins;
                activity.Measurement = Measurement.TimeSeconds;
            }

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
            ScoreboardNotifier notifier, PushService push, TeamService teams, SlapService slaps, TimeProvider clock, CancellationToken ct) =>
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

            // A music quiz needs each track to have a Spotify link plus the song and artist answers.
            if (activity.Status == ActivityStatus.Draft && req.Status is ActivityStatus.Open or ActivityStatus.Live
                && activity.Type == ActivityType.MusicQuiz)
            {
                var tracks = await db.Questions.Where(q => q.ActivityId == id).ToListAsync(ct);
                var incomplete = tracks.Count(q => string.IsNullOrWhiteSpace(q.SpotifyUrl)
                    || string.IsNullOrWhiteSpace(q.AcceptedFreeTextAnswer) || string.IsNullOrWhiteSpace(q.AcceptedArtist));
                if (tracks.Count == 0 || incomplete > 0)
                {
                    throw new RuleViolationException(
                        tracks.Count == 0
                            ? "Add at least one track before starting."
                            : $"{incomplete} track{(incomplete == 1 ? "" : "s")} still need a Spotify link, song and artist.",
                        StatusCodes.Status409Conflict);
                }
            }

            // Slaps no longer gate the next activity: activities finish out of order (e.g. an
            // all-day tipspromenad), so each finished activity surfaces its own slap ceremony
            // independently instead of blocking the running order.

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

            // MapPin: draw the cities once when it first opens — the same set for every team.
            if (activity.Type == ActivityType.MapPin && activity.Status is ActivityStatus.Open or ActivityStatus.Live
                && !await db.MapCities.AnyAsync(c => c.ActivityId == id, ct))
            {
                var n = Math.Clamp(activity.MapCityCount ?? 5, 1, SwedishCities.All.Count);
                var rng = new Random();
                var drawn = SwedishCities.All.OrderBy(_ => rng.Next()).Take(n).ToList();
                for (var i = 0; i < drawn.Count; i++)
                {
                    db.MapCities.Add(new MapCity
                    {
                        ActivityId = id,
                        Order = i,
                        Name = drawn[i].Name,
                        Latitude = drawn[i].Lat,
                        Longitude = drawn[i].Lng,
                    });
                }

                await db.SaveChangesAsync(ct);
            }

            await notifier.PushStatusAsync(id, activity.Status);
            await notifier.PushScoreboardAsync(id, ct);

            // Push notifications: an activity going live, or finishing (winner / slap / new leader).
            if (activity.EventId is { } pevId)
            {
                if (req.Status == ActivityStatus.Live)
                {
                    push.Notify(pevId, "▶ Activity started", $"“{activity.Title}” is live — go play!", $"e/{pevId}", $"live-{id}");
                }
                else if (req.Status == ActivityStatus.Finished)
                {
                    _ = push.NotifyActivityFinishedAsync(id);
                }
            }

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

            // Changing the type is only allowed while Draft, and abandons the old type's authored
            // content (questions, courts, drawn cities, bracket) — safe since nothing has been played.
            if (req.Type != activity.Type)
            {
                if (activity.Status != ActivityStatus.Draft)
                {
                    throw new RuleViolationException(
                        "Change the activity's type only while it's a draft.", StatusCodes.Status409Conflict);
                }

                db.Questions.RemoveRange(db.Questions.Where(q => q.ActivityId == id));
                db.Courts.RemoveRange(db.Courts.Where(c => c.ActivityId == id));
                db.MapCities.RemoveRange(db.MapCities.Where(c => c.ActivityId == id));
                db.BracketMatches.RemoveRange(db.BracketMatches.Where(m => m.ActivityId == id));
                db.ScoreEntries.RemoveRange(db.ScoreEntries.Where(s => s.ActivityId == id));
                activity.Type = req.Type;
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
            // A music host must always see the Spotify links to play them — never hide them.
            activity.HideQuestionsFromHost = req.HideQuestionsFromHost && activity.Type != ActivityType.MusicQuiz;
            activity.IsPublic = req.IsPublic;
            activity.ScoreEntryMode = req.ScoreEntryMode;
            activity.RoundCount = Math.Clamp(req.RoundCount, 1, 50);
            activity.PlayersPerRound = req.PlayersPerRound is int ppr ? Math.Clamp(ppr, 1, 50) : null;
            activity.Latitude = req.Latitude;
            activity.Longitude = req.Longitude;
            activity.RadiusMeters = req.RadiusMeters;
            if (activity.Type == ActivityType.MapPin)
            {
                activity.MapCityCount = req.MapCityCount is int n ? Math.Clamp(n, 1, SwedishCities.All.Count) : 5;
                activity.ScoringMode = ScoringMode.LowerWins; // distance — lowest total wins, always
            }
            if (activity.Type == ActivityType.Memory)
            {
                activity.ScoringMode = ScoringMode.LowerWins; // fastest time / fewest flips wins, always
            }
            activity.MatchFormat = req.MatchFormat;
            activity.BestOfSets = req.BestOfSets is 1 or 3 or 5 ? req.BestOfSets : 3;
            activity.GamesToWinSet = Math.Clamp(req.GamesToWinSet, 1, 100);

            // Tournament (knockout) advanced options.
            activity.UseGroupStage = req.UseGroupStage;
            activity.GroupCount = Math.Clamp(req.GroupCount, 0, 32);
            activity.GroupMatchFormat = req.GroupMatchFormat;
            activity.GroupBestOfSets = req.GroupBestOfSets is 1 or 3 or 5 ? req.GroupBestOfSets : 1;
            activity.GroupGamesToWinSet = Math.Clamp(req.GroupGamesToWinSet, 1, 100);
            activity.AdvanceToPlayoffA = Math.Clamp(req.AdvanceToPlayoffA, 1, 16);
            activity.AdvanceToPlayoffB = Math.Clamp(req.AdvanceToPlayoffB, 0, 16);
            activity.PlayoffAConsolation = req.PlayoffAConsolation;
            activity.PlayoffBConsolation = req.PlayoffBConsolation;
            activity.UseManualSeeding = req.UseManualSeeding;
            activity.TournamentScoring = req.TournamentScoring;
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

            var eventId = activity.EventId;

            // Slaps reference the activity by a loose int (no FK), so the event cascade won't
            // clean them — remove them here or a deleted activity's penalty haunts the standings.
            await db.Slaps.Where(s => s.ActivityId == id).ExecuteDeleteAsync(ct);

            db.Activities.Remove(activity);
            await db.SaveChangesAsync(ct);

            // Close the gap in the running order so the remaining activities stay numbered 1, 2, 3, …
            if (eventId is { } evId)
            {
                var remaining = await db.Activities
                    .Where(a => a.EventId == evId)
                    .OrderBy(a => a.Order)
                    .ToListAsync(ct);
                for (var i = 0; i < remaining.Count; i++)
                {
                    remaining[i].Order = i + 1;
                }

                await db.SaveChangesAsync(ct);
            }

            return Results.NoContent();
        }).AddEndpointFilter<ActivityManagerFilter>();

        // --- Players: look up the activity --------------------------------------

        app.MapGet("/api/activities/{id:int}", async (
            int id, AppDbContext db, HttpContext http, RundanOptions options, CancellationToken ct) =>
        {
            var dto = await LoadDtoAsync(db, id, ct);
            if (dto is null)
            {
                return Results.NotFound();
            }

            dto.CanManage = await EventAuthorization.CanManageActivityAsync(http, db, options, id, ct);
            return Results.Ok(dto);
        });

        // The slap ceremony for one finished activity (pending to take, or the resolved outcome).
        app.MapGet("/api/activities/{id:int}/slap", async (int id, SlapService slaps, CancellationToken ct) =>
            Results.Ok(await slaps.ActivitySlapAsync(id, ct)));

        app.MapGet("/api/activities/by-code/{code}", async (
            string code, AppDbContext db, HttpContext http, RundanOptions options, CancellationToken ct) =>
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
            var dto = activity.ToDto(pc, qc);
            dto.CanManage = await EventAuthorization.CanManageActivityAsync(http, db, options, activity.Id, ct);
            return Results.Ok(dto);
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
