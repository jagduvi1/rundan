using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Services;

/// <summary>
/// The activity library: deep-copies a public activity's definition (rules, picture,
/// scoring config, questions + options, courts) into an event as a fresh Draft instance.
/// Live state — status, participants, scores, teams, bracket — is never copied.
/// </summary>
public sealed class ActivityLibraryService(AppDbContext db, JoinCodeGenerator codes, TimeProvider clock)
{
    public async Task<int> CopyToEventAsync(int sourceId, int eventId, CancellationToken ct = default)
    {
        // Only public library activities may be copied — never a private activity from
        // another event (which would leak its questions and answer keys).
        var src = await db.Activities
            .AsNoTracking()
            .Include(a => a.Questions).ThenInclude(q => q.Options)
            .Include(a => a.Courts)
            .FirstOrDefaultAsync(a => a.Id == sourceId && a.IsPublic, ct)
            ?? throw new RuleViolationException("Library activity not found.", StatusCodes.Status404NotFound);

        var order = (await db.Activities.Where(a => a.EventId == eventId).MaxAsync(a => (int?)a.Order, ct) ?? 0) + 1;

        var copy = new Activity
        {
            EventId = eventId,
            Order = order,
            Type = src.Type,
            Title = src.Title,
            Description = src.Description,
            ImageUrl = src.ImageUrl,
            ScoringMode = src.ScoringMode,
            Measurement = src.Measurement,
            TargetValue = src.TargetValue,
            MapCityCount = src.MapCityCount,
            RandomizeQuestions = src.RandomizeQuestions,
            HideQuestionsFromHost = src.HideQuestionsFromHost,
            ScoreEntryMode = src.ScoreEntryMode,
            CourtLabel = src.CourtLabel,
            Latitude = src.Latitude,
            Longitude = src.Longitude,
            RadiusMeters = src.RadiusMeters,
            IsPublic = false, // a copy in an event isn't itself a library item unless re-published
            Status = ActivityStatus.Draft,
            JoinCode = await codes.NextAsync(db, ct),
            CreatedUtc = clock.GetUtcNow(),
            Questions = src.Questions
                .OrderBy(q => q.Order)
                .Select(q => new Question
                {
                    Order = q.Order,
                    Text = q.Text,
                    Kind = q.Kind,
                    Points = q.Points,
                    ImageUrl = q.ImageUrl,
                    Latitude = q.Latitude,
                    Longitude = q.Longitude,
                    RadiusMeters = q.RadiusMeters,
                    AcceptedFreeTextAnswer = q.AcceptedFreeTextAnswer,
                    Options = q.Options
                        .OrderBy(o => o.Order)
                        .Select(o => new AnswerOption { Order = o.Order, Text = o.Text, IsCorrect = o.IsCorrect })
                        .ToList(),
                })
                .ToList(),
            Courts = src.Courts
                .OrderBy(c => c.Order)
                .Select(c => new Court { Order = c.Order, Name = c.Name })
                .ToList(),
        };

        db.Activities.Add(copy);
        await db.SaveChangesAsync(ct);
        return copy.Id;
    }
}
