using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;

namespace Rundan.Server.Services;

/// <summary>
/// Generates the per-activity teams (the partner mixer). Teams are reshuffled for each
/// activity so players get fresh teammates; the team's score credits each member's
/// individual total (handled by <see cref="EventStandingsService"/>).
/// </summary>
public sealed class TeamService(AppDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Ensures team participants exist for an event-activity. Idempotent: returns the
    /// existing teams if already generated, otherwise generates and saves them.
    /// Returns an empty list for standalone activities or events with no roster.
    /// </summary>
    public async Task<List<Participant>> EnsureTeamsAsync(Activity activity, CancellationToken ct = default)
    {
        if (activity.EventId is not { } eventId)
        {
            return new();
        }

        var existing = await db.Participants
            .Include(p => p.Members).ThenInclude(m => m.User)
            .Where(p => p.ActivityId == activity.Id && p.IsTeam)
            .ToListAsync(ct);
        if (existing.Count > 0)
        {
            return existing;
        }

        var ev = await db.Events.FirstAsync(e => e.Id == eventId, ct);
        var members = await db.EventMembers
            .Where(m => m.EventId == eventId)
            .Select(m => m.User!)
            .OrderBy(u => u.Name)
            .ToListAsync(ct);
        if (members.Count == 0)
        {
            return new();
        }

        var teams = PartnerMixer.MakeTeams(members, Math.Max(1, ev.TeamSize), activity.Order);

        var created = new List<Participant>();
        foreach (var group in teams)
        {
            var participant = new Participant
            {
                ActivityId = activity.Id,
                DisplayName = string.Join(" & ", group.Select(u => u.Name)),
                IsTeam = true,
                Token = Guid.NewGuid(),
                JoinedUtc = clock.GetUtcNow(),
                Members = group.Select(u => new ParticipantMember { UserId = u.Id }).ToList(),
            };
            db.Participants.Add(participant);
            created.Add(participant);
        }

        await db.SaveChangesAsync(ct);

        // attach User refs for the returned objects
        foreach (var team in created)
        {
            foreach (var m in team.Members)
            {
                m.User = members.First(u => u.Id == m.UserId);
            }
        }

        return created;
    }
}

/// <summary>Pure team-pairing logic (the partner mixer).</summary>
internal static class PartnerMixer
{
    public static List<List<User>> MakeTeams(IReadOnlyList<User> members, int teamSize, int activityOrder)
    {
        if (teamSize <= 1)
        {
            return members.Select(u => new List<User> { u }).ToList();
        }

        if (teamSize == 2)
        {
            return Pairs(members, activityOrder);
        }

        // General case: rotate the roster per activity, then chunk into teams.
        var round = Math.Max(0, activityOrder - 1);
        var rotated = Rotate(members.ToList(), (round * teamSize) % members.Count);
        return Chunk(rotated, teamSize);
    }

    // Circle method: distinct pairings each round; an odd roster leaves one solo team.
    private static List<List<User>> Pairs(IReadOnlyList<User> members, int activityOrder)
    {
        var players = members.Select(u => (User?)u).ToList();
        if (players.Count % 2 == 1)
        {
            players.Add(null); // bye
        }

        var n = players.Count;
        var rounds = n - 1;
        var r = rounds == 0 ? 0 : (((activityOrder - 1) % rounds) + rounds) % rounds;

        var arranged = new List<User?> { players[0] };
        arranged.AddRange(Rotate(players.Skip(1).ToList(), r));

        var teams = new List<List<User>>();
        for (var i = 0; i < n / 2; i++)
        {
            var team = new List<User>();
            if (arranged[i] is { } a)
            {
                team.Add(a);
            }

            if (arranged[n - 1 - i] is { } b)
            {
                team.Add(b);
            }

            if (team.Count > 0)
            {
                teams.Add(team);
            }
        }

        return teams;
    }

    private static List<T> Rotate<T>(List<T> src, int k)
    {
        var n = src.Count;
        if (n == 0)
        {
            return src;
        }

        k = ((k % n) + n) % n;
        return src.Skip(n - k).Concat(src.Take(n - k)).ToList();
    }

    private static List<List<User>> Chunk(List<User> src, int size)
    {
        var result = new List<List<User>>();
        for (var i = 0; i < src.Count; i += size)
        {
            result.Add(src.Skip(i).Take(size).ToList());
        }

        return result;
    }
}
