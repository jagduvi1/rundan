using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Server.Services;

namespace Rundan.Server.Security;

public static class ParticipantContext
{
    public const string HeaderName = "X-Rundan-Participant";

    /// <summary>
    /// Resolves the calling participant from the <c>X-Rundan-Participant</c> token header.
    /// Throws a 401 rule violation if missing/unknown.
    /// </summary>
    public static async Task<Participant> ResolveAsync(
        this HttpContext context, AppDbContext db, CancellationToken ct = default)
    {
        var raw = context.Request.Headers[HeaderName].ToString();
        if (!Guid.TryParse(raw, out var token) || token == Guid.Empty)
        {
            throw new RuleViolationException("Join the activity first.", StatusCodes.Status401Unauthorized);
        }

        var participant = await db.Participants.FirstOrDefaultAsync(p => p.Token == token, ct);
        return participant
            ?? throw new RuleViolationException(
                "Your session is no longer valid — re-join the activity.", StatusCodes.Status401Unauthorized);
    }
}
