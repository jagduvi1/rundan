namespace Rundan.Server.Data.Entities;

/// <summary>One drawn city in a "Pin the city" (MapPin) activity. The real location is server-only —
/// players see the <see cref="Name"/> and place a pin; the distance to here is their score for it.</summary>
public class MapCity
{
    public int Id { get; set; }

    public int ActivityId { get; set; }
    public Activity? Activity { get; set; }

    /// <summary>Order the city is presented in (also the ScoreEntry round used for the team's pin).</summary>
    public int Order { get; set; }

    public string Name { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
