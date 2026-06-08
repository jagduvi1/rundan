using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Services;

/// <summary>
/// Knockout bracket for Boule activities. Round 1 is a random draw; winners climb the
/// winners' side to a final, while the round-1 losers play out a losers' side. A team's
/// activity score = 3 per winners'-side win + 1 per losers'-side win, so the bracket
/// standing flows straight into the existing scoreboard and event placement.
/// </summary>
public sealed class BracketService(AppDbContext db, TimeProvider clock)
{
    public async Task<bool> GenerateAsync(int activityId, CancellationToken ct = default)
    {
        if (await db.BracketMatches.AnyAsync(m => m.ActivityId == activityId, ct))
        {
            return false; // already drawn
        }

        var teams = await db.Participants.AsNoTracking()
            .Where(p => p.ActivityId == activityId && p.IsTeam)
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (teams.Count < 2)
        {
            throw new RuleViolationException("Need at least two teams to draw a bracket.");
        }

        var drawn = teams.OrderBy(_ => Random.Shared.Next()).ToList();
        CreateRound(activityId, BracketSide.Winners, 1, drawn);
        await db.SaveChangesAsync(ct);

        await AdvanceAsync(activityId, ct); // resolves any first-round byes
        return true;
    }

    public async Task RecordResultAsync(int activityId, int matchId, int winnerId, CancellationToken ct = default)
    {
        var match = await db.BracketMatches.FirstOrDefaultAsync(m => m.Id == matchId && m.ActivityId == activityId, ct)
            ?? throw new RuleViolationException("Match not found.", StatusCodes.Status404NotFound);

        if (match.IsBye)
        {
            throw new RuleViolationException("That match is a walkover.");
        }

        if (winnerId != match.ParticipantAId && winnerId != match.ParticipantBId)
        {
            throw new RuleViolationException("The winner must be one of the two teams in the match.");
        }

        match.WinnerParticipantId = winnerId;
        await db.SaveChangesAsync(ct);

        await AdvanceAsync(activityId, ct);
        await RecomputeScoresAsync(activityId, ct);
    }

    public async Task ResetAsync(int activityId, CancellationToken ct = default)
    {
        db.BracketMatches.RemoveRange(db.BracketMatches.Where(m => m.ActivityId == activityId));
        db.ScoreEntries.RemoveRange(db.ScoreEntries.Where(s => s.ActivityId == activityId));
        await db.SaveChangesAsync(ct);
    }

    public async Task<BracketDto?> BuildAsync(int activityId, CancellationToken ct = default)
    {
        if (!await db.Activities.AnyAsync(a => a.Id == activityId, ct))
        {
            return null;
        }

        var matches = await db.BracketMatches.AsNoTracking()
            .Where(m => m.ActivityId == activityId)
            .OrderBy(m => m.Side).ThenBy(m => m.Round).ThenBy(m => m.Slot)
            .ToListAsync(ct);

        var names = await db.Participants.AsNoTracking()
            .Where(p => p.ActivityId == activityId)
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);

        string? Name(int? id) => id.HasValue && names.TryGetValue(id.Value, out var n) ? n : null;

        var dto = new BracketDto
        {
            ActivityId = activityId,
            Drawn = matches.Count > 0,
            Matches = matches.Select(m => new BracketMatchDto
            {
                Id = m.Id,
                Side = m.Side,
                Round = m.Round,
                Slot = m.Slot,
                AId = m.ParticipantAId,
                AName = Name(m.ParticipantAId),
                BId = m.ParticipantBId,
                BName = Name(m.ParticipantBId),
                WinnerParticipantId = m.WinnerParticipantId,
                IsBye = m.IsBye,
            }).ToList(),
        };

        // The champion is the winner of the single match in the top winners' round.
        var winners = matches.Where(m => m.Side == BracketSide.Winners).ToList();
        if (winners.Count > 0)
        {
            var topRound = winners.Max(m => m.Round);
            var final = winners.Where(m => m.Round == topRound).ToList();
            if (final.Count == 1 && final[0].WinnerParticipantId.HasValue)
            {
                dto.Complete = true;
                dto.ChampionName = Name(final[0].WinnerParticipantId);
            }
        }

        return dto;
    }

    private void CreateRound(int activityId, BracketSide side, int round, List<int> teamIds)
    {
        var slot = 0;
        for (var i = 0; i < teamIds.Count; i += 2)
        {
            int? bTeam = i + 1 < teamIds.Count ? teamIds[i + 1] : null;
            db.BracketMatches.Add(new BracketMatch
            {
                ActivityId = activityId,
                Side = side,
                Round = round,
                Slot = slot++,
                ParticipantAId = teamIds[i],
                ParticipantBId = bTeam,
                IsBye = bTeam is null,
                WinnerParticipantId = bTeam is null ? teamIds[i] : null, // a bye auto-advances
            });
        }
    }

    /// <summary>Builds follow-on rounds as results come in (and seeds the losers' side once).</summary>
    private async Task AdvanceAsync(int activityId, CancellationToken ct)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            var matches = await db.BracketMatches.Where(m => m.ActivityId == activityId).ToListAsync(ct);

            // Seed losers' round 1 from the winners' round-1 losers (only once).
            if (!matches.Any(m => m.Side == BracketSide.Losers))
            {
                var w1 = matches.Where(m => m.Side == BracketSide.Winners && m.Round == 1).ToList();
                if (w1.Count > 0 && w1.All(m => m.WinnerParticipantId.HasValue))
                {
                    var losers = w1.Where(m => !m.IsBye).OrderBy(m => m.Slot)
                        .Select(m => m.WinnerParticipantId == m.ParticipantAId ? m.ParticipantBId!.Value : m.ParticipantAId!.Value)
                        .ToList();
                    if (losers.Count >= 1)
                    {
                        CreateRound(activityId, BracketSide.Losers, 1, losers);
                        await db.SaveChangesAsync(ct);
                        changed = true;
                        continue;
                    }
                }
            }

            // Advance a side whose latest complete round produced more than one survivor.
            foreach (var side in new[] { BracketSide.Winners, BracketSide.Losers })
            {
                var sideMatches = matches.Where(m => m.Side == side).ToList();
                if (sideMatches.Count == 0)
                {
                    continue;
                }

                var maxRound = sideMatches.Max(m => m.Round);
                for (var r = 1; r <= maxRound; r++)
                {
                    var round = sideMatches.Where(m => m.Round == r).OrderBy(m => m.Slot).ToList();
                    if (round.Count == 0 || !round.All(m => m.WinnerParticipantId.HasValue))
                    {
                        continue;
                    }

                    var survivors = round.Select(m => m.WinnerParticipantId!.Value).ToList();
                    if (survivors.Count >= 2 && !sideMatches.Any(m => m.Round == r + 1))
                    {
                        CreateRound(activityId, side, r + 1, survivors);
                        await db.SaveChangesAsync(ct);
                        changed = true;
                        break;
                    }
                }

                if (changed)
                {
                    break;
                }
            }
        }
    }

    private async Task RecomputeScoresAsync(int activityId, CancellationToken ct)
    {
        var matches = await db.BracketMatches.AsNoTracking()
            .Where(m => m.ActivityId == activityId && !m.IsBye && m.WinnerParticipantId != null)
            .ToListAsync(ct);

        var points = new Dictionary<int, int>();
        foreach (var m in matches)
        {
            var w = m.WinnerParticipantId!.Value;
            points[w] = points.GetValueOrDefault(w) + (m.Side == BracketSide.Winners ? 3 : 1);
        }

        // The bracket fully owns this activity's score lines — rebuild them from the results.
        db.ScoreEntries.RemoveRange(db.ScoreEntries.Where(s => s.ActivityId == activityId));
        foreach (var (participantId, pts) in points.Where(p => p.Value > 0))
        {
            db.ScoreEntries.Add(new ScoreEntry
            {
                ActivityId = activityId,
                ParticipantId = participantId,
                Round = 1,
                Points = pts,
                RecordedUtc = clock.GetUtcNow(),
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
