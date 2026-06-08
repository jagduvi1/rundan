namespace Rundan.Server.Data.Entities;

/// <summary>A playing surface within an activity — a court, field, track or lane.</summary>
public class Court
{
    public int Id { get; set; }

    public int ActivityId { get; set; }
    public Activity? Activity { get; set; }

    /// <summary>1-based position used for default names and ordering.</summary>
    public int Order { get; set; }

    /// <summary>Display name, e.g. "Court 1" (or a custom name).</summary>
    public string Name { get; set; } = string.Empty;
}
