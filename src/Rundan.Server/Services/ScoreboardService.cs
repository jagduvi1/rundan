using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Services;

/// <summary>Builds the live scoreboard for an activity from the database.</summary>
public sealed class ScoreboardService(AppDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Computes the current scoreboard. Returns <c>null</c> if the activity does not exist.
    /// Data volumes are tiny (a handful of friends), so aggregation is done in memory.
    /// </summary>
    public async Task<ScoreboardDto?> BuildAsync(int activityId, CancellationToken ct = default)
    {
        var activity = await db.Activities
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == activityId, ct);

        if (activity is null)
        {
            return null;
        }

        var participants = await db.Participants
            .AsNoTracking()
            .Where(p => p.ActivityId == activityId)
            .Select(p => new { p.Id, p.DisplayName })
            .ToListAsync(ct);

        // points[participantId] = (total, entryCount)
        var totals = participants.ToDictionary(p => p.Id, _ => (Points: 0, Entries: 0));

        var isQuestionGame = activity.Type is ActivityType.Quiz or ActivityType.Tipspromenad;
        var totalQuestions = 0;

        if (isQuestionGame)
        {
            totalQuestions = await db.Questions.CountAsync(q => q.ActivityId == activityId, ct);

            var perPlayer = await db.Answers
                .AsNoTracking()
                .Where(a => a.Participant!.ActivityId == activityId)
                .GroupBy(a => a.ParticipantId)
                .Select(g => new { ParticipantId = g.Key, Points = g.Sum(a => a.AwardedPoints), Count = g.Count() })
                .ToListAsync(ct);

            foreach (var row in perPlayer)
            {
                if (totals.ContainsKey(row.ParticipantId))
                {
                    totals[row.ParticipantId] = (row.Points, row.Count);
                }
            }
        }
        else
        {
            var perPlayer = await db.ScoreEntries
                .AsNoTracking()
                .Where(s => s.ActivityId == activityId)
                .GroupBy(s => s.ParticipantId)
                .Select(g => new { ParticipantId = g.Key, Points = g.Sum(s => s.Points), Count = g.Count() })
                .ToListAsync(ct);

            foreach (var row in perPlayer)
            {
                if (totals.ContainsKey(row.ParticipantId))
                {
                    totals[row.ParticipantId] = (row.Points, row.Count);
                }
            }
        }

        var ordered = participants
            .Select(p => new ScoreboardEntryDto
            {
                ParticipantId = p.Id,
                DisplayName = p.DisplayName,
                TotalPoints = totals[p.Id].Points,
                Entries = totals[p.Id].Entries,
            })
            // In a LowerWins (golf-like) game, players who haven't scored yet must not
            // outrank real low scores with their seeded 0 — push them to the bottom.
            .OrderBy(e => activity.ScoringMode == ScoringMode.LowerWins && e.Entries == 0)
            .ThenBy(e => activity.ScoringMode == ScoringMode.LowerWins ? e.TotalPoints : -e.TotalPoints)
            .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Standard competition ranking: tied totals share a rank; the next distinct total
        // takes its position-based rank (e.g. 1, 1, 3).
        var rank = 0;
        int? previousPoints = null;
        var seen = 0;
        foreach (var entry in ordered)
        {
            seen++;
            if (previousPoints is null || entry.TotalPoints != previousPoints)
            {
                rank = seen;
                previousPoints = entry.TotalPoints;
            }

            entry.Rank = rank;
        }

        return new ScoreboardDto
        {
            ActivityId = activity.Id,
            Type = activity.Type,
            Status = activity.Status,
            ScoringMode = activity.ScoringMode,
            TotalQuestions = totalQuestions,
            Entries = ordered,
            UpdatedUtc = clock.GetUtcNow(),
        };
    }
}
