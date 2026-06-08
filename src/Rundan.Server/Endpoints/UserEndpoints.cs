using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Endpoints;

internal static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/users", async (CreateUserRequest req, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var name = (req.Name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                throw new RuleViolationException("Enter a name.");
            }

            if (name.Length > 60)
            {
                name = name[..60];
            }

            if (await db.Users.AnyAsync(u => u.Name == name, ct))
            {
                throw new RuleViolationException("That name is already in the roster.", StatusCodes.Status409Conflict);
            }

            var user = new User { Name = name, CreatedUtc = clock.GetUtcNow() };
            db.Users.Add(user);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                throw new RuleViolationException("That name is already in the roster.", StatusCodes.Status409Conflict);
            }

            return Results.Created($"/api/users/{user.Id}", user.ToDto());
        }).AddEndpointFilter<AdminEndpointFilter>();

        app.MapGet("/api/users", async (AppDbContext db, CancellationToken ct) =>
        {
            var users = await db.Users.AsNoTracking().OrderBy(u => u.Name).ToListAsync(ct);
            return Results.Ok(users.Select(u => u.ToDto()));
        }).AddEndpointFilter<AdminEndpointFilter>();

        app.MapPut("/api/users/{id:int}", async (int id, CreateUserRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.FindAsync([id], ct);
            if (user is null)
            {
                return Results.NotFound();
            }

            var name = (req.Name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                throw new RuleViolationException("Enter a name.");
            }

            if (name.Length > 60)
            {
                name = name[..60];
            }

            if (await db.Users.AnyAsync(u => u.Name == name && u.Id != id, ct))
            {
                throw new RuleViolationException("That name is already in the roster.", StatusCodes.Status409Conflict);
            }

            user.Name = name;

            // Propagate the new name into already-formed team labels ("A & B"), so existing
            // activities show it too — otherwise those snapshots would keep the old name.
            var teamIds = await db.ParticipantMembers.Where(pm => pm.UserId == id)
                .Select(pm => pm.ParticipantId).Distinct().ToListAsync(ct);
            if (teamIds.Count > 0)
            {
                var teams = await db.Participants
                    .Include(p => p.Members).ThenInclude(m => m.User)
                    .Where(p => teamIds.Contains(p.Id) && p.IsTeam)
                    .ToListAsync(ct);
                foreach (var team in teams)
                {
                    var names = team.Members.OrderBy(m => m.Id)
                        .Select(m => m.UserId == id ? name : m.User!.Name);
                    team.DisplayName = string.Join(" & ", names);
                }
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(user.ToDto());
        }).AddEndpointFilter<AdminEndpointFilter>();

        app.MapDelete("/api/users/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.FindAsync([id], ct);
            if (user is null)
            {
                return Results.NotFound();
            }

            db.Users.Remove(user);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).AddEndpointFilter<AdminEndpointFilter>();
    }
}
