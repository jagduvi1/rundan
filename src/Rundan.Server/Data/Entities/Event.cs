using Rundan.Shared;

namespace Rundan.Server.Data.Entities;

/// <summary>
/// An event/day that groups several activities of different types (e.g. a quiz walk,
/// a quiz and a boule game). Players join the event once and points across all of its
/// activities add up into a combined standings.
/// </summary>
public class Event
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Free-text rules / info shown to players.</summary>
    public string? Description { get; set; }

    /// <summary>Optional descriptive picture or map for the event.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Players per team within the event's activities (preset, e.g. 2/4/6).</summary>
    public int TeamSize { get; set; } = 2;

    /// <summary>How the combined event total is calculated.</summary>
    public EventScoring Scoring { get; set; } = EventScoring.Cumulative;

    /// <summary>Short, human-typable join code for the whole event.</summary>
    public string JoinCode { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }

    public List<Activity> Activities { get; set; } = new();
    public List<EventMember> Members { get; set; } = new();
}
