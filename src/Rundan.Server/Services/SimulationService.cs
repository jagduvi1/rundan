using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Shared;

namespace Rundan.Server.Services;

/// <summary>
/// Dry-run support: fills an activity with plausible random results so the host can check the
/// whole setup (scoreboards, bracket, event standings) before the real day — and clear it again.
/// Writes directly to the store (it doesn't go through the player endpoints), then finishes the
/// activity so the event's placement total updates.
/// </summary>
public sealed class SimulationService(AppDbContext db, TeamService teams, BracketService brackets, TimeProvider clock)
{
    /// <summary>Generates teams (for event activities), fills random results, and finishes the activity.</summary>
    public async Task SimulateAsync(int activityId, CancellationToken ct = default)
    {
        var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == activityId, ct)
            ?? throw new RuleViolationException("Activity not found.", StatusCodes.Status404NotFound);

        if (activity.EventId is not null)
        {
            await teams.EnsureTeamsAsync(activity, ct);
        }

        await ClearResultsAsync(activityId, ct);

        var participants = await db.Participants
            .Where(p => p.ActivityId == activityId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        switch (activity.Type)
        {
            case ActivityType.Quiz or ActivityType.Tipspromenad:
                await SimulateAnswersAsync(activityId, participants, ct);
                break;
            case ActivityType.Boule:
                await SimulateBracketAsync(activityId, ct);
                break;
            default:
                SimulateScores(activity, participants);
                break;
        }

        activity.Status = ActivityStatus.Finished;
        activity.StartedUtc ??= clock.GetUtcNow();
        activity.FinishedUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Clears all results for the activity and returns it to Draft.</summary>
    public async Task ResetAsync(int activityId, CancellationToken ct = default)
    {
        var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == activityId, ct)
            ?? throw new RuleViolationException("Activity not found.", StatusCodes.Status404NotFound);

        await ClearResultsAsync(activityId, ct);
        activity.Status = ActivityStatus.Draft;
        activity.StartedUtc = null;
        activity.FinishedUtc = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<int>> EventActivityIdsAsync(int eventId, CancellationToken ct = default) =>
        await db.Activities.Where(a => a.EventId == eventId).OrderBy(a => a.Order).Select(a => a.Id).ToListAsync(ct);

    /// <summary>Deletes the whole group-chat history for an event (host opt-in on a restart).</summary>
    public async Task ClearEventChatAsync(int eventId, CancellationToken ct = default) =>
        await db.ChatMessages.Where(m => m.EventId == eventId).ExecuteDeleteAsync(ct);

    private async Task ClearResultsAsync(int activityId, CancellationToken ct)
    {
        db.Answers.RemoveRange(db.Answers.Where(a => a.Participant!.ActivityId == activityId));
        db.ScoreEntries.RemoveRange(db.ScoreEntries.Where(s => s.ActivityId == activityId));
        db.BracketMatches.RemoveRange(db.BracketMatches.Where(m => m.ActivityId == activityId));
        // Drop the drawn MapPin cities too, so re-opening draws a fresh set instead of replaying them.
        db.MapCities.RemoveRange(db.MapCities.Where(c => c.ActivityId == activityId));
        await db.SaveChangesAsync(ct);

        // Slaps reference the activity by a loose int (no FK), so clear them too — otherwise the
        // penalties linger in the standings after a reset.
        await db.Slaps.Where(s => s.ActivityId == activityId).ExecuteDeleteAsync(ct);

        // Clear any music-quiz "track started" timers so a replay begins fresh.
        await db.Questions.Where(q => q.ActivityId == activityId && q.PlayStartedUtc != null)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.PlayStartedUtc, (DateTimeOffset?)null), ct);
    }

    private async Task SimulateAnswersAsync(int activityId, List<int> participants, CancellationToken ct)
    {
        var questions = await db.Questions
            .Where(q => q.ActivityId == activityId)
            .Include(q => q.Options)
            .ToListAsync(ct);

        var now = clock.GetUtcNow();
        foreach (var pid in participants)
        {
            foreach (var q in questions)
            {
                var makeCorrect = Random.Shared.NextDouble() < 0.7;
                AnswerOption? chosen = null;
                bool isCorrect;
                if (q.Options.Count > 0)
                {
                    chosen = (makeCorrect ? q.Options.FirstOrDefault(o => o.IsCorrect) : q.Options.FirstOrDefault(o => !o.IsCorrect))
                             ?? q.Options[Random.Shared.Next(q.Options.Count)];
                    isCorrect = chosen.IsCorrect;
                }
                else
                {
                    isCorrect = makeCorrect;
                }

                db.Answers.Add(new Answer
                {
                    QuestionId = q.Id,
                    ParticipantId = pid,
                    SelectedOptionId = chosen?.Id,
                    FreeText = q.Options.Count == 0 ? (isCorrect ? q.AcceptedFreeTextAnswer : "—") : null,
                    IsCorrect = isCorrect,
                    AwardedPoints = isCorrect ? q.Points : 0,
                    SubmittedUtc = now,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SimulateBracketAsync(int activityId, CancellationToken ct)
    {
        await brackets.GenerateAsync(activityId, ct);

        // Resolve every playable match with a random winner until the tournament is complete. This
        // sweeps the round-robin group matches first (which then seed the playoffs) and then the
        // bracket rounds, so the guard must cover a full group stage plus two playoff brackets.
        for (var guard = 0; guard < 1000; guard++)
        {
            var match = await db.BracketMatches
                .Where(m => m.ActivityId == activityId && m.WinnerParticipantId == null && !m.IsBye
                            && m.ParticipantAId != null && m.ParticipantBId != null)
                .OrderBy(m => m.Side).ThenBy(m => m.Round).ThenBy(m => m.Slot)
                .FirstOrDefaultAsync(ct);
            if (match is null)
            {
                break;
            }

            var winner = Random.Shared.Next(2) == 0 ? match.ParticipantAId!.Value : match.ParticipantBId!.Value;
            await brackets.RecordResultAsync(activityId, match.Id, sets: null, explicitWinnerId: winner, ct);
        }
    }

    private void SimulateScores(Activity activity, List<int> participants)
    {
        var now = clock.GetUtcNow();
        foreach (var pid in participants)
        {
            int value;
            if (activity.ScoringMode == ScoringMode.ClosestToTarget)
            {
                var target = activity.TargetValue ?? 100;
                value = Math.Max(0, target + Random.Shared.Next(-30, 31));
            }
            else
            {
                value = activity.Measurement switch
                {
                    Measurement.TimeSeconds => Random.Shared.Next(30, 181),
                    Measurement.Millimetres => Random.Shared.Next(500, 2001),
                    _ when activity.Type == ActivityType.WordGame => Random.Shared.Next(3, 11),
                    _ => Random.Shared.Next(3, 16),
                };
            }

            db.ScoreEntries.Add(new ScoreEntry
            {
                ActivityId = activity.Id,
                ParticipantId = pid,
                Round = 1,
                Points = value,
                Note = "dry run",
                RecordedUtc = now,
            });
        }
    }
}
