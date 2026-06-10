using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Services;

/// <summary>
/// Knockout tournament for Boule activities. The default is a straight single-elimination bracket
/// (winners' side + a losers' consolation side). An optional <em>group stage</em> plays a round-robin
/// in each group first, then seeds up to two playoff brackets — A (championship) and B (plate) — from
/// the group standings. Teams can be seeded manually (1→N) or drawn at random.
/// A team's activity score (fed into the scoreboard + event placement) is either points-per-win or a
/// final-placement value, per <see cref="TournamentScoring"/>.
/// </summary>
public sealed class BracketService(AppDbContext db, TimeProvider clock)
{
    /// <summary>Suggests a group count for the best distribution (aim for ~4 teams a group).</summary>
    public static int SuggestGroupCount(int teamCount)
    {
        if (teamCount < 4)
        {
            return 1;
        }

        var g = (int)Math.Round(teamCount / 4.0, MidpointRounding.AwayFromZero);
        return Math.Clamp(g, 1, teamCount / 2);
    }

    public async Task<bool> GenerateAsync(int activityId, CancellationToken ct = default)
    {
        if (await db.BracketMatches.AnyAsync(m => m.ActivityId == activityId, ct))
        {
            return false; // already drawn
        }

        var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == activityId, ct)
            ?? throw new RuleViolationException("Activity not found.", StatusCodes.Status404NotFound);

        var teamRows = await db.Participants.AsNoTracking()
            .Where(p => p.ActivityId == activityId && p.IsTeam)
            .Select(p => new { p.Id, p.Seed })
            .ToListAsync(ct);

        if (teamRows.Count < 2)
        {
            throw new RuleViolationException("Need at least two teams to draw the tournament.");
        }

        // Seeded order: manual seeds first (1 = top), then a random shuffle of the rest. A plain
        // random order when manual seeding is off.
        List<int> ordered = activity.UseManualSeeding
            ? teamRows.OrderBy(t => t.Seed ?? int.MaxValue).ThenBy(_ => Random.Shared.Next()).Select(t => t.Id).ToList()
            : teamRows.OrderBy(_ => Random.Shared.Next()).Select(t => t.Id).ToList();

        if (activity.UseGroupStage)
        {
            GenerateGroups(activity, ordered);
            await db.SaveChangesAsync(ct);
            await AssignCourtsAsync(activityId, ct);
        }
        else
        {
            CreateKnockoutRound1(activityId, pool: 0, ordered, seeded: activity.UseManualSeeding);
            await db.SaveChangesAsync(ct);
            await AdvanceAsync(activityId, ct); // resolves any first-round byes, seeds losers, assigns courts
        }

        return true;
    }

    // ---- Group stage -------------------------------------------------------

    private void GenerateGroups(Activity activity, List<int> seededTeams)
    {
        var n = seededTeams.Count;
        var g = activity.GroupCount > 0 ? activity.GroupCount : SuggestGroupCount(n);
        g = Math.Clamp(g, 1, n / 2);

        // Snake-distribute the seeded order so the strongest teams spread across the groups.
        var groups = new List<List<int>>();
        for (var i = 0; i < g; i++)
        {
            groups.Add(new List<int>());
        }

        var gi = 0;
        var forward = true;
        foreach (var team in seededTeams)
        {
            groups[gi].Add(team);
            if (forward)
            {
                if (gi == g - 1) forward = false; else gi++;
            }
            else
            {
                if (gi == 0) forward = true; else gi--;
            }
        }

        for (var index = 0; index < groups.Count; index++)
        {
            GenerateRoundRobin(activity.Id, index, groups[index]);
        }
    }

    // Circle method: every team plays every other once. Round = "matchday" (for court scheduling).
    private void GenerateRoundRobin(int activityId, int groupIndex, List<int> teams)
    {
        var rotation = new List<int?>(teams.Select(t => (int?)t));
        if (rotation.Count % 2 == 1)
        {
            rotation.Add(null); // odd team out rests each matchday
        }

        var m = rotation.Count;
        var rounds = m - 1;
        var half = m / 2;

        for (var r = 1; r <= rounds; r++)
        {
            var slot = 0;
            for (var i = 0; i < half; i++)
            {
                var a = rotation[i];
                var b = rotation[m - 1 - i];
                if (a is int ai && b is int bi)
                {
                    db.BracketMatches.Add(new BracketMatch
                    {
                        ActivityId = activityId,
                        GroupIndex = groupIndex,
                        Pool = 0,
                        Side = BracketSide.Winners,
                        Round = r,
                        Slot = slot++,
                        ParticipantAId = ai,
                        ParticipantBId = bi,
                        IsBye = false,
                    });
                }
            }

            // Rotate all but the first element clockwise.
            var last = rotation[m - 1];
            for (var i = m - 1; i > 1; i--)
            {
                rotation[i] = rotation[i - 1];
            }

            rotation[1] = last;
        }
    }

    /// <summary>Once every group match is decided, seeds Playoff A and (optionally) Playoff B from the standings.</summary>
    private async Task<bool> TrySeedPlayoffsAsync(Activity activity, CancellationToken ct)
    {
        var all = await db.BracketMatches.Where(m => m.ActivityId == activity.Id).ToListAsync(ct);

        // Already seeded once a knockout match exists.
        if (all.Any(m => m.GroupIndex == null))
        {
            return false;
        }

        var groupMatches = all.Where(m => m.GroupIndex != null).ToList();
        if (groupMatches.Count == 0 || !groupMatches.All(m => m.WinnerParticipantId.HasValue || m.IsBye))
        {
            return false;
        }

        var standings = ComputeStandings(activity, groupMatches);

        var aAdvance = Math.Max(1, activity.AdvanceToPlayoffA);
        var bAdvance = Math.Max(0, activity.AdvanceToPlayoffB);

        var aSeeds = SeedsForPool(standings, fromRank: 1, count: aAdvance);
        var bSeeds = SeedsForPool(standings, fromRank: aAdvance + 1, count: bAdvance);

        if (aSeeds.Count >= 1)
        {
            CreateKnockoutRound1(activity.Id, pool: 0, aSeeds, seeded: true);
        }

        if (bSeeds.Count >= 2)
        {
            CreateKnockoutRound1(activity.Id, pool: 1, bSeeds, seeded: true);
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    // Cross-group seed list: all teams finishing at rank `fromRank` (ordered best-first), then the next
    // rank, … for `count` ranks. Putting group winners at the top seeds spreads them across the bracket.
    private static List<int> SeedsForPool(List<GroupStandingDto> standings, int fromRank, int count)
    {
        var seeds = new List<int>();
        for (var k = fromRank; k < fromRank + count; k++)
        {
            var atRank = standings.SelectMany(g => g.Rows)
                .Where(r => r.Rank == k)
                .OrderByDescending(r => r.Won).ThenByDescending(r => r.Diff).ThenByDescending(r => r.PointsFor)
                .ThenBy(r => r.TeamId);
            seeds.AddRange(atRank.Select(r => r.TeamId));
        }

        return seeds;
    }

    // Computes each group's table and marks which playoff (if any) each position advances to.
    private static List<GroupStandingDto> ComputeStandings(Activity activity, List<BracketMatch> groupMatches)
    {
        var groups = new List<GroupStandingDto>();
        foreach (var grp in groupMatches.GroupBy(m => m.GroupIndex!.Value).OrderBy(g => g.Key))
        {
            var teamIds = grp.SelectMany(m => new[] { m.ParticipantAId, m.ParticipantBId })
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();

            var rows = teamIds.ToDictionary(id => id, id => new GroupRowDto { TeamId = id });

            foreach (var match in grp.Where(m => m.WinnerParticipantId.HasValue && !m.IsBye))
            {
                var a = match.ParticipantAId!.Value;
                var b = match.ParticipantBId!.Value;
                var (ga, gb) = SplitGames(match.SetScores);
                rows[a].Played++; rows[b].Played++;
                rows[a].PointsFor += ga; rows[a].PointsAgainst += gb;
                rows[b].PointsFor += gb; rows[b].PointsAgainst += ga;
                if (match.WinnerParticipantId == a) { rows[a].Won++; rows[b].Lost++; }
                else { rows[b].Won++; rows[a].Lost++; }
            }

            var ordered = rows.Values
                .OrderByDescending(r => r.Won).ThenByDescending(r => r.Diff).ThenByDescending(r => r.PointsFor)
                .ThenBy(r => r.TeamId).ToList();

            var aAdvance = Math.Max(1, activity.AdvanceToPlayoffA);
            var bAdvance = Math.Max(0, activity.AdvanceToPlayoffB);
            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].Rank = i + 1;
                ordered[i].AdvancesToPool = (i + 1) <= aAdvance ? 0
                    : (i + 1) <= aAdvance + bAdvance ? 1
                    : (int?)null;
            }

            groups.Add(new GroupStandingDto { GroupIndex = grp.Key, Rows = ordered });
        }

        return groups;
    }

    // Total games for each side across the sets (e.g. "13-7,9-13" → A=22, B=20). A win with no score
    // recorded (simulated) counts as a 1–0.
    private static (int A, int B) SplitGames(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (1, 0);
        }

        int a = 0, b = 0;
        foreach (var set in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = set.Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var sa) && int.TryParse(parts[1], out var sb))
            {
                a += sa; b += sb;
            }
        }

        return (a, b);
    }

    // ---- Result entry ------------------------------------------------------

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

        var activity = await db.Activities.FirstAsync(a => a.Id == activityId, ct);

        int winnerId;
        string? scores = null;
        if (sets is { Count: > 0 })
        {
            // Group matches use the group-stage format; knockout matches use the playoff format.
            var (format, bestOf) = match.GroupIndex != null
                ? (activity.GroupMatchFormat, activity.GroupBestOfSets)
                : (activity.MatchFormat, activity.BestOfSets);
            winnerId = DeriveWinner(format, bestOf, sets, aId, bId);
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

        // A finished group stage seeds the playoffs; then build out any knockout rounds.
        if (match.GroupIndex != null)
        {
            await TrySeedPlayoffsAsync(activity, ct);
        }

        await AdvanceAsync(activityId, ct);
        await RecomputeScoresAsync(activityId, ct);
    }

    // Decides the winner from the entered scores, per the given match format.
    private static int DeriveWinner(MatchFormat format, int bestOfSets, IReadOnlyList<(int A, int B)> sets, int aId, int bId)
    {
        if (format == MatchFormat.Sets)
        {
            var aSets = sets.Count(s => s.A > s.B);
            var bSets = sets.Count(s => s.B > s.A);
            var need = Math.Max(1, bestOfSets) / 2 + 1;
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

    /// <summary>If the tournament has crowned its champion(s), finish the activity automatically.</summary>
    public async Task<bool> TryAutoFinishAsync(int activityId, CancellationToken ct = default)
    {
        var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == activityId, ct);
        if (activity is not { Type: ActivityType.Boule, Status: ActivityStatus.Live })
        {
            return false;
        }

        var bracket = await BuildAsync(activityId, ct);
        if (bracket?.Complete != true)
        {
            return false;
        }

        activity.Status = ActivityStatus.Finished;
        activity.FinishedUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ResetAsync(int activityId, CancellationToken ct = default)
    {
        db.BracketMatches.RemoveRange(db.BracketMatches.Where(m => m.ActivityId == activityId));
        db.ScoreEntries.RemoveRange(db.ScoreEntries.Where(s => s.ActivityId == activityId));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sets the manual seed order (team ids ranked 1→N). Teams left out are unseeded.</summary>
    public async Task SetSeedsAsync(int activityId, IReadOnlyList<int> orderedTeamIds, CancellationToken ct = default)
    {
        var teams = await db.Participants.Where(p => p.ActivityId == activityId && p.IsTeam).ToListAsync(ct);
        var rank = new Dictionary<int, int>();
        for (var i = 0; i < orderedTeamIds.Count; i++)
        {
            rank[orderedTeamIds[i]] = i + 1;
        }

        foreach (var team in teams)
        {
            team.Seed = rank.TryGetValue(team.Id, out var s) ? s : null;
        }

        await db.SaveChangesAsync(ct);
    }

    // ---- Build the view ----------------------------------------------------

    public async Task<BracketDto?> BuildAsync(int activityId, CancellationToken ct = default)
    {
        var activity = await db.Activities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == activityId, ct);
        if (activity is null)
        {
            return null;
        }

        var matches = await db.BracketMatches.AsNoTracking()
            .Where(m => m.ActivityId == activityId)
            .OrderBy(m => m.Pool).ThenBy(m => m.Side).ThenBy(m => m.Round).ThenBy(m => m.Slot)
            .ToListAsync(ct);

        var names = await db.Participants.AsNoTracking()
            .Where(p => p.ActivityId == activityId)
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);

        var courtNames = await db.Courts.AsNoTracking()
            .Where(c => c.ActivityId == activityId)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        string? Name(int? id) => id.HasValue && names.TryGetValue(id.Value, out var n) ? n : null;
        string? Court(int? id) => id.HasValue && courtNames.TryGetValue(id.Value, out var n) ? n : null;

        var groupMatches = matches.Where(m => m.GroupIndex != null).ToList();
        var knockout = matches.Where(m => m.GroupIndex == null).ToList();

        var dto = new BracketDto
        {
            ActivityId = activityId,
            Drawn = matches.Count > 0,
            HasGroupStage = activity.UseGroupStage && groupMatches.Count > 0,
            GroupStageComplete = groupMatches.Count > 0
                && groupMatches.All(m => m.WinnerParticipantId.HasValue || m.IsBye)
                && knockout.Count > 0,
            Matches = matches.Select(m => new BracketMatchDto
            {
                Id = m.Id,
                Pool = m.Pool,
                GroupIndex = m.GroupIndex,
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

        if (groupMatches.Count > 0)
        {
            dto.Groups = ComputeStandings(activity, groupMatches);
            foreach (var g in dto.Groups)
            {
                foreach (var row in g.Rows)
                {
                    row.Name = Name(row.TeamId) ?? "—";
                }
            }
        }

        // Champion of a pool = the winner of the single match in its top winners' round.
        string? PoolChampion(int pool)
        {
            var w = knockout.Where(m => m.Pool == pool && m.Side == BracketSide.Winners).ToList();
            if (w.Count == 0)
            {
                return null;
            }

            var top = w.Max(m => m.Round);
            var final = w.Where(m => m.Round == top).ToList();
            return final.Count == 1 && final[0].WinnerParticipantId.HasValue ? Name(final[0].WinnerParticipantId) : null;
        }

        dto.ChampionName = PoolChampion(0);
        dto.PlayoffBChampionName = PoolChampion(1);

        // Complete once the playoffs exist, A has a champion, and every knockout match is decided.
        var seededOk = !dto.HasGroupStage || dto.GroupStageComplete;
        dto.Complete = seededOk && knockout.Count > 0 && dto.ChampionName is not null
            && knockout.All(m => m.WinnerParticipantId.HasValue || m.IsBye);

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

    // ---- Bracket building --------------------------------------------------

    // Sequential pairing (legacy random draw and follow-on rounds): (0,1),(2,3),… trailing bye.
    private void CreateRound(int activityId, int pool, BracketSide side, int round, List<int> teamIds)
    {
        var slot = 0;
        for (var i = 0; i < teamIds.Count; i += 2)
        {
            int? bTeam = i + 1 < teamIds.Count ? teamIds[i + 1] : null;
            db.BracketMatches.Add(new BracketMatch
            {
                ActivityId = activityId,
                Pool = pool,
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

    // Seeded round 1: standard bracket positions so 1 meets the lowest seed and top seeds get the byes.
    private void CreateKnockoutRound1(int activityId, int pool, List<int> orderedTeams, bool seeded)
    {
        if (!seeded || orderedTeams.Count < 2)
        {
            CreateRound(activityId, pool, BracketSide.Winners, 1, orderedTeams);
            return;
        }

        var slots = SeededTeamSlots(orderedTeams); // length is the next power of two; nulls are byes
        var slot = 0;
        for (var i = 0; i < slots.Count; i += 2)
        {
            var a = slots[i];
            var b = slots[i + 1];
            if (a is null && b is not null)
            {
                (a, b) = (b, a); // keep the present team as A so a bye reads "team vs (walkover)"
            }

            db.BracketMatches.Add(new BracketMatch
            {
                ActivityId = activityId,
                Pool = pool,
                Side = BracketSide.Winners,
                Round = 1,
                Slot = slot++,
                ParticipantAId = a,
                ParticipantBId = b,
                IsBye = b is null,
                WinnerParticipantId = b is null ? a : null,
            });
        }
    }

    // Maps seeded teams onto standard bracket slot order (seed 1 top, seed 2 bottom, …); null where the
    // seed number exceeds the team count (a bye).
    private static List<int?> SeededTeamSlots(List<int> orderedTeams)
    {
        var n = orderedTeams.Count;
        var size = 1;
        while (size < n)
        {
            size <<= 1;
        }

        var seeds = new List<int> { 1, 2 };
        while (seeds.Count < size)
        {
            var next = new List<int>(seeds.Count * 2);
            var sum = seeds.Count * 2 + 1;
            foreach (var s in seeds)
            {
                next.Add(s);
                next.Add(sum - s);
            }

            seeds = next;
        }

        return seeds.Select(s => s <= n ? (int?)orderedTeams[s - 1] : null).ToList();
    }

    private async Task<List<int>> CourtIdsAsync(int activityId, CancellationToken ct) =>
        await db.Courts.Where(c => c.ActivityId == activityId).OrderBy(c => c.Order)
            .Select(c => c.Id).ToListAsync(ct);

    /// <summary>
    /// Spreads matches evenly across the courts following the order-of-play sequence, so matches that
    /// run in the same wave land on different courts. Byes take no court. Deterministic, so a decided
    /// match keeps its court label.
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
            .OrderBy(m => m.GroupIndex.HasValue ? m.GroupIndex.Value : 1000)
            .ThenBy(m => m.Round).ThenBy(m => m.Pool).ThenBy(m => m.Side).ThenBy(m => m.Slot)
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

    /// <summary>Builds follow-on knockout rounds as results come in, per pool (seeding each pool's
    /// losers' side once, if that pool's consolation is enabled).</summary>
    private async Task AdvanceAsync(int activityId, CancellationToken ct)
    {
        var activity = await db.Activities.AsNoTracking().FirstAsync(a => a.Id == activityId, ct);

        while (true)
        {
            var knockout = await db.BracketMatches
                .Where(m => m.ActivityId == activityId && m.GroupIndex == null)
                .ToListAsync(ct);
            if (knockout.Count == 0)
            {
                break; // group stage only, so far
            }

            var acted = false;
            foreach (var pool in knockout.Select(m => m.Pool).Distinct().OrderBy(p => p))
            {
                if (await TryAdvancePoolAsync(activity, pool, knockout.Where(m => m.Pool == pool).ToList(), ct))
                {
                    acted = true;
                    break;
                }
            }

            if (!acted)
            {
                break;
            }
        }

        await AssignCourtsAsync(activityId, ct);
    }

    // One advancement step for a pool: seed its losers' side, or build the next round of a side.
    private async Task<bool> TryAdvancePoolAsync(Activity activity, int pool, List<BracketMatch> poolMatches, CancellationToken ct)
    {
        var consolation = pool == 0 ? activity.PlayoffAConsolation : activity.PlayoffBConsolation;

        // Seed the losers' side once from the winners' round-1 losers.
        if (consolation && !poolMatches.Any(m => m.Side == BracketSide.Losers))
        {
            var w1 = poolMatches.Where(m => m.Side == BracketSide.Winners && m.Round == 1).ToList();
            if (w1.Count > 0 && w1.All(m => m.WinnerParticipantId.HasValue))
            {
                var losers = w1.Where(m => !m.IsBye).OrderBy(m => m.Slot)
                    .Select(m => m.WinnerParticipantId == m.ParticipantAId ? m.ParticipantBId!.Value : m.ParticipantAId!.Value)
                    .ToList();
                if (losers.Count >= 2)
                {
                    CreateRound(activity.Id, pool, BracketSide.Losers, 1, losers);
                    await db.SaveChangesAsync(ct);
                    return true;
                }
            }
        }

        // Advance a side whose latest complete round produced more than one survivor.
        foreach (var side in new[] { BracketSide.Winners, BracketSide.Losers })
        {
            var sideMatches = poolMatches.Where(m => m.Side == side).ToList();
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
                    CreateRound(activity.Id, pool, side, r + 1, survivors);
                    await db.SaveChangesAsync(ct);
                    return true;
                }
            }
        }

        return false;
    }

    // ---- Scoring -----------------------------------------------------------

    private async Task RecomputeScoresAsync(int activityId, CancellationToken ct)
    {
        var activity = await db.Activities.AsNoTracking().FirstAsync(a => a.Id == activityId, ct);
        var matches = await db.BracketMatches.AsNoTracking()
            .Where(m => m.ActivityId == activityId && !m.IsBye && m.WinnerParticipantId != null)
            .ToListAsync(ct);

        var points = activity.TournamentScoring == TournamentScoring.Placement
            ? PlacementPoints(activity, matches)
            : PerWinPoints(matches);

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

    // Points per win: group win = 1; Playoff A win = 3 (consolation 1); Playoff B win = 2 (consolation 1).
    private static Dictionary<int, int> PerWinPoints(List<BracketMatch> matches)
    {
        var points = new Dictionary<int, int>();
        foreach (var m in matches)
        {
            var w = m.WinnerParticipantId!.Value;
            var award = m.GroupIndex != null ? 1
                : m.Pool == 0 ? (m.Side == BracketSide.Winners ? 3 : 1)
                : (m.Side == BracketSide.Winners ? 2 : 1);
            points[w] = points.GetValueOrDefault(w) + award;
        }

        return points;
    }

    // Final placement: rank every team by how far it got, then award position points (1st = team count).
    private static Dictionary<int, int> PlacementPoints(Activity activity, List<BracketMatch> matches)
    {
        var teamIds = matches.SelectMany(m => new[] { m.ParticipantAId, m.ParticipantBId })
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (teamIds.Count == 0)
        {
            return new();
        }

        // An achievement score that sorts champions first: pool tier ≫ winners' depth ≫ wins ≫ group form.
        int Achievement(int team)
        {
            var knockout = matches.Where(m => m.GroupIndex == null);
            var inA = knockout.Any(m => m.Pool == 0 && (m.ParticipantAId == team || m.ParticipantBId == team));
            var inB = knockout.Any(m => m.Pool == 1 && (m.ParticipantAId == team || m.ParticipantBId == team));
            var tier = inA ? 1_000_000 : inB ? 100_000 : 0;

            var winnersReached = knockout
                .Where(m => m.Side == BracketSide.Winners && (m.ParticipantAId == team || m.ParticipantBId == team))
                .Select(m => m.Round).DefaultIfEmpty(0).Max();
            var winnersWins = knockout.Count(m => m.Side == BracketSide.Winners && m.WinnerParticipantId == team);
            var losersWins = knockout.Count(m => m.Side == BracketSide.Losers && m.WinnerParticipantId == team);
            var groupWins = matches.Count(m => m.GroupIndex != null && m.WinnerParticipantId == team);

            return tier + winnersReached * 1000 + winnersWins * 100 + losersWins * 10 + groupWins;
        }

        var ranked = teamIds
            .Select(id => (Id: id, Score: Achievement(id)))
            .OrderByDescending(t => t.Score).ThenBy(t => t.Id)
            .ToList();

        var points = new Dictionary<int, int>();
        var n = ranked.Count;
        for (var i = 0; i < n; i++)
        {
            points[ranked[i].Id] = n - i; // 1st = n, last = 1
        }

        return points;
    }
}
