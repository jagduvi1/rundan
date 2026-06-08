namespace Rundan.Server.Data.Entities;

/// <summary>A friend taking part in an activity.</summary>
public class Participant
{
    public int Id { get; set; }
    public int ActivityId { get; set; }
    public Activity? Activity { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>True if this participant is a team (event play); false for a solo player.</summary>
    public bool IsTeam { get; set; }

    /// <summary>For team participants: the roster users on this team (shared across the team's devices).</summary>
    public List<ParticipantMember> Members { get; set; } = new();

    /// <summary>Secret token the device presents to prove it is this participant.</summary>
    public Guid Token { get; set; }

    /// <summary>The creator/admin of the activity (can manage it from the same device).</summary>
    public bool IsAdmin { get; set; }

    public DateTimeOffset JoinedUtc { get; set; }

    public List<Answer> Answers { get; set; } = new();
    public List<ScoreEntry> ScoreEntries { get; set; } = new();
}
