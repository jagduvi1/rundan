namespace Rundan.Client.Services;

/// <summary>A device's saved identity within one activity (persisted to localStorage).</summary>
public sealed class PlayerSession
{
    public Guid Token { get; set; }
    public int ParticipantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
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
