namespace Rundan.Shared.Contracts;

/// <summary>A participant (a friend) in an activity.</summary>
public class ParticipantDto
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public DateTimeOffset JoinedUtc { get; set; }
}

/// <summary>Request body for joining an activity by its join code.</summary>
public class JoinActivityRequest
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// If the device re-joins, it can pass its previously issued token to reclaim
    /// the same participant identity instead of creating a duplicate.
    /// </summary>
    public Guid? ExistingToken { get; set; }
}

/// <summary>
/// Result of joining. The <see cref="Token"/> is the secret the device stores and
/// presents (header <c>X-Rundan-Participant</c>) to attribute answers/scores to it.
/// </summary>
public class JoinResultDto
{
    public Guid Token { get; set; }
    public ActivityDto Activity { get; set; } = new();
    public ParticipantDto Participant { get; set; } = new();
}
