using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Endpoints;

internal static class EventEndpoints
{
    public static void MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        // --- Admin: create / list / manage --------------------------------------

        app.MapPost("/api/events", async (
            CreateEventRequest req, AppDbContext db, JoinCodeGenerator codes, TimeProvider clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                throw new RuleViolationException("Give the event a name.");
            }

            var ev = new Event
            {
                Name = req.Name.Trim(),
                Description = TextHelpers.Clean(req.Description),
                ImageUrl = TextHelpers.Clean(req.ImageUrl),
                TeamSize = Math.Clamp(req.TeamSize, 1, 20),
                JoinCode = await codes.NextAsync(db, ct),
                CreatedUtc = clock.GetUtcNow(),
            };
            db.Events.Add(ev);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/events/{ev.Id}", ev.ToDto(new(), new()));
        }).AddEndpointFilter<AdminEndpointFilter>();

        app.MapGet("/api/events", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await ListAllEventDtosAsync(db, ct))).AddEndpointFilter<AdminEndpointFilter>();

        // Player-facing: the events to show on the welcome page (access-gated, no admin code).
        app.MapGet("/api/events/active", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await ListAllEventDtosAsync(db, ct)));

        // A player reports their GPS; auto-start any OPEN activity whose geofence — or, for a
        // tipspromenad, a question's geofence — they've walked into. Location-verified, so it can't
        // be triggered from afar; only opened activities start (Draft ones aren't ready yet).
        app.MapPost("/api/events/{id:int}/arrive", async (
            int id, ArriveRequest req, AppDbContext db, ScoreboardNotifier notifier,
            PushService push, TimeProvider clock, CancellationToken ct) =>
        {
            var open = await db.Activities
                .Where(a => a.EventId == id && a.Status == ActivityStatus.Open)
                .ToListAsync(ct);
            if (open.Count == 0)
            {
                return Results.Ok(new { started = Array.Empty<int>() });
            }

            var tipsIds = open.Where(a => a.Type == ActivityType.Tipspromenad).Select(a => a.Id).ToList();
            var stations = tipsIds.Count == 0
                ? new List<(int ActivityId, double Lat, double Lng, int? Radius)>()
                : (await db.Questions.AsNoTracking()
                        .Where(q => tipsIds.Contains(q.ActivityId) && q.Latitude != null && q.Longitude != null)
                        .Select(q => new { q.ActivityId, q.Latitude, q.Longitude, q.RadiusMeters })
                        .ToListAsync(ct))
                    .Select(q => (ActivityId: q.ActivityId, Lat: q.Latitude!.Value, Lng: q.Longitude!.Value, Radius: q.RadiusMeters))
                    .ToList();

            static bool Within(double lat1, double lng1, double lat2, double lng2, int? radius) =>
                GeoMath.DistanceKm(lat1, lng1, lat2, lng2) * 1000 <= (radius is > 0 ? radius.Value : 25);

            var started = new List<int>();
            foreach (var a in open)
            {
                var here =
                    (a.Latitude is double alat && a.Longitude is double alng && Within(req.Lat, req.Lng, alat, alng, a.RadiusMeters))
                    || stations.Any(s => s.ActivityId == a.Id && Within(req.Lat, req.Lng, s.Lat, s.Lng, s.Radius));
                if (here)
                {
                    a.Status = ActivityStatus.Live;
                    a.StartedUtc ??= clock.GetUtcNow();
                    started.Add(a.Id);
                }
            }

            if (started.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                foreach (var aid in started)
                {
                    await notifier.PushStatusAsync(aid, ActivityStatus.Live);
                    var title = open.First(a => a.Id == aid).Title;
                    push.Notify(id, "📍 First arrival!", $"Someone reached “{title}” — it's live now.", $"e/{id}", $"live-{aid}");
                }
            }

            return Results.Ok(new { started });
        });

        // --- Group chat (everyone in the event) ---------------------------------
        app.MapGet("/api/events/{id:int}/chat", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var messages = await db.ChatMessages.AsNoTracking()
                .Where(m => m.EventId == id)
                .OrderByDescending(m => m.Id).Take(200)
                .Select(m => new ChatMessageDto { Id = m.Id, Author = m.Author, Text = m.Text, CreatedUtc = m.CreatedUtc })
                .ToListAsync(ct);
            messages.Reverse(); // oldest first for display
            return Results.Ok(messages);
        });

        app.MapPost("/api/events/{id:int}/chat", async (
            int id, PostChatMessageRequest req, AppDbContext db, ScoreboardNotifier notifier,
            PushService push, TimeProvider clock, CancellationToken ct) =>
        {
            var text = (req.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                throw new RuleViolationException("Type a message first.");
            }
            if (text.Length > 1000)
            {
                text = text[..1000];
            }

            var author = (req.Author ?? string.Empty).Trim();
            author = author.Length == 0 ? "Someone" : author.Length > 60 ? author[..60] : author;

            if (!await db.Events.AnyAsync(e => e.Id == id, ct))
            {
                return Results.NotFound();
            }

            var msg = new ChatMessage { EventId = id, Author = author, Text = text, CreatedUtc = clock.GetUtcNow() };
            db.ChatMessages.Add(msg);
            await db.SaveChangesAsync(ct);

            var dto = new ChatMessageDto { Id = msg.Id, Author = msg.Author, Text = msg.Text, CreatedUtc = msg.CreatedUtc };
            await notifier.PushChatAsync(id, dto);
            push.Notify(id, $"💬 {dto.Author}", dto.Text, $"e/{id}", "chat");
            return Results.Ok(dto);
        });

        // --- Web Push (notifications) -------------------------------------------
        app.MapGet("/api/push/key", (PushService push) => Results.Ok(new PushKeyDto { PublicKey = push.PublicKey }));

        app.MapPost("/api/events/{id:int}/push/subscribe", async (
            int id, PushSubscribeRequest req, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Endpoint) || string.IsNullOrWhiteSpace(req.P256dh) || string.IsNullOrWhiteSpace(req.Auth))
            {
                throw new RuleViolationException("Invalid push subscription.");
            }
            if (!await db.Events.AnyAsync(e => e.Id == id, ct))
            {
                return Results.NotFound();
            }

            // One row per device endpoint — re-point it at the event they just subscribed under.
            var existing = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == req.Endpoint, ct);
            if (existing is null)
            {
                db.PushSubscriptions.Add(new PushSubscription
                {
                    EventId = id, Endpoint = req.Endpoint, P256dh = req.P256dh, Auth = req.Auth, CreatedUtc = clock.GetUtcNow(),
                });
            }
            else
            {
                existing.EventId = id;
                existing.P256dh = req.P256dh;
                existing.Auth = req.Auth;
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Viewers (spectators): register / heartbeat / leave — access-gated, no competing identity.
        app.MapPost("/api/events/{id:int}/viewers", async (
            int id, RegisterViewerRequest req, AppDbContext db, ScoreboardNotifier notifier, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await db.Events.AnyAsync(e => e.Id == id, ct))
            {
                return Results.NotFound();
            }

            var name = (req.Name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                throw new RuleViolationException("Enter a name to watch.");
            }

            if (name.Length > 60)
            {
                name = name[..60];
            }

            EventViewer? viewer = null;
            if (req.Token is { } token)
            {
                viewer = await db.EventViewers.FirstOrDefaultAsync(v => v.Token == token && v.EventId == id, ct);
            }

            if (viewer is null)
            {
                viewer = new EventViewer { EventId = id, Token = Guid.NewGuid() };
                db.EventViewers.Add(viewer);
            }

            viewer.Name = name;
            viewer.LastSeenUtc = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);

            await notifier.PushViewersAsync(id, await CurrentViewerNamesAsync(db, id, ct));
            return Results.Ok(new ViewerDto { Token = viewer.Token, Name = viewer.Name });
        });

        app.MapDelete("/api/events/{id:int}/viewers/{token:guid}", async (
            int id, Guid token, AppDbContext db, ScoreboardNotifier notifier, CancellationToken ct) =>
        {
            var viewer = await db.EventViewers.FirstOrDefaultAsync(v => v.Token == token && v.EventId == id, ct);
            if (viewer is not null)
            {
                db.EventViewers.Remove(viewer);
                await db.SaveChangesAsync(ct);
                await notifier.PushViewersAsync(id, await CurrentViewerNamesAsync(db, id, ct));
            }

            return Results.NoContent();
        });

        app.MapPut("/api/events/{id:int}/reorder", async (
            int id, ReorderActivitiesRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var activities = await db.Activities.Where(a => a.EventId == id).ToListAsync(ct);
            var order = 1;
            foreach (var activityId in req.ActivityIds)
            {
                var match = activities.FirstOrDefault(a => a.Id == activityId);
                if (match is not null)
                {
                    match.Order = order++;
                }
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).AddEndpointFilter<EventManagerFilter>();

        // Host: bulk-reset every activity to Draft (fresh, not opened) or Open (ready to start),
        // without touching scores. Confirmed in the UI.
        app.MapPut("/api/events/{id:int}/activities/status", async (
            int id, UpdateActivityStatusRequest req, AppDbContext db, ScoreboardNotifier notifier, CancellationToken ct) =>
        {
            if (req.Status is not (ActivityStatus.Draft or ActivityStatus.Open))
            {
                throw new RuleViolationException("Resetting all activities only supports Draft or Open.");
            }

            var activities = await db.Activities.Where(a => a.EventId == id).ToListAsync(ct);
            var changed = new List<int>();
            foreach (var a in activities.Where(a => a.Status != req.Status))
            {
                a.Status = req.Status;
                a.FinishedUtc = null;
                if (req.Status == ActivityStatus.Draft)
                {
                    a.StartedUtc = null;
                }

                changed.Add(a.Id);
            }

            if (changed.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                foreach (var aid in changed)
                {
                    await notifier.PushStatusAsync(aid, req.Status);
                }
            }

            return Results.NoContent();
        }).AddEndpointFilter<EventManagerFilter>();

        app.MapDelete("/api/events/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var ev = await db.Events.FindAsync([id], ct);
            if (ev is null)
            {
                return Results.NotFound();
            }

            db.Events.Remove(ev);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).AddEndpointFilter<AdminEndpointFilter>();

        app.MapPut("/api/events/{id:int}", async (int id, UpdateEventRequest req, AppDbContext db, TeamService teams, CancellationToken ct) =>
        {
            var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == id, ct)
                ?? throw new RuleViolationException("Event not found.", StatusCodes.Status404NotFound);
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                throw new RuleViolationException("Give the event a name.");
            }

            if (req.StartsAt is { } start && req.EndsAt is { } end && end <= start)
            {
                throw new RuleViolationException("The event's end time must be after its start.");
            }

            var teamModeChanged = ev.TeamShuffle != req.TeamShuffle;

            ev.Name = req.Name.Trim();
            ev.Description = TextHelpers.Clean(req.Description);
            ev.ImageUrl = TextHelpers.Clean(req.ImageUrl);
            ev.TeamSize = Math.Clamp(req.TeamSize, 1, 20);
            ev.Scoring = req.Scoring;
            ev.TeamShuffle = req.TeamShuffle;
            // Fixed mode needs a seed to lock teams; give it one the first time it's selected.
            if (ev.TeamShuffle == TeamShuffle.FixedForEvent && ev.FixedTeamSeed == 0)
            {
                ev.FixedTeamSeed = NextTeamSeed(0);
            }

            ev.SlapMode = req.SlapMode;
            ev.StartsAt = req.StartsAt;
            ev.EndsAt = req.EndsAt;
            await db.SaveChangesAsync(ct);

            // Switching how teams are formed re-forms the not-yet-played activities' teams.
            if (teamModeChanged)
            {
                await teams.ResetUnplayedTeamsAsync(id, ct);
            }

            return Results.Ok(await LoadEventDtoAsync(db, ev, ct));
        }).AddEndpointFilter<EventManagerFilter>();

        // Host re-rolls the locked teams of a fixed-team event: a fresh seed, regenerate the
        // not-yet-played activities' teams, and return the new line-up to preview.
        app.MapPost("/api/events/{id:int}/teams/reshuffle", async (
            int id, AppDbContext db, TeamService teams, CancellationToken ct) =>
        {
            var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == id, ct)
                ?? throw new RuleViolationException("Event not found.", StatusCodes.Status404NotFound);

            ev.TeamShuffle = TeamShuffle.FixedForEvent;
            ev.FixedTeamSeed = NextTeamSeed(ev.FixedTeamSeed);
            await db.SaveChangesAsync(ct);

            await teams.ResetUnplayedTeamsAsync(id, ct);
            return Results.Ok(await teams.PreviewTeamsAsync(ev, ct));
        }).AddEndpointFilter<EventManagerFilter>();

        // The team line-up the event's roster currently forms (host view; the locked set in fixed mode).
        app.MapGet("/api/events/{id:int}/teams", async (
            int id, AppDbContext db, TeamService teams, CancellationToken ct) =>
        {
            var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == id, ct);
            return ev is null ? Results.NotFound() : Results.Ok(await teams.PreviewTeamsAsync(ev, ct));
        }).AddEndpointFilter<EventManagerFilter>();

        app.MapPut("/api/events/{id:int}/members", async (
            int id, SetEventMembersRequest req, AppDbContext db, ScoreboardNotifier notifier,
            TimeProvider clock, CancellationToken ct) =>
        {
            var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == id, ct)
                ?? throw new RuleViolationException("Event not found.", StatusCodes.Status404NotFound);

            var wanted = (await db.Users.Where(u => req.UserIds.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct))
                .ToHashSet();
            var admins = req.AdminUserIds.ToHashSet();
            var current = await db.EventMembers.Where(m => m.EventId == id).ToListAsync(ct);
            var currentIds = current.Select(m => m.UserId).ToHashSet();

            foreach (var m in current.Where(m => !wanted.Contains(m.UserId)))
            {
                db.EventMembers.Remove(m);
            }

            // Update the admin flag on members we're keeping.
            foreach (var m in current.Where(m => wanted.Contains(m.UserId)))
            {
                m.IsAdmin = admins.Contains(m.UserId);
            }

            foreach (var uid in wanted.Where(uid => !currentIds.Contains(uid)))
            {
                db.EventMembers.Add(new EventMember
                {
                    EventId = id, UserId = uid, Token = Guid.NewGuid(),
                    IsAdmin = admins.Contains(uid), AddedUtc = clock.GetUtcNow(),
                });
            }

            await db.SaveChangesAsync(ct);

            // Tell connected players so a just-(de)selected admin's host controls update live.
            await notifier.PushEventChangedAsync(id);

            return Results.Ok(await LoadEventDtoAsync(db, ev, ct));
        }).AddEndpointFilter<AdminEndpointFilter>();

        app.MapPut("/api/events/{id:int}/code", async (
            int id, SetEventCodeRequest req, AppDbContext db, JoinCodeGenerator codes, CancellationToken ct) =>
        {
            var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == id, ct)
                ?? throw new RuleViolationException("Event not found.", StatusCodes.Status404NotFound);

            var code = (req.Code ?? string.Empty).Trim().ToUpperInvariant();
            if (code.Length == 0)
            {
                ev.JoinCode = await codes.NextAsync(db, ct);
            }
            else
            {
                if (code.Length is < 3 or > 16 || !code.All(c => char.IsLetterOrDigit(c) || c == '-'))
                {
                    throw new RuleViolationException("A code must be 3–16 letters, numbers or dashes.");
                }

                var taken = await db.Events.AnyAsync(e => e.JoinCode == code && e.Id != id, ct)
                            || await db.Activities.AnyAsync(a => a.JoinCode == code, ct);
                if (taken)
                {
                    throw new RuleViolationException("That code is already in use.", StatusCodes.Status409Conflict);
                }

                ev.JoinCode = code;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(await LoadEventDtoAsync(db, ev, ct));
        }).AddEndpointFilter<EventManagerFilter>();

        // --- Players: look up + standings ---------------------------------------

        app.MapGet("/api/events/{id:int}", async (int id, AppDbContext db, SlapService slaps, CancellationToken ct) =>
        {
            var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
            if (ev is null)
            {
                return Results.NotFound();
            }

            var dto = await LoadEventDtoAsync(db, ev, ct);
            dto.PendingSlap = await slaps.PendingAsync(id, ct);
            return Results.Ok(dto);
        });

        app.MapGet("/api/events/by-code/{code}", async (string code, AppDbContext db, SlapService slaps, CancellationToken ct) =>
        {
            var normalized = code.Trim().ToUpperInvariant();
            var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.JoinCode == normalized, ct);
            if (ev is null)
            {
                return Results.NotFound();
            }

            var dto = await LoadEventDtoAsync(db, ev, ct);
            dto.PendingSlap = await slaps.PendingAsync(ev.Id, ct);
            return Results.Ok(dto);
        });

        // --- Slaps (the optional twist) -----------------------------------------
        app.MapPost("/api/events/{id:int}/slap", async (
            int id, PerformSlapRequest req, AppDbContext db, SlapService slaps,
            ScoreboardNotifier notifier, HttpContext http, CancellationToken ct) =>
        {
            // The slapper must prove they are a member of this event (their event-member token → userId).
            var slapperUserId = await ResolveMemberUserIdAsync(http, db, id, ct)
                ?? throw new RuleViolationException("Only a player in this event can slap.", StatusCodes.Status403Forbidden);

            await slaps.PerformAsync(id, req.ActivityId, slapperUserId, req.SlappedUserId, req.RecipientUserId, ct);
            await notifier.PushScoreboardAsync(req.ActivityId, ct); // nudges everyone's standings to refresh
            return Results.Ok(new { ok = true });
        });

        // SlappedSends mode: the slapped player passes their lost points to someone.
        app.MapPost("/api/events/{id:int}/slap/send-points", async (
            int id, SendSlapPointsRequest req, AppDbContext db, SlapService slaps,
            ScoreboardNotifier notifier, HttpContext http, CancellationToken ct) =>
        {
            // The sender must prove they are the slapped player (their event-member token → userId).
            var senderUserId = await ResolveMemberUserIdAsync(http, db, id, ct)
                ?? throw new RuleViolationException("Only a player in this event can do that.", StatusCodes.Status403Forbidden);

            await slaps.SendPointsAsync(id, req.ActivityId, senderUserId, req.RecipientUserId, ct);
            await notifier.PushScoreboardAsync(req.ActivityId, ct); // nudges everyone's standings to refresh
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/events/{id:int}/slap/skip", async (
            int id, SkipSlapRequest req, AppDbContext db, RundanOptions options,
            SlapService slaps, ScoreboardNotifier notifier, HttpContext http, CancellationToken ct) =>
        {
            if (!await EventAuthorization.CanManageEventAsync(http, db, options, id, ct))
            {
                return EventManagerFilter.Forbidden();
            }

            await slaps.SkipAsync(id, req.ActivityId, ct);
            await notifier.PushScoreboardAsync(req.ActivityId, ct);
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/events/{id:int}/standings", async (
            int id, EventStandingsService standings, CancellationToken ct) =>
        {
            var dto = await standings.BuildAsync(id, ct);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });

        // --- Players: join the whole event with one name ------------------------
        // Additive: joins every currently-joinable activity the name isn't already in.
        // Call again (e.g. when the host opens the next activity) to pick up new ones.

        app.MapPost("/api/events/by-code/{code}/join", async (
            string code, EventJoinRequest req, AppDbContext db, ScoreboardNotifier notifier,
            RundanOptions options, TimeProvider clock, HttpContext http, CancellationToken ct) =>
        {
            var normalized = code.Trim().ToUpperInvariant();
            var ev = await db.Events.Include(e => e.Activities)
                .FirstOrDefaultAsync(e => e.JoinCode == normalized, ct)
                ?? throw new RuleViolationException("No event with that code.", StatusCodes.Status404NotFound);

            var name = (req.DisplayName ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                throw new RuleViolationException("Enter a name to join with.");
            }

            if (name.Length > 60)
            {
                name = name[..60];
            }

            var isAdmin = options.RequiresAdminCode && SecurityHelpers.FixedEquals(
                http.Request.Headers[AdminEndpointFilter.HeaderName].ToString(), options.AdminCode);

            var joinable = ev.Activities
                .Where(a => a.Status is ActivityStatus.Open or ActivityStatus.Live)
                .OrderBy(a => a.Order)
                .ToList();

            var created = new List<(int ActivityId, Participant Participant)>();
            foreach (var act in joinable)
            {
                var taken = await db.Participants.AnyAsync(p => p.ActivityId == act.Id && p.DisplayName == name, ct);
                if (taken)
                {
                    continue; // already in this activity (this device re-joining, or name reused)
                }

                var participant = new Participant
                {
                    ActivityId = act.Id,
                    DisplayName = name,
                    Token = Guid.NewGuid(),
                    IsAdmin = isAdmin,
                    JoinedUtc = clock.GetUtcNow(),
                };
                db.Participants.Add(participant);
                created.Add((act.Id, participant));
            }

            if (created.Count > 0)
            {
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    throw new RuleViolationException("That name was just taken — pick another.",
                        StatusCodes.Status409Conflict);
                }

                foreach (var (activityId, participant) in created)
                {
                    await notifier.PushParticipantJoinedAsync(activityId, participant.ToDto());
                    await notifier.PushScoreboardAsync(activityId, ct);
                }
            }

            return Results.Ok(new EventJoinResultDto
            {
                EventId = ev.Id,
                DisplayName = name,
                Slots = created.Select(c => new EventJoinSlotDto
                {
                    ActivityId = c.ActivityId,
                    ParticipantId = c.Participant.Id,
                    Token = c.Participant.Token,
                }).ToList(),
            });
        });

        // --- Roster events: claim your pre-registered identity ------------------
        // Returns your TEAM session for each joinable activity (generating teams as needed).
        // Idempotent — call again when the host opens the next activity.
        app.MapPost("/api/events/by-code/{code}/claim", async (
            string code, ClaimRequest req, AppDbContext db, TeamService teams,
            ScoreboardNotifier notifier, CancellationToken ct) =>
        {
            var normalized = code.Trim().ToUpperInvariant();
            var ev = await db.Events.Include(e => e.Activities)
                .FirstOrDefaultAsync(e => e.JoinCode == normalized, ct)
                ?? throw new RuleViolationException("No event with that code.", StatusCodes.Status404NotFound);

            var member = await db.EventMembers.Include(m => m.User)
                .FirstOrDefaultAsync(m => m.EventId == ev.Id && m.UserId == req.UserId, ct)
                ?? throw new RuleViolationException("That player isn't on this event's roster.",
                    StatusCodes.Status404NotFound);

            var slots = new List<EventJoinSlotDto>();
            foreach (var act in ev.Activities
                         .Where(a => a.Status is ActivityStatus.Open or ActivityStatus.Live)
                         .OrderBy(a => a.Order))
            {
                var generated = await teams.EnsureTeamsAsync(act, ct);
                var myTeam = generated.FirstOrDefault(t => t.Members.Any(m => m.UserId == req.UserId));
                if (myTeam is not null)
                {
                    slots.Add(new EventJoinSlotDto
                    {
                        ActivityId = act.Id,
                        ParticipantId = myTeam.Id,
                        Token = myTeam.Token,
                        TeamName = myTeam.DisplayName,
                    });
                    await notifier.PushScoreboardAsync(act.Id, ct);
                }
            }

            return Results.Ok(new ClaimResultDto
            {
                EventId = ev.Id,
                UserId = req.UserId,
                DisplayName = member.User!.Name,
                MemberToken = member.Token,
                IsEventAdmin = member.IsAdmin,
                Slots = slots,
            });
        });

        // The teams (partner mixer output) for one activity.
        app.MapGet("/api/activities/{id:int}/teams", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var teamList = await db.Participants.AsNoTracking()
                .Include(p => p.Members).ThenInclude(m => m.User)
                .Where(p => p.ActivityId == id && p.IsTeam)
                .OrderBy(p => p.Id)
                .ToListAsync(ct);

            var dto = teamList.Select(t => new TeamDto
            {
                ActivityId = id,
                ParticipantId = t.Id,
                Name = t.DisplayName,
                Members = t.Members.Select(m => new UserDto { Id = m.UserId, Name = m.User!.Name }).ToList(),
            }).ToList();
            return Results.Ok(dto);
        });
    }

    // A fresh, non-zero partner-mixer seed (zero means "never shuffled"); avoids repeating the current one.
    private static int NextTeamSeed(int current)
    {
        int seed;
        do
        {
            seed = Random.Shared.Next(1, 1_000_000);
        }
        while (seed == current);

        return seed;
    }

    // Newest-first list of every event as DTOs (SQLite can't ORDER BY DateTimeOffset; Id ≈ creation order).
    private static async Task<List<EventDto>> ListAllEventDtosAsync(AppDbContext db, CancellationToken ct)
    {
        var events = await db.Events.AsNoTracking().OrderByDescending(e => e.Id).ToListAsync(ct);
        var result = new List<EventDto>();
        foreach (var ev in events)
        {
            result.Add(await LoadEventDtoAsync(db, ev, ct));
        }

        return result;
    }

    private static async Task<EventDto> LoadEventDtoAsync(AppDbContext db, Event ev, CancellationToken ct)
    {
        var rows = await db.Activities.AsNoTracking()
            .Where(a => a.EventId == ev.Id)
            .OrderBy(a => a.Order).ThenBy(a => a.Id)
            .Select(a => new { Activity = a, Pc = a.Participants.Count, Qc = a.Questions.Count })
            .ToListAsync(ct);

        var memberRows = await db.EventMembers.AsNoTracking()
            .Where(m => m.EventId == ev.Id)
            .OrderBy(m => m.User!.Name)
            .Select(m => new { m.UserId, Name = m.User!.Name, m.IsAdmin })
            .ToListAsync(ct);

        var members = memberRows.Select(m => new UserDto { Id = m.UserId, Name = m.Name }).ToList();
        var dtos = rows.Select(r => r.Activity.ToDto(r.Pc, r.Qc)).ToList();
        var dto = ev.ToDto(dtos, members);
        dto.AdminUserIds = memberRows.Where(m => m.IsAdmin).Select(m => m.UserId).ToList();
        dto.EstimatedMeters = await EstimateRouteMetersAsync(db, ev.Id, rows.Select(r => r.Activity).ToList(), ct);

        dto.Viewers = await CurrentViewerNamesAsync(db, ev.Id, ct);
        return dto;
    }

    /// <summary>
    /// Walks the geolocated route in running order — each Tipspromenad's stations (in order),
    /// then any single activity geofence — and sums the leg distances. Null if fewer than two points.
    /// </summary>
    private static async Task<int?> EstimateRouteMetersAsync(
        AppDbContext db, int eventId, List<Activity> activities, CancellationToken ct)
    {
        var stations = await db.Questions.AsNoTracking()
            .Where(q => q.Activity!.EventId == eventId && q.Latitude != null && q.Longitude != null)
            .Select(q => new { q.ActivityId, q.Order, Lat = q.Latitude!.Value, Lng = q.Longitude!.Value })
            .ToListAsync(ct);

        var points = new List<(double Lat, double Lng)>();
        foreach (var a in activities) // already ordered by running order
        {
            var qs = stations.Where(s => s.ActivityId == a.Id).OrderBy(s => s.Order).ToList();
            if (qs.Count > 0)
            {
                points.AddRange(qs.Select(s => (s.Lat, s.Lng)));
            }
            else if (a.Latitude is { } lat && a.Longitude is { } lng)
            {
                points.Add((lat, lng));
            }
        }

        if (points.Count < 2)
        {
            return null;
        }

        var metres = 0.0;
        for (var i = 1; i < points.Count; i++)
        {
            metres += Haversine(points[i - 1].Lat, points[i - 1].Lng, points[i].Lat, points[i].Lng);
        }

        return (int)Math.Round(metres);
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusM = 6_371_000;
        static double Rad(double d) => d * Math.PI / 180.0;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var h = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
                + (Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        return earthRadiusM * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
    }

    // Recently-seen viewer names (filtered in memory — SQLite can't compare DateTimeOffset in a query).
    private static async Task<List<string>> CurrentViewerNamesAsync(AppDbContext db, int eventId, CancellationToken ct)
    {
        var rows = await db.EventViewers.AsNoTracking()
            .Where(v => v.EventId == eventId)
            .Select(v => new { v.Name, v.LastSeenUtc })
            .ToListAsync(ct);

        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-15);
        return rows
            .Where(v => v.LastSeenUtc >= cutoff)
            .Select(v => v.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Maps the caller's event-member token to their roster user id (proves who they are).
    private static async Task<int?> ResolveMemberUserIdAsync(HttpContext http, AppDbContext db, int eventId, CancellationToken ct)
    {
        if (!Guid.TryParse(http.Request.Headers[EventAuthorization.MemberHeader].FirstOrDefault(), out var token))
        {
            return null;
        }

        return await db.EventMembers
            .Where(m => m.Token == token && m.EventId == eventId)
            .Select(m => (int?)m.UserId)
            .FirstOrDefaultAsync(ct);
    }
}
