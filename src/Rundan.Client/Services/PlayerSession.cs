namespace Rundan.Client.Services;

/// <summary>A device's saved identity within one activity (persisted to localStorage).</summary>
public sealed class PlayerSession
{
    public Guid Token { get; set; }
    public int ParticipantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
}
