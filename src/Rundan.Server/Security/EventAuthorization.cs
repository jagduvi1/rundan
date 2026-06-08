using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;

namespace Rundan.Server.Security;

/// <summary>
/// Authorizes event/activity management for EITHER the site host (admin code) OR an
/// event admin — a participant the host promoted, identified by their member token.
/// </summary>
public static class EventAuthorization
{
    public const string MemberHeader = "X-Rundan-Member";

    public static async Task<bool> CanManageEventAsync(
        HttpContext http, AppDbContext db, RundanOptions options, int? eventId, CancellationToken ct = default)
    {
        // Dev convenience: with no admin code configured, management is open.
        if (!options.RequiresAdminCode)
        {
            return true;
        }

        // Site host (global admin code).
        if (SecurityHelpers.FixedEquals(http.Request.Headers[AdminEndpointFilter.HeaderName].ToString(), options.AdminCode))
        {
            return true;
        }

        // Event admin (a promoted participant of this event).
        if (eventId is { } eid && TryGetMemberToken(http, out var token))
        {
            return await db.EventMembers.AsNoTracking()
                .AnyAsync(m => m.Token == token && m.EventId == eid && m.IsAdmin, ct);
        }

        return false;
    }

    public static async Task<bool> CanManageActivityAsync(
        HttpContext http, AppDbContext db, RundanOptions options, int activityId, CancellationToken ct = default)
    {
        var eventId = await db.Activities.AsNoTracking()
            .Where(a => a.Id == activityId)
            .Select(a => a.EventId)
            .FirstOrDefaultAsync(ct);
        return await CanManageEventAsync(http, db, options, eventId, ct);
    }

    /// <summary>Image upload: site host, or an event admin of any event.</summary>
    public static async Task<bool> CanUploadAsync(
        HttpContext http, AppDbContext db, RundanOptions options, CancellationToken ct = default)
    {
        if (!options.RequiresAdminCode)
        {
            return true;
        }

        if (SecurityHelpers.FixedEquals(http.Request.Headers[AdminEndpointFilter.HeaderName].ToString(), options.AdminCode))
        {
            return true;
        }

        return TryGetMemberToken(http, out var token)
               && await db.EventMembers.AsNoTracking().AnyAsync(m => m.Token == token && m.IsAdmin, ct);
    }

    private static bool TryGetMemberToken(HttpContext http, out Guid token)
        => Guid.TryParse(http.Request.Headers[MemberHeader].ToString(), out token) && token != Guid.Empty;
}

/// <summary>Endpoint filter for event-scoped routes where the route "id" is the event id.</summary>
public sealed class EventManagerFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetRequiredService<AppDbContext>();
        var options = http.RequestServices.GetRequiredService<RundanOptions>();

        int? eventId = int.TryParse(http.Request.RouteValues["id"]?.ToString(), out var id) ? id : null;
        if (await EventAuthorization.CanManageEventAsync(http, db, options, eventId))
        {
            return await next(context);
        }

        return Forbidden();
    }

    internal static IResult Forbidden() => Results.Problem(
        title: "Not allowed", detail: "Only the host or an event admin can do this.",
        statusCode: StatusCodes.Status403Forbidden);
}

/// <summary>Endpoint filter for activity-scoped routes where the route "id" is the activity id.</summary>
public sealed class ActivityManagerFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetRequiredService<AppDbContext>();
        var options = http.RequestServices.GetRequiredService<RundanOptions>();

        if (int.TryParse(http.Request.RouteValues["id"]?.ToString(), out var activityId)
            && await EventAuthorization.CanManageActivityAsync(http, db, options, activityId))
        {
            return await next(context);
        }

        return EventManagerFilter.Forbidden();
    }
}
