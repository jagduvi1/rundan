using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Services;

/// <summary>
/// Builds the combined event standings — every player's points across all activities.
/// In a roster event, a team's points on an activity are credited to EACH member's
/// individual total (the partner-mixer model). Rosterless events aggregate by name.
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
            ? await BuildRosterStandingsAsync(eventId, memberUsers.Select(m => (m.UserId, m.Name)).ToList(), ct)
            : await BuildFreeNameStandingsAsync(eventId, ct);

        Rank(entries);

        return new EventStandingsDto
        {
            EventId = ev.Id,
            Name = ev.Name,
            Entries = entries,
            UpdatedUtc = clock.GetUtcNow(),
        };
    }

    private async Task<List<EventStandingEntryDto>> BuildRosterStandingsAsync(
        int eventId, List<(int UserId, string Name)> roster, CancellationToken ct)
    {
        // Points earned by each team participant.
        var answerRows = await db.Answers.AsNoTracking()
            .Where(a => a.Participant!.Activity!.EventId == eventId)
            .Select(a => new { Pid = a.ParticipantId, a.AwardedPoints })
            .ToListAsync(ct);
        var scoreRows = await db.ScoreEntries.AsNoTracking()
            .Where(s => s.Activity!.EventId == eventId)
            .Select(s => new { Pid = s.ParticipantId, s.Points })
            .ToListAsync(ct);

        var teamPoints = new Dictionary<int, int>();
        foreach (var r in answerRows)
        {
            teamPoints[r.Pid] = teamPoints.GetValueOrDefault(r.Pid) + r.AwardedPoints;
        }

        foreach (var r in scoreRows)
        {
            teamPoints[r.Pid] = teamPoints.GetValueOrDefault(r.Pid) + r.Points;
        }

        // Team participant -> activity, and team participant -> member users.
        var partActivity = await db.Participants.AsNoTracking()
            .Where(p => p.Activity!.EventId == eventId && p.IsTeam)
            .Select(p => new { p.Id, p.ActivityId })
            .ToListAsync(ct);
        var partToActivity = partActivity.ToDictionary(x => x.Id, x => x.ActivityId);

        var memberships = await db.ParticipantMembers.AsNoTracking()
            .Where(pm => pm.Participant!.Activity!.EventId == eventId)
            .Select(pm => new { pm.ParticipantId, pm.UserId })
            .ToListAsync(ct);

        var totals = roster.ToDictionary(u => u.UserId, _ => 0);
        var activitiesPlayed = roster.ToDictionary(u => u.UserId, _ => new HashSet<int>());

        foreach (var m in memberships)
        {
            if (!totals.ContainsKey(m.UserId))
            {
                continue; // user removed from roster
            }

            var pts = teamPoints.GetValueOrDefault(m.ParticipantId);
            totals[m.UserId] += pts;
            if (pts != 0 && partToActivity.TryGetValue(m.ParticipantId, out var activityId))
            {
                activitiesPlayed[m.UserId].Add(activityId);
            }
        }

        return roster.Select(u => new EventStandingEntryDto
        {
            DisplayName = u.Name,
            TotalPoints = totals[u.UserId],
            ActivitiesPlayed = activitiesPlayed[u.UserId].Count,
        }).ToList();
    }

    private async Task<List<EventStandingEntryDto>> BuildFreeNameStandingsAsync(int eventId, CancellationToken ct)
    {
        var answerRows = await db.Answers.AsNoTracking()
            .Where(a => a.Participant!.Activity!.EventId == eventId)
            .Select(a => new { Name = a.Participant!.DisplayName, ActId = a.Participant!.ActivityId, a.AwardedPoints })
            .ToListAsync(ct);
        var scoreRows = await db.ScoreEntries.AsNoTracking()
            .Where(s => s.Activity!.EventId == eventId)
            .Select(s => new { Name = s.Participant!.DisplayName, ActId = s.ActivityId, s.Points })
            .ToListAsync(ct);
        var names = await db.Participants.AsNoTracking()
            .Where(p => p.Activity!.EventId == eventId)
            .Select(p => p.DisplayName)
            .Distinct()
            .ToListAsync(ct);

        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        var acts = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var n in names)
        {
            totals[n] = 0;
            acts[n] = new();
        }

        void Add(string name, int pts, int actId)
        {
            totals[name] = totals.GetValueOrDefault(name) + pts;
            if (!acts.TryGetValue(name, out var set))
            {
                acts[name] = set = new();
            }

            set.Add(actId);
        }

        foreach (var r in answerRows) Add(r.Name, r.AwardedPoints, r.ActId);
        foreach (var r in scoreRows) Add(r.Name, r.Points, r.ActId);

        return totals.Select(kv => new EventStandingEntryDto
        {
            DisplayName = kv.Key,
            TotalPoints = kv.Value,
            ActivitiesPlayed = acts[kv.Key].Count,
        }).ToList();
    }

    private static void Rank(List<EventStandingEntryDto> entries)
    {
        var ordered = entries
            .OrderByDescending(e => e.TotalPoints)
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rank = 0;
        int? prev = null;
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
