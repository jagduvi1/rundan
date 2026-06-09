using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Endpoints;

/// <summary>Admin-only maintenance actions (host dashboard "danger zone").</summary>
internal static class MaintenanceEndpoints
{
    public static void MapMaintenanceEndpoints(this IEndpointRouteBuilder app)
    {
        // Wipe all data and re-seed the demo day. Destructive — guarded by a hardcoded confirmation
        // code the host must type (a deliberate barrier; for real protection configure an admin code).
        app.MapPost("/api/admin/clean-and-seed", async (
            CleanAndSeedRequest req, MaintenanceService maintenance, CancellationToken ct) =>
        {
            if (!string.Equals(req.Code?.Trim(), SeedCodes.CleanAndSeed, StringComparison.Ordinal))
            {
                return Results.Problem(
                    title: "Wrong code",
                    detail: $"Enter the code \"{SeedCodes.CleanAndSeed}\" to clean & seed.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            await maintenance.CleanAndSeedAsync(ct);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter<AdminEndpointFilter>();
    }
}
