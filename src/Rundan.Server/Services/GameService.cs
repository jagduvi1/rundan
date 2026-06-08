using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Services;

/// <summary>Game write operations: scoring answers and recording score lines.</summary>
public sealed class GameService(AppDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Records a participant's answer to a question and awards points. One answer per
    /// participant per question; a repeated submission returns the original result.
    /// </summary>
    public async Task<AnswerResultDto> SubmitAnswerAsync(
        Participant participant, SubmitAnswerRequest req, CancellationToken ct = default)
    {
        var question = await db.Questions
            .Include(q => q.Options)
            .FirstOrDefaultAsync(q => q.Id == req.QuestionId, ct)
            ?? throw new RuleViolationException("That question no longer exists.", StatusCodes.Status404NotFound);

        if (question.ActivityId != participant.ActivityId)
        {
            throw new RuleViolationException("That question is not part of your activity.");
        }

        var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == participant.ActivityId, ct)
            ?? throw new RuleViolationException("This activity no longer exists.", StatusCodes.Status404NotFound);
        if (activity.Status != ActivityStatus.Live)
        {
            throw new RuleViolationException("This activity is not accepting answers right now.",
                StatusCodes.Status409Conflict);
        }

        var totalQuestions = await db.Questions.CountAsync(q => q.ActivityId == activity.Id, ct);

        // Already answered? Return the original result (no resubmission / no score farming).
        var existing = await db.Answers
            .FirstOrDefaultAsync(a => a.QuestionId == question.Id && a.ParticipantId == participant.Id, ct);
        if (existing is not null)
        {
            return await BuildAnswerResultAsync(participant.Id, question.Id, existing, totalQuestions, ct);
        }

        var (isCorrect, selectedOptionId, freeText) = Evaluate(question, req);
        var awarded = isCorrect ? question.Points : 0;

        var answer = new Answer
        {
            QuestionId = question.Id,
            ParticipantId = participant.Id,
            SelectedOptionId = selectedOptionId,
            FreeText = freeText,
            IsCorrect = isCorrect,
            AwardedPoints = awarded,
            SubmittedUtc = clock.GetUtcNow(),
        };
        db.Answers.Add(answer);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Only the (QuestionId, ParticipantId) uniqueness race leaves a winner row to
            // return; any other DB failure (disk/IO, FK, ...) must keep its real cause.
            db.Entry(answer).State = EntityState.Detached;
            var winner = await db.Answers
                .FirstOrDefaultAsync(a => a.QuestionId == question.Id && a.ParticipantId == participant.Id, ct);
            if (winner is null)
            {
                throw;
            }

            return await BuildAnswerResultAsync(participant.Id, question.Id, winner, totalQuestions, ct);
        }

        return await BuildAnswerResultAsync(participant.Id, question.Id, answer, totalQuestions, ct);
    }

    private static (bool isCorrect, int? selectedOptionId, string? freeText) Evaluate(
        Question question, SubmitAnswerRequest req)
    {
        switch (question.Kind)
        {
            case QuestionKind.MultipleChoice:
            case QuestionKind.TrueFalse:
                var option = question.Options.FirstOrDefault(o => o.Id == req.SelectedOptionId)
                    ?? throw new RuleViolationException("Choose one of the options.");
                return (option.IsCorrect, option.Id, null);

            case QuestionKind.FreeText:
                var given = (req.FreeText ?? string.Empty).Trim();
                if (given.Length == 0)
                {
                    throw new RuleViolationException("Type an answer first.");
                }

                var correct = !string.IsNullOrWhiteSpace(question.AcceptedFreeTextAnswer)
                              && string.Equals(given, question.AcceptedFreeTextAnswer!.Trim(),
                                  StringComparison.OrdinalIgnoreCase);
                return (correct, null, given);

            default:
                throw new RuleViolationException("Unsupported question type.");
        }
    }

    private async Task<AnswerResultDto> BuildAnswerResultAsync(
        int participantId, int questionId, Answer answer, int totalQuestions, CancellationToken ct)
    {
        var answeredCount = await db.Answers.CountAsync(a => a.ParticipantId == participantId, ct);
        return new AnswerResultDto
        {
            QuestionId = questionId,
            IsCorrect = answer.IsCorrect,
            AwardedPoints = answer.AwardedPoints,
            AnsweredCount = answeredCount,
            TotalQuestions = totalQuestions,
        };
    }

    /// <summary>Records a score line for a round-based game (boule / generic).</summary>
    public async Task<ScoreEntryDto> RecordScoreAsync(
        Activity activity, RecordScoreRequest req, CancellationToken ct = default)
    {
        if (activity.Type is not (ActivityType.Boule or ActivityType.ScoreGame))
        {
            throw new RuleViolationException("This activity does not use score rounds.");
        }

        if (activity.Status != ActivityStatus.Live)
        {
            throw new RuleViolationException("This activity is not accepting scores right now.",
                StatusCodes.Status409Conflict);
        }

        var target = await db.Participants
            .FirstOrDefaultAsync(p => p.Id == req.ParticipantId && p.ActivityId == activity.Id, ct)
            ?? throw new RuleViolationException("That player is not in this activity.");

        if (req.Round is < 1 or > 1000)
        {
            throw new RuleViolationException("Round must be between 1 and 1000.");
        }

        // The UI clamps points, but the API must reject out-of-range/forged values too.
        if (req.Points is < -1000 or > 1000)
        {
            throw new RuleViolationException("Points must be between -1000 and 1000.");
        }

        var entry = new ScoreEntry
        {
            ActivityId = activity.Id,
            ParticipantId = target.Id,
            Round = req.Round,
            Points = req.Points,
            Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            RecordedUtc = clock.GetUtcNow(),
        };
        db.ScoreEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        entry.Participant = target;
        return entry.ToDto();
    }
}
