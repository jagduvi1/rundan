using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Shared;

namespace Rundan.Server.Services;

/// <summary>
/// Pulls random questions from the pre-generated library into an activity, filtered by tag and
/// avoiding ones the host has already used. Lets a participating host run a quiz they didn't author.
/// </summary>
public sealed class QuestionLibraryService(AppDbContext db, TimeProvider clock)
{
    public async Task<List<string>> TagsAsync(CancellationToken ct = default) =>
        await db.QuestionTemplateTags.AsNoTracking()
            .Select(t => t.Tag).Distinct()
            .OrderBy(t => t)
            .ToListAsync(ct);

    public async Task<int> AvailableCountAsync(IReadOnlyCollection<string> tags, CancellationToken ct = default) =>
        (await CandidatesAsync(tags, ct)).Count;

    /// <summary>Adds up to <paramref name="count"/> random matching questions to the activity; returns how many were added.</summary>
    public async Task<(int Added, int Available)> GenerateIntoAsync(int activityId, int count, IReadOnlyCollection<string> tags, CancellationToken ct = default)
    {
        var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == activityId, ct)
            ?? throw new RuleViolationException("Activity not found.", StatusCodes.Status404NotFound);

        if (activity.Type is not (ActivityType.Quiz or ActivityType.Tipspromenad))
        {
            throw new RuleViolationException("Only quiz and tipspromenad activities use questions.");
        }

        if (activity.Status != ActivityStatus.Draft)
        {
            throw new RuleViolationException("Set the activity back to Draft to add questions.");
        }

        if (count < 1)
        {
            throw new RuleViolationException("Ask for at least one question.");
        }

        var candidates = await CandidatesAsync(tags, ct);
        var available = candidates.Count;
        var picked = candidates.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();

        var nextOrder = (await db.Questions.Where(q => q.ActivityId == activityId).MaxAsync(q => (int?)q.Order, ct) ?? 0) + 1;
        var now = clock.GetUtcNow();

        foreach (var template in picked)
        {
            db.Questions.Add(new Question
            {
                ActivityId = activityId,
                Order = nextOrder++,
                Text = template.Text,
                Kind = template.Kind,
                Points = template.Points,
                AcceptedFreeTextAnswer = template.AcceptedFreeTextAnswer,
                Options = template.Options
                    .OrderBy(o => o.Order)
                    .Select(o => new AnswerOption { Order = o.Order, Text = o.Text, IsCorrect = o.IsCorrect })
                    .ToList(),
            });
            db.QuestionTemplateUsages.Add(new QuestionTemplateUsage { QuestionTemplateId = template.Id, UsedUtc = now });
        }

        await db.SaveChangesAsync(ct);
        return (picked.Count, available - picked.Count);
    }

    public async Task ResetUsageAsync(CancellationToken ct = default)
    {
        db.QuestionTemplateUsages.RemoveRange(db.QuestionTemplateUsages);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Unused templates matching the selected tags. Tags are grouped by dimension (the part
    /// before ':' — e.g. topic / age / difficulty): within a dimension a template needs ANY of
    /// the chosen tags (so several categories act as OR), and it must satisfy every dimension (AND).
    /// </summary>
    private async Task<List<QuestionTemplate>> CandidatesAsync(IReadOnlyCollection<string> tags, CancellationToken ct)
    {
        var wanted = tags.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).ToList();
        var groups = wanted
            .GroupBy(t => t.Contains(':') ? t[..t.IndexOf(':')] : t)
            .Select(g => g.ToHashSet())
            .ToList();

        var usedIds = await db.QuestionTemplateUsages.AsNoTracking().Select(u => u.QuestionTemplateId).ToListAsync(ct);
        var used = usedIds.ToHashSet();

        var all = await db.QuestionTemplates.AsNoTracking()
            .Include(t => t.Options)
            .Include(t => t.Tags)
            .ToListAsync(ct);

        return all
            .Where(t => !used.Contains(t.Id))
            .Where(t => groups.All(group => t.Tags.Any(tag => group.Contains(tag.Tag))))
            .ToList();
    }
}
