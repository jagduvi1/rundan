namespace Rundan.Server.Data.Entities;

/// <summary>A photo a player uploaded during an activity, shown on the activity's shared photo wall.</summary>
public class ActivityPhoto
{
    public int Id { get; set; }

    public int ActivityId { get; set; }
    public Activity? Activity { get; set; }

    /// <summary>The uploader's participant display name (team / player name).</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>The served image URL (/uploads/...).</summary>
    public string Url { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }
}
