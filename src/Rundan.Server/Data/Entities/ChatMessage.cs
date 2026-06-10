namespace Rundan.Server.Data.Entities;

/// <summary>A message in an event's group chat, visible to everyone in the event.</summary>
public class ChatMessage
{
    public int Id { get; set; }

    public int EventId { get; set; }
    public Event? Event { get; set; }

    /// <summary>The sender's display name (their roster/free-name name, or "Host").</summary>
    public string Author { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
}
