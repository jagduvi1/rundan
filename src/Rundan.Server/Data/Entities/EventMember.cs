namespace Rundan.Server.Data.Entities;

/// <summary>A roster user selected into an event. The token authenticates that user's device.</summary>
public class EventMember
{
    public int Id { get; set; }

    public int EventId { get; set; }
    public Event? Event { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Secret the device presents to act as this user in the event.</summary>
    public Guid Token { get; set; }

    /// <summary>An event admin (chosen from the participants) can manage this event without the site admin code.</summary>
    public bool IsAdmin { get; set; }

    public DateTimeOffset AddedUtc { get; set; }
}
