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

    /// <summary>
    /// Host correction of a question's answer key after answers are in (e.g. the wrong option
    /// was marked correct). Updates the key in place — option ids are preserved so existing
    /// answers stay valid — then re-scores every submitted answer for the question.
    /// </summary>
    public async Task<QuestionResultDto> UpdateAnswerKeyAsync(
        Activity activity, int questionId, UpdateAnswerKeyRequest req, CancellationToken ct = default)
    {
        var question = await db.Questions.Include(q => q.Options)
            .FirstOrDefaultAsync(q => q.Id == questionId && q.ActivityId == activity.Id, ct)
            ?? throw new RuleViolationException("That question no longer exists.", StatusCodes.Status404NotFound);

        if (question.Kind == QuestionKind.FreeText)
        {
            var accepted = (req.AcceptedFreeTextAnswer ?? string.Empty).Trim();
            if (accepted.Length == 0)
            {
                throw new RuleViolationException("A free-text question needs an accepted answer.");
            }

            question.AcceptedFreeTextAnswer = accepted;
        }
        else
        {
            if (req.CorrectOptionId is not { } correctId || question.Options.All(o => o.Id != correctId))
            {
                throw new RuleViolationException("Pick which option is correct.");
            }

            foreach (var option in question.Options)
            {
                option.IsCorrect = option.Id == correctId;
            }
        }

        // Re-evaluate every answer against the corrected key (the scoreboard and event standings
        // both sum AwardedPoints, so this reflects across all scoring).
        var answers = await db.Answers.Where(a => a.QuestionId == questionId).ToListAsync(ct);
        foreach (var answer in answers)
        {
            var isCorrect = question.Kind == QuestionKind.FreeText
                ? !string.IsNullOrWhiteSpace(question.AcceptedFreeTextAnswer)
                  && string.Equals((answer.FreeText ?? string.Empty).Trim(),
                      question.AcceptedFreeTextAnswer!.Trim(), StringComparison.OrdinalIgnoreCase)
                : answer.SelectedOptionId is { } sel && question.Options.Any(o => o.Id == sel && o.IsCorrect);

            answer.IsCorrect = isCorrect;
            answer.AwardedPoints = isCorrect ? question.Points : 0;
        }

        await db.SaveChangesAsync(ct);
        return question.ToResultDto();
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
            ?? throw new RuleViolationException("That team is not in this activity.");

        // Per-player mode: the points belong to one player on the team; the team total is the sum.
        int? scoredByUserId = null;
        if (activity.ScoreEntryMode == ScoreEntryMode.PerPlayer && target.IsTeam)
        {
            if (req.UserId is not { } uid)
            {
                throw new RuleViolationException("Pick which player scored.");
            }

            var onTeam = await db.ParticipantMembers.AnyAsync(pm => pm.ParticipantId == target.Id && pm.UserId == uid, ct);
            if (!onTeam)
            {
                throw new RuleViolationException("That player isn't on this team.");
            }

            scoredByUserId = uid;
        }

        if (req.Round is < 1 or > 1000)
        {
            throw new RuleViolationException("Round must be between 1 and 1000.");
        }

        // The UI clamps points, but the API must reject out-of-range/forged values too.
        // The range is generous so it also covers seconds and millimetres measurements.
        if (req.Points < -100000 || req.Points > 100000)
        {
            throw new RuleViolationException("That value is out of range.");
        }

        // Time / length are single measurements — a new reading replaces the old. In per-player
        // mode each player keeps their own reading, so only replace this player's (not the team's).
        if (activity.Measurement is Measurement.TimeSeconds or Measurement.Millimetres)
        {
            var previous = db.ScoreEntries.Where(s =>
                s.ActivityId == activity.Id && s.ParticipantId == target.Id
                && (activity.ScoreEntryMode != ScoreEntryMode.PerPlayer || s.UserId == scoredByUserId));
            db.ScoreEntries.RemoveRange(previous);
        }

        var entry = new ScoreEntry
        {
            ActivityId = activity.Id,
            ParticipantId = target.Id,
            UserId = scoredByUserId,
            Round = req.Round,
            Points = req.Points,
            Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
            RecordedUtc = clock.GetUtcNow(),
        };
        db.ScoreEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        entry.Participant = target;
        if (scoredByUserId is { } scorer)
        {
            entry.User = await db.Users.FirstOrDefaultAsync(u => u.Id == scorer, ct);
        }

        return entry.ToDto();
    }
}
