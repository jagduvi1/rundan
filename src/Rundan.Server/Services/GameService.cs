using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Services;

/// <summary>Game write operations: scoring answers and recording score lines.</summary>
public sealed class GameService(AppDbContext db, TimeProvider clock)
{
    /// <summary>MusicQuiz fastest-to-answer: the speed-bonus window in seconds (the bonus decays to 0 across it).</summary>
    public const int SpeedWindowSeconds = 30;


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

        var isMusic = activity.Type == ActivityType.MusicQuiz;
        var totalQuestions = await db.Questions.CountAsync(q => q.ActivityId == activity.Id, ct);

        // Already answered? Return the original result (no resubmission / no score farming).
        var existing = await db.Answers
            .FirstOrDefaultAsync(a => a.QuestionId == question.Id && a.ParticipantId == participant.Id, ct);
        if (existing is not null)
        {
            return await BuildAnswerResultAsync(participant.Id, question, existing, totalQuestions, isMusic, ct);
        }

        int? selectedOptionId;
        string? freeText;
        string? artistText = null;
        bool isCorrect;
        int awarded;
        int? guessedYear = null;
        if (isMusic)
        {
            // Score the song, the artist and (Hitster mode) the year independently — each is worth the
            // track's points; the year earns full points for an exact hit, half if it's close.
            var song = (req.FreeText ?? string.Empty).Trim();
            var artist = (req.ArtistText ?? string.Empty).Trim();
            var asksYear = question.ReleaseYear.HasValue;
            if (song.Length == 0 && artist.Length == 0 && !(asksYear && req.Year.HasValue))
            {
                throw new RuleViolationException("Type the song, the artist or the year first.");
            }

            var songOk = Matches(song, question.AcceptedFreeTextAnswer);
            var artistOk = Matches(artist, question.AcceptedArtist);
            guessedYear = asksYear ? req.Year : null;
            var yearPoints = ScoreYear(guessedYear, question.ReleaseYear, question.Points);
            selectedOptionId = null;
            freeText = song;
            artistText = artist;
            isCorrect = songOk && artistOk;
            awarded = (songOk ? question.Points : 0) + (artistOk ? question.Points : 0) + yearPoints;

            // Speed scoring (opt-in, Kahoot-style): when enabled and the host started this track for live
            // play, a *genuinely correct* answer earns a bonus that decays from the track's points to zero
            // across the window. A merely close (half-credit) year does NOT unlock it — only a right
            // song/artist or an EXACT year. Off = a correct answer is worth flat points regardless of speed.
            var exactYear = question.ReleaseYear is { } ry && guessedYear == ry;
            if (activity.SpeedScoring && (songOk || artistOk || exactYear) && question.PlayStartedUtc is { } startedAt)
            {
                var elapsed = (clock.GetUtcNow() - startedAt).TotalSeconds;
                var fraction = Math.Clamp(1d - elapsed / SpeedWindowSeconds, 0d, 1d);
                awarded += (int)Math.Round(question.Points * fraction);
            }
        }
        else
        {
            (isCorrect, selectedOptionId, freeText) = Evaluate(question, req);
            awarded = isCorrect ? question.Points : 0;
        }

        var answer = new Answer
        {
            QuestionId = question.Id,
            ParticipantId = participant.Id,
            SelectedOptionId = selectedOptionId,
            FreeText = freeText,
            ArtistText = artistText,
            GuessedYear = guessedYear,
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

            return await BuildAnswerResultAsync(participant.Id, question, winner, totalQuestions, isMusic, ct);
        }

        return await BuildAnswerResultAsync(participant.Id, question, answer, totalQuestions, isMusic, ct);
    }

    private static bool Matches(string? guess, string? accepted) =>
        !string.IsNullOrWhiteSpace(accepted) && Normalize(guess) == Normalize(accepted);

    /// <summary>Hitster year scoring: exact year = full points, within two years = half (min 1), else 0.</summary>
    private const int YearTolerance = 2;
    private static int ScoreYear(int? guess, int? correct, int points)
    {
        // points <= 0: nothing to award (and stops the half-credit floor from out-scoring an exact hit on a 0-pt track).
        if (guess is not int g || correct is not int c || points <= 0)
        {
            return 0;
        }

        var delta = Math.Abs(g - c);
        if (delta == 0)
        {
            return points;
        }

        return delta <= YearTolerance ? Math.Max(1, points / 2) : 0;
    }

    // Lenient match for music answers: lowercase, strip punctuation/accents-ish, collapse spaces, drop a leading "the".
    private static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return string.Empty;
        }

        // Decompose so accents become separate marks we can drop (é→e, ö→o, å→a) — lenient matching.
        var decomposed = s.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                sb.Append(' ');
            }
        }

        var cleaned = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return cleaned.StartsWith("the ", StringComparison.Ordinal) ? cleaned[4..] : cleaned;
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
        int participantId, Question question, Answer answer, int totalQuestions, bool isMusic, CancellationToken ct)
    {
        var answeredCount = await db.Answers.CountAsync(a => a.ParticipantId == participantId, ct);
        var dto = new AnswerResultDto
        {
            QuestionId = question.Id,
            IsCorrect = answer.IsCorrect,
            AwardedPoints = answer.AwardedPoints,
            AnsweredCount = answeredCount,
            TotalQuestions = totalQuestions,
        };

        if (isMusic)
        {
            // Reveal this track's answers to the team that just answered it (they can't re-submit).
            dto.SongCorrect = Matches(answer.FreeText, question.AcceptedFreeTextAnswer);
            dto.ArtistCorrect = Matches(answer.ArtistText, question.AcceptedArtist);
            dto.CorrectSong = question.AcceptedFreeTextAnswer;
            dto.CorrectArtist = question.AcceptedArtist;
            dto.CorrectYear = question.ReleaseYear;
            dto.YearPoints = ScoreYear(answer.GuessedYear, question.ReleaseYear, question.Points);
        }

        return dto;
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
        if (activity.Type is not (ActivityType.Boule or ActivityType.ScoreGame or ActivityType.Memory))
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

    /// <summary>If every expected score for a ScoreGame is in, finish it automatically. Returns true
    /// when it just transitioned to Finished. Event team games only — that's where "complete" is
    /// well-defined (a fixed set of teams/players); standalone games are still finished by the host.</summary>
    public async Task<bool> TryAutoFinishScoreGameAsync(Activity activity, CancellationToken ct = default)
    {
        if (activity.Type is not (ActivityType.ScoreGame or ActivityType.Memory) || activity.Status != ActivityStatus.Live || activity.EventId is null)
        {
            return false;
        }

        var teamIds = await db.Participants
            .Where(p => p.ActivityId == activity.Id && p.IsTeam)
            .Select(p => p.Id)
            .ToListAsync(ct);
        if (teamIds.Count == 0)
        {
            return false; // no teams formed yet → nothing to complete against
        }

        int expected, recorded;
        if (activity.ScoreEntryMode == ScoreEntryMode.PerPlayer)
        {
            // One score per player on every team.
            expected = await db.ParticipantMembers.CountAsync(pm => teamIds.Contains(pm.ParticipantId), ct);
            recorded = await db.ScoreEntries
                .Where(s => s.ActivityId == activity.Id && teamIds.Contains(s.ParticipantId) && s.UserId != null)
                .Select(s => new { s.ParticipantId, s.UserId })
                .Distinct()
                .CountAsync(ct);
        }
        else
        {
            // Whole team per round. Time/length games are a single reading per team (no rounds).
            var rounds = activity.Measurement is Measurement.TimeSeconds or Measurement.Millimetres
                ? 1
                : Math.Max(1, activity.RoundCount);
            expected = teamIds.Count * rounds;
            recorded = await db.ScoreEntries
                .Where(s => s.ActivityId == activity.Id && teamIds.Contains(s.ParticipantId) && s.Round >= 1 && s.Round <= rounds)
                .Select(s => new { s.ParticipantId, s.Round })
                .Distinct()
                .CountAsync(ct);
        }

        if (expected <= 0 || recorded < expected)
        {
            return false;
        }

        activity.Status = ActivityStatus.Finished;
        activity.FinishedUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Quiz / tipspromenad / music quiz: finish once every team has answered every question.
    /// Roster team games only (a fixed set of teams) — open-join free-name games are finished by the
    /// host, since "everyone" isn't bounded. Returns true when it just transitioned to Finished.</summary>
    public async Task<bool> TryAutoFinishQuestionsAsync(Activity activity, CancellationToken ct = default)
    {
        if (activity.Type is not (ActivityType.Quiz or ActivityType.Tipspromenad or ActivityType.MusicQuiz)
            || activity.Status != ActivityStatus.Live)
        {
            return false;
        }

        var teamIds = await db.Participants
            .Where(p => p.ActivityId == activity.Id && p.IsTeam)
            .Select(p => p.Id)
            .ToListAsync(ct);
        var questionCount = await db.Questions.CountAsync(q => q.ActivityId == activity.Id, ct);
        var expected = teamIds.Count * questionCount;
        if (expected <= 0)
        {
            return false;
        }

        var recorded = await db.Answers.CountAsync(a => teamIds.Contains(a.ParticipantId), ct);
        if (recorded < expected)
        {
            return false;
        }

        activity.Status = ActivityStatus.Finished;
        activity.FinishedUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>MapPin: finish once every team has dropped a pin for every city. Roster team games
    /// only. Returns true when it just transitioned to Finished.</summary>
    public async Task<bool> TryAutoFinishMapPinAsync(Activity activity, CancellationToken ct = default)
    {
        if (activity.Type != ActivityType.MapPin || activity.Status != ActivityStatus.Live)
        {
            return false;
        }

        var teamIds = await db.Participants
            .Where(p => p.ActivityId == activity.Id && p.IsTeam)
            .Select(p => p.Id)
            .ToListAsync(ct);
        var cityCount = await db.MapCities.CountAsync(c => c.ActivityId == activity.Id, ct);
        var expected = teamIds.Count * cityCount;
        if (expected <= 0)
        {
            return false;
        }

        // Each dropped pin is one ScoreEntry (one per team per city).
        var recorded = await db.ScoreEntries.CountAsync(s => s.ActivityId == activity.Id && teamIds.Contains(s.ParticipantId), ct);
        if (recorded < expected)
        {
            return false;
        }

        activity.Status = ActivityStatus.Finished;
        activity.FinishedUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return true;
    }
}
