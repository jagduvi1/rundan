using Rundan.Server;
using Rundan.Server.Security;
using Rundan.Server.Services;

namespace Rundan.Server.Endpoints;

/// <summary>Admin-only maintenance actions (host dashboard "danger zone").</summary>
internal static class MaintenanceEndpoints
{
    public static void MapMaintenanceEndpoints(this IEndpointRouteBuilder app)
    {
        // Wipe all data and re-seed the demo day. Destructive — fails closed.
        app.MapPost("/api/admin/clean-and-seed", async (
            MaintenanceService maintenance, RundanOptions options, CancellationToken ct) =>
        {
            // The admin filter is a no-op when no admin code is configured (dev-open). A total,
            // irreversible wipe must never run on an open deployment, so require a real admin code.
            if (!options.RequiresAdminCode)
            {
                return Results.Problem(
                    title: "Disabled",
                    detail: "Clean & seed requires a configured admin code.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            await maintenance.CleanAndSeedAsync(ct);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter<AdminEndpointFilter>();
    }
}
