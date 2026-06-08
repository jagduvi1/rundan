namespace Rundan.Server.Data.Entities;

/// <summary>A spectator of an event — sees everything but doesn't compete. Tracked only so others can see who's watching.</summary>
public class EventViewer
{
    public int Id { get; set; }

    public int EventId { get; set; }
    public Event? Event { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Per-device token used to update/remove this viewer.</summary>
    public Guid Token { get; set; }

    /// <summary>Updated on each heartbeat; stale viewers drop off the list.</summary>
    public DateTimeOffset LastSeenUtc { get; set; }
}
