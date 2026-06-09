using Rundan.Server;
using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Endpoints;

/// <summary>Admin-only maintenance actions (host dashboard "danger zone").</summary>
internal static class MaintenanceEndpoints
{
    public static void MapMaintenanceEndpoints(this IEndpointRouteBuilder app)
    {
        // Wipe all data and re-seed the demo day. Destructive — guarded by a confirmation code the host
        // must type (RundanOptions.SeedCode, default "CALLE"; server-side so it's not in the client).
        app.MapPost("/api/admin/clean-and-seed", async (
            CleanAndSeedRequest req, MaintenanceService maintenance, RundanOptions options, CancellationToken ct) =>
        {
            if (!string.Equals(req.Code?.Trim(), options.SeedCode, StringComparison.Ordinal))
            {
                return Results.Problem(
                    title: "Wrong code",
                    detail: "That's not the right code.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            await maintenance.CleanAndSeedAsync(ct);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter<AdminEndpointFilter>();
    }
}
