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

            var winner = await WinnerAsync(eventId, a.Id, ct);
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
                SlapperUserId = winner.Value.SlapperUserId,
                SlapperName = members.FirstOrDefault(m => m.UserId == winner.Value.SlapperUserId)?.Name,
                EffectiveMode = EffectiveMode(ev.SlapMode, a.Id),
                Members = members.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            };
        }

        return null;
    }

    /// <summary>The slap for one activity as the player flow sees it: pending (winners owe one),
    /// taken (who slapped whom + who got the points), skipped, or none. Independent of order, so it
    /// surfaces as each activity finishes regardless of the running order.</summary>
    public async Task<ActivitySlapDto> ActivitySlapAsync(int activityId, CancellationToken ct = default)
    {
        var result = new ActivitySlapDto { ActivityId = activityId };

        var activity = await db.Activities.AsNoTracking().FirstOrDefaultAsync(a => a.Id == activityId, ct);
        if (activity?.EventId is not int eventId)
        {
            return result; // standalone activity → no slaps
        }

        result.EventId = eventId;
        result.ActivityTitle = activity.Title;

        var ev = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null || ev.SlapMode == SlapMode.Off || activity.Status != ActivityStatus.Finished)
        {
            return result; // None
        }

        result.EffectiveMode = EffectiveMode(ev.SlapMode, activityId);

        var slap = await db.Slaps.AsNoTracking().FirstOrDefaultAsync(s => s.ActivityId == activityId, ct);
        if (slap is not null)
        {
            if (slap.Skipped)
            {
                result.State = SlapState.Skipped;
                return result;
            }

            var ids = new[] { slap.SlapperUserId, slap.SlappedUserId }
                .Concat(slap.RecipientUserId is int r ? new[] { r } : Array.Empty<int>())
                .Distinct().ToList();
            var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

            result.SlapperUserId = slap.SlapperUserId;
            result.SlapperName = names.GetValueOrDefault(slap.SlapperUserId);
            result.SlappedUserId = slap.SlappedUserId;
            result.SlappedName = names.GetValueOrDefault(slap.SlappedUserId);
            result.Penalty = slap.Penalty;

            // SlappedSends: the slap landed but the slapped player hasn't passed the points on yet.
            if (result.EffectiveMode == SlapMode.SlappedSends && slap.RecipientUserId is null)
            {
                result.State = SlapState.AwaitingRecipient;
                result.Members = await MembersAsync(eventId, ct);
                return result;
            }

            result.State = SlapState.Taken;
            result.RecipientName = slap.RecipientUserId is int rid ? names.GetValueOrDefault(rid) : null;
            return result;
        }

        // Not resolved yet → pending, but only if the activity has a winner to slap with.
        var winner = await WinnerAsync(eventId, activityId, ct);
        if (winner is null)
        {
            return result; // None — no team winner (e.g. nobody played)
        }

        result.State = SlapState.Pending;
        result.WinnerName = winner.Value.Name;
        result.WinnerUserIds = winner.Value.MemberIds;
        result.SlapperUserId = winner.Value.SlapperUserId;
        result.Members = await MembersAsync(eventId, ct);
        result.SlapperName = result.Members.FirstOrDefault(m => m.UserId == winner.Value.SlapperUserId)?.Name;
        return result;
    }

    private async Task<List<SlapPersonDto>> MembersAsync(int eventId, CancellationToken ct)
    {
        var members = await db.EventMembers.AsNoTracking()
            .Where(m => m.EventId == eventId)
            .Select(m => new SlapPersonDto { UserId = m.UserId, Name = m.User!.Name })
            .ToListAsync(ct);
        return members.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>SlappedSends mode: the slapped player passes their lost points to a recipient
    /// (never themselves, never the slapper).</summary>
    public async Task SendPointsAsync(int eventId, int activityId, int senderUserId, int recipientUserId, CancellationToken ct = default)
    {
        var ev = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId, ct)
            ?? throw new RuleViolationException("Event not found.", StatusCodes.Status404NotFound);

        var slap = await db.Slaps.FirstOrDefaultAsync(s => s.EventId == eventId && s.ActivityId == activityId, ct)
            ?? throw new RuleViolationException("There's no slap to pass on here.", StatusCodes.Status404NotFound);

        if (slap.Skipped || EffectiveMode(ev.SlapMode, activityId) != SlapMode.SlappedSends)
        {
            throw new RuleViolationException("These points aren't yours to pass on.");
        }

        if (slap.SlappedUserId != senderUserId)
        {
            throw new RuleViolationException("Only the slapped player can pass on the points.", StatusCodes.Status403Forbidden);
        }

        if (slap.RecipientUserId is not null)
        {
            throw new RuleViolationException("You've already passed the points on.", StatusCodes.Status409Conflict);
        }

        if (recipientUserId == senderUserId)
        {
            throw new RuleViolationException("You can't keep the points yourself.");
        }

        if (recipientUserId == slap.SlapperUserId)
        {
            throw new RuleViolationException("You can't give them to whoever slapped you.");
        }

        var members = await db.EventMembers.Where(m => m.EventId == eventId).Select(m => m.UserId).ToListAsync(ct);
        if (!members.Contains(recipientUserId))
        {
            throw new RuleViolationException("Pick a player in this event.");
        }

        slap.RecipientUserId = recipientUserId;
        await db.SaveChangesAsync(ct);
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

        var winner = await WinnerAsync(eventId, activityId, ct)
            ?? throw new RuleViolationException("This activity has no winner to slap with.");
        if (winner.SlapperUserId != slapperUserId)
        {
            throw new RuleViolationException(
                "It's not your slap to take — the lowest-scoring player on the winning team does it.",
                StatusCodes.Status403Forbidden);
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
        var board = await standings.BuildAsync(eventId, ct);
        if (board is null)
        {
            return 0;
        }

        // Match by user id, not display name — two roster players can share a name.
        var entries = board.Entries;
        var idx = entries.FindIndex(e => e.UserId == slappedUserId);
        if (idx < 0)
        {
            return 0;
        }

        var total = entries[idx].TotalPoints;
        var following = entries.Skip(idx + 1).FirstOrDefault(e => e.TotalPoints < total);
        return following is null ? 0 : Math.Max(0d, (total - following.TotalPoints) / 2d);
    }

    /// <summary>The winning team(s) (rank 1) of an activity: the team name, every winning member's
    /// roster id (none can be slapped), and the single member designated to take the slap.</summary>
    private async Task<(string Name, List<int> MemberIds, int SlapperUserId)?> WinnerAsync(int eventId, int activityId, CancellationToken ct)
    {
        var board = await scoreboard.BuildAsync(activityId, ct);
        var top = board?.Entries.Where(e => e.Rank == 1).ToList() ?? new();
        if (top.Count == 0)
        {
            return null;
        }

        // On a first-place tie, every member of every tied team counts as a winner.
        var topIds = top.Select(e => e.ParticipantId).ToList();
        var memberIds = await db.ParticipantMembers.AsNoTracking()
            .Where(pm => topIds.Contains(pm.ParticipantId))
            .Select(pm => pm.UserId)
            .Distinct()
            .ToListAsync(ct);

        if (memberIds.Count == 0)
        {
            return null;
        }

        var name = string.Join(" & ", top.Select(e => e.DisplayName));
        var slapper = await DesignatedSlapperAsync(eventId, activityId, memberIds, ct);
        return (name, memberIds, slapper);
    }

    /// <summary>Of the winning team's members, the one with the lowest overall event score takes the
    /// slap. Ties are broken deterministically-at-random per activity, so the choice is stable across
    /// refreshes (everyone sees the same designated slapper).</summary>
    private async Task<int> DesignatedSlapperAsync(int eventId, int activityId, List<int> memberIds, CancellationToken ct)
    {
        var board = await standings.BuildAsync(eventId, ct);
        var totals = board?.Entries.ToDictionary(e => e.UserId, e => e.TotalPoints) ?? new();
        double Score(int uid) => totals.GetValueOrDefault(uid, 0d);

        var min = memberIds.Min(Score);
        var lowest = memberIds.Where(uid => Score(uid) <= min + 1e-9).OrderBy(uid => uid).ToList();
        if (lowest.Count == 1)
        {
            return lowest[0];
        }

        var hash = unchecked((uint)activityId * 2654435761u);
        return lowest[(int)(hash % (uint)lowest.Count)];
    }
}
