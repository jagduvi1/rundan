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

        await AdvanceAsync(activityId, ct); // resolves any first-round byes and assigns courts
        return true;
    }

    /// <summary>
    /// Records a match result. Pass <paramref name="sets"/> (one entry for free scoring, several for
    /// best-of sets) and the winner is derived from the activity's match format. The simulator instead
    /// passes a <paramref name="explicitWinnerId"/> with no sets.
    /// </summary>
    public async Task RecordResultAsync(
        int activityId, int matchId, IReadOnlyList<(int A, int B)>? sets, int? explicitWinnerId,
        CancellationToken ct = default)
    {
        var match = await db.BracketMatches.FirstOrDefaultAsync(m => m.Id == matchId && m.ActivityId == activityId, ct)
            ?? throw new RuleViolationException("Match not found.", StatusCodes.Status404NotFound);

        if (match.IsBye)
        {
            throw new RuleViolationException("That match is a walkover.");
        }

        // Re-recording can't safely re-seed already-built later rounds, so a decided match is final.
        if (match.WinnerParticipantId.HasValue)
        {
            throw new RuleViolationException("That match result is already recorded.", StatusCodes.Status409Conflict);
        }

        if (match.ParticipantAId is not { } aId || match.ParticipantBId is not { } bId)
        {
            throw new RuleViolationException("That match isn't ready to be played yet.");
        }

        int winnerId;
        string? scores = null;
        if (sets is { Count: > 0 })
        {
            var activity = await db.Activities.FirstAsync(a => a.Id == activityId, ct);
            winnerId = DeriveWinner(activity, sets, aId, bId);
            scores = string.Join(",", sets.Select(s => $"{s.A}-{s.B}"));
        }
        else if (explicitWinnerId is { } w)
        {
            winnerId = w;
        }
        else
        {
            throw new RuleViolationException("Enter the match result.");
        }

        if (winnerId != aId && winnerId != bId)
        {
            throw new RuleViolationException("The winner must be one of the two teams in the match.");
        }

        match.WinnerParticipantId = winnerId;
        match.SetScores = scores;
        await db.SaveChangesAsync(ct);

        await AdvanceAsync(activityId, ct);
        await RecomputeScoresAsync(activityId, ct);
    }

    // Decides the winner from the entered scores, per the activity's match format.
    private static int DeriveWinner(Activity activity, IReadOnlyList<(int A, int B)> sets, int aId, int bId)
    {
        if (activity.MatchFormat == MatchFormat.Sets)
        {
            var aSets = sets.Count(s => s.A > s.B);
            var bSets = sets.Count(s => s.B > s.A);
            var need = Math.Max(1, activity.BestOfSets) / 2 + 1;
            if (aSets < need && bSets < need)
            {
                throw new RuleViolationException(
                    $"No team has won enough sets yet — first to {need} set{(need == 1 ? "" : "s")} wins.");
            }

            return aSets >= need ? aId : bId;
        }

        // Free scoring: a single score, higher wins.
        var (sa, sb) = sets[0];
        if (sa == sb)
        {
            throw new RuleViolationException("A match can't end in a tie — one team must score more.");
        }

        return sa > sb ? aId : bId;
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

        var courtNames = await db.Courts.AsNoTracking()
            .Where(c => c.ActivityId == activityId)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        string? Name(int? id) => id.HasValue && names.TryGetValue(id.Value, out var n) ? n : null;
        string? Court(int? id) => id.HasValue && courtNames.TryGetValue(id.Value, out var n) ? n : null;

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
                CourtName = Court(m.CourtId),
                Score = FormatScore(m.SetScores),
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

    // "13-7,9-13,13-10" -> "13–7, 9–13, 13–10".
    private static string? FormatScore(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var sets = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(", ", sets.Select(s => s.Replace("-", "–")));
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
                Slot = slot,
                ParticipantAId = teamIds[i],
                ParticipantBId = bTeam,
                IsBye = bTeam is null,
                WinnerParticipantId = bTeam is null ? teamIds[i] : null, // a bye auto-advances
            });
            slot++;
        }
    }

    private async Task<List<int>> CourtIdsAsync(int activityId, CancellationToken ct) =>
        await db.Courts.Where(c => c.ActivityId == activityId).OrderBy(c => c.Order)
            .Select(c => c.Id).ToListAsync(ct);

    /// <summary>
    /// Spreads matches evenly across the courts following the order-of-play sequence, so matches that
    /// run in the same wave (e.g. the losers'-side match and the winners' final) land on different courts.
    /// Byes take no court. Deterministic, so a decided match keeps its court label.
    /// </summary>
    private async Task AssignCourtsAsync(int activityId, CancellationToken ct)
    {
        var courtIds = await CourtIdsAsync(activityId, ct);
        if (courtIds.Count == 0)
        {
            return;
        }

        var matches = await db.BracketMatches
            .Where(m => m.ActivityId == activityId)
            .OrderBy(m => m.Round).ThenBy(m => m.Side).ThenBy(m => m.Slot)
            .ToListAsync(ct);

        var played = 0;
        foreach (var m in matches)
        {
            if (m.IsBye)
            {
                m.CourtId = null;
                continue;
            }

            m.CourtId = courtIds[played % courtIds.Count];
            played++;
        }

        await db.SaveChangesAsync(ct);
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
                    // Need at least two losers for a real losers'-side match; a lone loser would
                    // just get a pointless walkover round.
                    if (losers.Count >= 2)
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

        await AssignCourtsAsync(activityId, ct);
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
