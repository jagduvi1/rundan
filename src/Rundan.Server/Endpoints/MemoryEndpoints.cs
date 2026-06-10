using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Server.Security;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Endpoints;

/// <summary>Memory game setup: the host's list of card labels (each becomes two cards on the board).</summary>
internal static class MemoryEndpoints
{
    public static void MapMemoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/activities/{id:int}/memory-cards", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var cards = await db.MemoryCards.AsNoTracking()
                .Where(c => c.ActivityId == id).OrderBy(c => c.Order)
                .Select(c => new MemoryCardDto { Id = c.Id, Order = c.Order, Text = c.Text })
                .ToListAsync(ct);
            return Results.Ok(cards);
        });

        app.MapPut("/api/activities/{id:int}/memory-cards", async (
            int id, SetMemoryCardsRequest req, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Activities.AnyAsync(a => a.Id == id, ct))
            {
                return Results.NotFound();
            }

            var words = req.Words
                .Select(w => (w ?? string.Empty).Trim())
                .Where(w => w.Length > 0)
                .Select(w => w.Length > 120 ? w[..120] : w)
                .Take(40)
                .ToList();

            db.MemoryCards.RemoveRange(db.MemoryCards.Where(c => c.ActivityId == id));
            var order = 1;
            foreach (var w in words)
            {
                db.MemoryCards.Add(new MemoryCard { ActivityId = id, Order = order++, Text = w });
            }
            await db.SaveChangesAsync(ct);

            var cards = await db.MemoryCards.AsNoTracking()
                .Where(c => c.ActivityId == id).OrderBy(c => c.Order)
                .Select(c => new MemoryCardDto { Id = c.Id, Order = c.Order, Text = c.Text })
                .ToListAsync(ct);
            return Results.Ok(cards);
        }).AddEndpointFilter<ActivityManagerFilter>();
    }
}
