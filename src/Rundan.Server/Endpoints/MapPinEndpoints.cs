using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Server.Security;
using Rundan.Server.Services;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Endpoints;

/// <summary>"Pin the city" (MapPin) gameplay: list the drawn cities (names only) and record a pin's
/// distance. The real city locations stay server-side until a team has pinned them.</summary>
internal static class MapPinEndpoints
{
    public static void MapMapPinEndpoints(this IEndpointRouteBuilder app)
    {
        // The drawn cities (names only) plus the calling team's distance for any it has pinned.
        app.MapGet("/api/activities/{id:int}/map-cities", async (
            int id, AppDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var participant = await http.ResolveForActivityAsync(db, id, ct);

            var cities = await db.MapCities.AsNoTracking()
                .Where(c => c.ActivityId == id)
                .OrderBy(c => c.Order)
                .Select(c => new { c.Id, c.Order, c.Name })
                .ToListAsync(ct);

            // The team's distances live as ScoreEntries keyed by Round == city.Order.
            var byRound = await db.ScoreEntries.AsNoTracking()
                .Where(s => s.ActivityId == id && s.ParticipantId == participant.Id)
                .ToDictionaryAsync(s => s.Round, s => s.Points, ct);

            var dtos = cities.Select(c => new MapCityDto
            {
                Id = c.Id,
                Order = c.Order,
                Name = c.Name,
                Pinned = byRound.ContainsKey(c.Order),
                DistanceKm = byRound.TryGetValue(c.Order, out var d) ? d : null,
            }).ToList();

            return Results.Ok(dtos);
        });

        // Place (or re-place) a pin: the distance is computed server-side and stored as the team's
        // score for that city (one entry per city, replaced on a re-pin).
        app.MapPost("/api/activities/{id:int}/map-pin", async (
            int id, MapPinRequest req, AppDbContext db, ScoreboardNotifier notifier,
            HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var participant = await http.ResolveForActivityAsync(db, id, ct);

            var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == id, ct)
                ?? throw new RuleViolationException("Activity not found.", StatusCodes.Status404NotFound);
            if (activity.Status != ActivityStatus.Live)
            {
                throw new RuleViolationException("This game isn't live.", StatusCodes.Status409Conflict);
            }

            var city = await db.MapCities.FirstOrDefaultAsync(c => c.Id == req.CityId && c.ActivityId == id, ct)
                ?? throw new RuleViolationException("City not found.", StatusCodes.Status404NotFound);

            var distanceKm = Math.Round(GeoMath.DistanceKm(req.Lat, req.Lng, city.Latitude, city.Longitude), 1);

            var entry = await db.ScoreEntries
                .FirstOrDefaultAsync(s => s.ActivityId == id && s.ParticipantId == participant.Id && s.Round == city.Order, ct);
            if (entry is null)
            {
                entry = new ScoreEntry
                {
                    ActivityId = id,
                    ParticipantId = participant.Id,
                    Round = city.Order,
                    RecordedUtc = clock.GetUtcNow(),
                };
                db.ScoreEntries.Add(entry);
            }

            entry.Points = distanceKm;
            await db.SaveChangesAsync(ct);
            await notifier.PushScoreboardAsync(id, ct);

            return Results.Ok(new MapPinResultDto
            {
                CityId = city.Id,
                DistanceKm = distanceKm,
                RealLat = city.Latitude,
                RealLng = city.Longitude,
            });
        });
    }
}
