using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Services;

/// <summary>
/// The "slap" twist. After an activity finishes (points already counted), the winning team
/// slaps a rival, halving that rival's lead over the player just below them in the standings.
/// The lost points vanish or are handed to another player. One slap per activity; it must be
/// resolved (or host-skipped) before the next activity starts.
/// </summary>
public sealed class SlapService(AppDbContext db, ScoreboardService scoreboard, EventStandingsService standings, TimeProvider clock)
{
    /// <summary>Resolves Random to Vanish/SendToPlayer deterministically per activity (stable across restarts).</summary>
    public static SlapMode EffectiveMode(SlapMode mode, int activityId)
    {
        if (mode != SlapMode.Random)
        {
            return mode;
        }

        var hash = unchecked((uint)activityId * 2654435761u);
        return ((hash >> 13) & 1u) == 0 ? SlapMode.Vanish : SlapMode.SendToPlayer;
    }

    /// <summary>The first finished activity whose slap hasn't been resolved yet, if any.</summary>
    public async Task<PendingSlapDto?> PendingAsync(int eventId, CancellationToken ct = default)
    {
        var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null || ev.SlapMode == SlapMode.Off)
        {
            return null;
        }

        var finished = await db.Activities.AsNoTracking()
            .Where(a => a.EventId == eventId && a.Status == ActivityStatus.Finished)
            .OrderBy(a => a.Order)
            .Select(a => new { a.Id, a.Title })
            .ToListAsync(ct);

        var resolved = await db.Slaps.AsNoTracking()
            .Where(s => s.EventId == eventId)
            .Select(s => s.ActivityId)
            .ToListAsync(ct);

        foreach (var a in finished)
        {
            if (resolved.Contains(a.Id))
            {
                continue;
            }

            var winner = await WinnerAsync(a.Id, ct);
            if (winner is null)
            {
                continue; // no players / not a team activity → nothing to slap
            }

            var members = await db.EventMembers.AsNoTracking()
                .Where(m => m.EventId == eventId)
                .Select(m => new SlapPersonDto { UserId = m.UserId, Name = m.User!.Name })
                .ToListAsync(ct);

            return new PendingSlapDto
            {
                ActivityId = a.Id,
                ActivityTitle = a.Title,
                WinnerName = winner.Value.Name,
                WinnerUserIds = winner.Value.MemberIds,
                EffectiveMode = EffectiveMode(ev.SlapMode, a.Id),
                Members = members.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            };
        }

        return null;
    }

    public async Task PerformAsync(int eventId, int activityId, int slapperUserId, int slappedUserId, int? recipientUserId, CancellationToken ct = default)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId, ct)
            ?? throw new RuleViolationException("Event not found.", StatusCodes.Status404NotFound);
        if (ev.SlapMode == SlapMode.Off)
        {
            throw new RuleViolationException("Slaps are off for this event.");
        }

        var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == activityId && a.EventId == eventId, ct)
            ?? throw new RuleViolationException("Activity not found.", StatusCodes.Status404NotFound);
        if (activity.Status != ActivityStatus.Finished)
        {
            throw new RuleViolationException("That activity hasn't finished yet.");
        }

        if (await db.Slaps.AnyAsync(s => s.ActivityId == activityId, ct))
        {
            throw new RuleViolationException("Someone already took this slap.", StatusCodes.Status409Conflict);
        }

        var winner = await WinnerAsync(activityId, ct)
            ?? throw new RuleViolationException("This activity has no winner to slap with.");
        if (!winner.MemberIds.Contains(slapperUserId))
        {
            throw new RuleViolationException("Only the winning team can slap.", StatusCodes.Status403Forbidden);
        }

        var memberIds = await db.EventMembers.Where(m => m.EventId == eventId).Select(m => m.UserId).ToListAsync(ct);
        if (!memberIds.Contains(slappedUserId))
        {
            throw new RuleViolationException("Pick a player in this event.");
        }

        if (winner.MemberIds.Contains(slappedUserId))
        {
            throw new RuleViolationException("You can't slap your own team.");
        }

        var mode = EffectiveMode(ev.SlapMode, activityId);
        int? recipient = null;
        if (mode == SlapMode.SendToPlayer)
        {
            if (recipientUserId is not { } rid)
            {
                throw new RuleViolationException("Pick who gets the points.");
            }

            if (rid == slapperUserId)
            {
                throw new RuleViolationException("You can't send the points to yourself.");
            }

            if (rid == slappedUserId)
            {
                throw new RuleViolationException("Send the points to someone other than the slapped player.");
            }

            if (!memberIds.Contains(rid))
            {
                throw new RuleViolationException("Pick a player in this event.");
            }

            recipient = rid;
        }

        var penalty = await PenaltyForAsync(eventId, slappedUserId, ct);

        db.Slaps.Add(new Slap
        {
            EventId = eventId,
            ActivityId = activityId,
            SlapperUserId = slapperUserId,
            SlappedUserId = slappedUserId,
            RecipientUserId = recipient,
            Penalty = penalty,
            Skipped = false,
            CreatedUtc = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task SkipAsync(int eventId, int activityId, CancellationToken ct = default)
    {
        if (await db.Slaps.AnyAsync(s => s.ActivityId == activityId, ct))
        {
            return; // already resolved
        }

        db.Slaps.Add(new Slap
        {
            EventId = eventId,
            ActivityId = activityId,
            Skipped = true,
            CreatedUtc = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Half the slapped player's lead over the next-lower player in the current standings.</summary>
    private async Task<double> PenaltyForAsync(int eventId, int slappedUserId, CancellationToken ct)
    {
        var name = (await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == slappedUserId, ct))?.Name;
        var board = await standings.BuildAsync(eventId, ct);
        if (name is null || board is null)
        {
            return 0;
        }

        var entries = board.Entries;
        var idx = entries.FindIndex(e => e.DisplayName == name);
        if (idx < 0)
        {
            return 0;
        }

        var total = entries[idx].TotalPoints;
        var following = entries.Skip(idx + 1).FirstOrDefault(e => e.TotalPoints < total);
        return following is null ? 0 : Math.Max(0d, (total - following.TotalPoints) / 2d);
    }

    /// <summary>The winning team (rank 1) of an activity and its roster member ids, or null.</summary>
    private async Task<(string Name, List<int> MemberIds)?> WinnerAsync(int activityId, CancellationToken ct)
    {
        var board = await scoreboard.BuildAsync(activityId, ct);
        var top = board?.Entries.FirstOrDefault(e => e.Rank == 1);
        if (top is null)
        {
            return null;
        }

        var memberIds = await db.ParticipantMembers.AsNoTracking()
            .Where(pm => pm.ParticipantId == top.ParticipantId)
            .Select(pm => pm.UserId)
            .ToListAsync(ct);

        return memberIds.Count == 0 ? null : (top.DisplayName, memberIds);
    }
}
