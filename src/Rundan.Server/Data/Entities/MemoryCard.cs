namespace Rundan.Server.Data.Entities;

/// <summary>One label in a Memory game. Each label becomes two matching cards on the shuffled board.</summary>
public class MemoryCard
{
    public int Id { get; set; }
    public int ActivityId { get; set; }
    public Activity? Activity { get; set; }

    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;
}
