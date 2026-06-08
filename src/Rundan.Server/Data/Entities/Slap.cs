namespace Rundan.Server.Data.Entities;

/// <summary>
/// One resolved slap for an activity (at most one per activity). The slapped player's event
/// total is reduced by <see cref="Penalty"/>; for a "send" slap those points go to
/// <see cref="RecipientUserId"/>. A skipped slap records that the host moved on without one.
/// User ids are stored loosely (no FK) — the event cascade cleans them up.
/// </summary>
public class Slap
{
    public int Id { get; set; }

    public int EventId { get; set; }
    public Event? Event { get; set; }

    public int ActivityId { get; set; }

    /// <summary>The winning-team player who performed the slap.</summary>
    public int SlapperUserId { get; set; }

    /// <summary>The slapped (penalised) player.</summary>
    public int SlappedUserId { get; set; }

    /// <summary>For a "send" slap: who receives the points. Null = vanished.</summary>
    public int? RecipientUserId { get; set; }

    /// <summary>Points removed from the slapped player (exactly half their lead over the next player).</summary>
    public double Penalty { get; set; }

    /// <summary>The host moved on without a slap.</summary>
    public bool Skipped { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
}
