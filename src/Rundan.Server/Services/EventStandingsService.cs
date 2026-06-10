using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Services;

/// <summary>
/// Builds the combined event standings. Two scoring models:
///  - Cumulative: sum the actual points each team scored across activities.
///  - Placement:  in each FINISHED activity, rank teams and award position points
///                (1st = number of teams, then 3, 2, 1; ties share the higher points),
///                credited to each member.
/// A team's points are always credited to EACH of its members individually.
/// </summary>
public sealed class EventStandingsService(AppDbContext db, TimeProvider clock)
{
    public async Task<EventStandingsDto?> BuildAsync(int eventId, CancellationToken ct = default)
    {
        var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null)
        {
            return null;
        }

        var memberUsers = await db.EventMembers.AsNoTracking()
            .Where(m => m.EventId == eventId)
            .Select(m => new { m.UserId, Name = m.User!.Name })
            .ToListAsync(ct);

        var entries = memberUsers.Count > 0
            ? await BuildRosterAsync(eventId, ev.Scoring, memberUsers.Select(m => (m.UserId, m.Name)).ToList(), ct)
            : await BuildFreeNameAsync(eventId, ev.Scoring, ct);

        Rank(entries);

        return new EventStandingsDto
        {
            EventId = ev.Id,
            Name = ev.Name,
            Entries = entries,
            UpdatedUtc = clock.GetUtcNow(),
        };
    }

    private async Task<List<EventStandingEntryDto>> BuildRosterAsync(
        int eventId, EventScoring scoring, List<(int UserId, string Name)> roster, CancellationToken ct)
    {
        var pointsByParticipant = await PointsByParticipantAsync(eventId, ct);
        var teamParts = await db.Participants.AsNoTracking()
            .Where(p => p.Activity!.EventId == eventId && p.IsTeam)
            .Select(p => new { p.Id, p.ActivityId })
            .ToListAsync(ct);
        var membersByParticipant = (await db.ParticipantMembers.AsNoTracking()
                .Where(pm => pm.Participant!.Activity!.EventId == eventId)
                .Select(pm => new { pm.ParticipantId, pm.UserId })
                .ToListAsync(ct))
            .GroupBy(x => x.ParticipantId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.UserId).ToList());

        var totals = roster.ToDictionary(u => u.UserId, _ => 0d);
        var played = roster.ToDictionary(u => u.UserId, _ => new HashSet<int>());

        void Credit(int participantId, double points, int activityId)
        {
            if (!membersByParticipant.TryGetValue(participantId, out var users))
            {
                return;
            }

            // "Played" means the team actually has answer/score rows for the activity — not
            // merely that the net points are non-zero (all-wrong or canceling entries net to 0).
            var participated = pointsByParticipant.ContainsKey(participantId);
            foreach (var uid in users)
            {
                if (!totals.ContainsKey(uid))
                {
                    continue;
                }

                totals[uid] += points;
                if (participated)
                {
                    played[uid].Add(activityId);
                }
            }
        }

        if (scoring == EventScoring.Placement)
        {
            await AwardPlacementAsync(eventId,
                teamParts.Select(t => (t.Id, t.ActivityId)).ToList(), pointsByParticipant, Credit, ct);
        }
        else
        {
            foreach (var t in teamParts)
            {
                Credit(t.Id, pointsByParticipant.GetValueOrDefault(t.Id), t.ActivityId);
            }
        }

        // Apply slap penalties: the slapped player loses points; a "send" slap gives them to a recipient.
        // Track lost/received per player so the standings can show the slap columns when slaps are on.
        var lost = roster.ToDictionary(u => u.UserId, _ => 0d);
        var received = roster.ToDictionary(u => u.UserId, _ => 0d);
        var slaps = await db.Slaps.AsNoTracking().Where(s => s.EventId == eventId && !s.Skipped).ToListAsync(ct);
        foreach (var s in slaps)
        {
            if (totals.ContainsKey(s.SlappedUserId))
            {
                totals[s.SlappedUserId] -= s.Penalty;
                lost[s.SlappedUserId] += s.Penalty;
            }

            if (s.RecipientUserId is { } r && totals.ContainsKey(r))
            {
                totals[r] += s.Penalty;
                received[r] += s.Penalty;
            }
        }

        return roster.Select(u => new EventStandingEntryDto
        {
            UserId = u.UserId,
            DisplayName = u.Name,
            TotalPoints = totals[u.UserId],
            ActivitiesPlayed = played[u.UserId].Count,
            SlapLost = lost[u.UserId],
            SlapReceived = received[u.UserId],
        }).ToList();
    }

    private async Task<List<EventStandingEntryDto>> BuildFreeNameAsync(
        int eventId, EventScoring scoring, CancellationToken ct)
    {
        var pointsByParticipant = await PointsByParticipantAsync(eventId, ct);
        var parts = await db.Participants.AsNoTracking()
            .Where(p => p.Activity!.EventId == eventId)
            .Select(p => new { p.Id, p.ActivityId, p.DisplayName })
            .ToListAsync(ct);

        var totals = new Dictionary<string, double>(StringComparer.Ordinal);
        var played = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var name in parts.Select(p => p.DisplayName).Distinct())
        {
            totals[name] = 0;
            played[name] = new();
        }

        var nameByParticipant = parts.ToDictionary(p => p.Id, p => p.DisplayName);

        void Credit(int participantId, double points, int activityId)
        {
            if (!nameByParticipant.TryGetValue(participantId, out var name))
            {
                return;
            }

            totals[name] = totals.GetValueOrDefault(name) + points;
            if (pointsByParticipant.ContainsKey(participantId)) // actually has rows for this activity
            {
                played[name].Add(activityId);
            }
        }

        if (scoring == EventScoring.Placement)
        {
            await AwardPlacementAsync(eventId,
                parts.Select(p => (p.Id, p.ActivityId)).ToList(), pointsByParticipant, Credit, ct);
        }
        else
        {
            foreach (var p in parts)
            {
                Credit(p.Id, pointsByParticipant.GetValueOrDefault(p.Id), p.ActivityId);
            }
        }

        return totals.Select(kv => new EventStandingEntryDto
        {
            DisplayName = kv.Key,
            TotalPoints = kv.Value,
            ActivitiesPlayed = played[kv.Key].Count,
        }).ToList();
    }

    /// <summary>Sum of answer points + score points per participant across the event.</summary>
    private async Task<Dictionary<int, double>> PointsByParticipantAsync(int eventId, CancellationToken ct)
    {
        var points = new Dictionary<int, double>();

        var answerRows = await db.Answers.AsNoTracking()
            .Where(a => a.Participant!.Activity!.EventId == eventId)
            .Select(a => new { a.ParticipantId, a.AwardedPoints })
            .ToListAsync(ct);
        foreach (var r in answerRows)
        {
            points[r.ParticipantId] = points.GetValueOrDefault(r.ParticipantId) + r.AwardedPoints;
        }

        var scoreRows = await db.ScoreEntries.AsNoTracking()
            .Where(s => s.Activity!.EventId == eventId)
            .Select(s => new { s.ParticipantId, s.Points })
            .ToListAsync(ct);
        foreach (var r in scoreRows)
        {
            points[r.ParticipantId] = points.GetValueOrDefault(r.ParticipantId) + r.Points;
        }

        return points;
    }

    /// <summary>
    /// Ranks the participants within each FINISHED activity and credits placement points
    /// (1st = number of participants, then descending by 1; ties share the higher points).
    /// </summary>
    private async Task AwardPlacementAsync(
        int eventId, List<(int ParticipantId, int ActivityId)> participants,
        Dictionary<int, double> pointsByParticipant, Action<int, double, int> credit, CancellationToken ct)
    {
        var activities = await db.Activities.AsNoTracking()
            .Where(a => a.EventId == eventId)
            .Select(a => new { a.Id, a.Status, a.ScoringMode, a.TargetValue })
            .ToListAsync(ct);
        var modeByActivity = activities.ToDictionary(a => a.Id, a => (a.ScoringMode, Target: a.TargetValue ?? 0));
        var finished = activities.Where(a => a.Status == ActivityStatus.Finished).Select(a => a.Id).ToHashSet();

        foreach (var group in participants.GroupBy(p => p.ActivityId))
        {
            if (!finished.Contains(group.Key))
            {
                continue; // placement is awarded once an activity is finished
            }

            var n = group.Count();
            var (mode, target) = modeByActivity.GetValueOrDefault(group.Key, (ScoringMode.HigherWins, 0));
            var pushUnscoredLast = ScoringHelper.PushesUnscoredLast(mode);

            var ordered = group
                .Select(p => new
                {
                    p.ParticipantId,
                    Score = pointsByParticipant.GetValueOrDefault(p.ParticipantId),
                    // A team that recorded nothing must not win a lowest/closest game with its seeded 0.
                    Unscored = pushUnscoredLast && !pointsByParticipant.ContainsKey(p.ParticipantId),
                })
                .OrderBy(x => x.Unscored)
                .ThenBy(x => ScoringHelper.RankKey(mode, x.Score, target))
                .ToList();

            var position = 0;
            double? previousKey = null;
            bool? previousUnscored = null;
            var seen = 0;
            foreach (var entry in ordered)
            {
                seen++;
                var key = ScoringHelper.RankKey(mode, entry.Score, target);
                if (previousKey is null || key != previousKey || entry.Unscored != previousUnscored)
                {
                    position = seen; // competition ranking: ties share the higher position
                    previousKey = key;
                    previousUnscored = entry.Unscored;
                }

                credit(entry.ParticipantId, n - position + 1, group.Key);
            }
        }
    }

    private static void Rank(List<EventStandingEntryDto> entries)
    {
        var ordered = entries
            .OrderByDescending(e => e.TotalPoints)
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rank = 0;
        double? prev = null;
        var seen = 0;
        foreach (var e in ordered)
        {
            seen++;
            if (prev is null || e.TotalPoints != prev)
            {
                rank = seen;
                prev = e.TotalPoints;
            }

            e.Rank = rank;
        }

        entries.Clear();
        entries.AddRange(ordered);
    }
}
