using Rundan.Server;
using Rundan.Server.Data;
using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Endpoints;

internal static class BootstrapEndpoints
{
    public static void MapBootstrapEndpoints(this IEndpointRouteBuilder app)
    {
        // Public (excluded from the access-code gate): lets the client know whether to
        // show the access-code screen at all.
        app.MapGet("/api/bootstrap", (RundanOptions options) => Results.Ok(new BootstrapDto
        {
            AppName = options.AppName,
            RequiresAccessCode = options.RequiresAccessCode,
            RequiresAdminCode = options.RequiresAdminCode,
            SpotifyClientId = options.SpotifyClientId ?? string.Empty,
        }));

        // Gated by the access-code middleware: reaching it means the code is valid.
        app.MapGet("/api/session/verify", () => Results.Ok(new { ok = true }));

        // Reaching this means the admin code is valid (used by the host-panel gate).
        app.MapGet("/api/admin/verify", () => Results.Ok(new { ok = true }))
            .AddEndpointFilter<AdminEndpointFilter>();

        // Admin: seed sample data on demand (no-op if data already exists).
        app.MapPost("/api/admin/seed", async (DataSeeder seeder) =>
        {
            var seeded = await seeder.SeedAsync();
            return Results.Ok(new { seeded });
        }).AddEndpointFilter<AdminEndpointFilter>();

        // Admin: upload a descriptive picture / map. Stored on the persistent disk, served at /uploads.
        app.MapPost("/api/admin/upload", async (
            IFormFile file, StoragePaths paths, AppDbContext db, RundanOptions options, HttpContext http, CancellationToken ct) =>
        {
            if (!await EventAuthorization.CanUploadAsync(http, db, options, ct))
            {
                return EventManagerFilter.Forbidden();
            }

            if (file is null || file.Length == 0)
            {
                throw new RuleViolationException("No file was uploaded.");
            }

            if (file.Length > 5 * 1024 * 1024)
            {
                throw new RuleViolationException("Image is too large (max 5 MB).");
            }

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            string[] allowed = { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg" };
            if (!allowed.Contains(ext))
            {
                throw new RuleViolationException("Use a JPG, PNG, GIF, WEBP or SVG image.");
            }

            var name = $"{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(paths.UploadsDir, name);
            await using (var stream = File.Create(fullPath))
            {
                await file.CopyToAsync(stream, ct);
            }

            return Results.Ok(new UploadResultDto { Url = $"/uploads/{name}" });
        }).DisableAntiforgery();
    }
}
