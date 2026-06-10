namespace Rundan.Server.Data.Entities;

/// <summary>A browser Web Push subscription for one device, scoped to the event it subscribed under
/// (so notifications only go to people following that event).</summary>
public class PushSubscription
{
    public int Id { get; set; }

    public int EventId { get; set; }
    public Event? Event { get; set; }

    /// <summary>The push service endpoint URL (unique per device subscription).</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>The client public key (base64url) for payload encryption.</summary>
    public string P256dh { get; set; } = string.Empty;

    /// <summary>The client auth secret (base64url).</summary>
    public string Auth { get; set; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; set; }
}
