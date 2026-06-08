namespace Rundan.Server.Data.Entities;

/// <summary>Links a team participant (in one activity) to one of its member users.</summary>
public class ParticipantMember
{
    public int Id { get; set; }

    public int ParticipantId { get; set; }
    public Participant? Participant { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }
}
