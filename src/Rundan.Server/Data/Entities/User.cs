namespace Rundan.Server.Data.Entities;

/// <summary>
/// A pre-registered person in the admin's global roster. Users are selected into
/// events and grouped into teams; each user keeps an individual cumulative score.
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }

    public List<EventMember> EventMemberships { get; set; } = new();
}
