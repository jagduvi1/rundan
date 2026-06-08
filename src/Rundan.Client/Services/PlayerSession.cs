using Rundan.Shared.Contracts;

namespace Rundan.Client.Services;

/// <summary>A device's saved identity within one activity (persisted to localStorage).</summary>
public sealed class PlayerSession
{
    public Guid Token { get; set; }
    public int ParticipantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }

    /// <summary>Session for a per-activity slot (roster claim / free-name join), labelled with a display name.</summary>
    public static PlayerSession FromSlot(EventJoinSlotDto slot, string displayName) => new()
    {
        Token = slot.Token,
        ParticipantId = slot.ParticipantId,
        DisplayName = displayName,
    };

    /// <summary>Session from joining a single activity (carries the admin flag from the join result).</summary>
    public static PlayerSession FromJoin(JoinResultDto result) => new()
    {
        Token = result.Token,
        ParticipantId = result.Participant.Id,
        DisplayName = result.Participant.DisplayName,
        IsAdmin = result.Participant.IsAdmin,
    };
}

/// <summary>
/// A roster player's identity a host has temporarily taken over (e.g. the player's phone
/// died). While set, it overlays the host's own per-activity sessions and member token so
/// the host answers, scores and slaps as that player. Persisted so a reload keeps it.
/// </summary>
public sealed class ProxyIdentity
{
    public int EventId { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid MemberToken { get; set; }

    /// <summary>The player's team session per open/live activity (activityId -> session).</summary>
    public Dictionary<int, PlayerSession> Sessions { get; set; } = new();
}
