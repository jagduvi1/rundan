using Rundan.Server.Data.Entities;
using Rundan.Shared;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Services;

/// <summary>Entity → DTO mapping helpers. Player-facing maps never leak correctness.</summary>
public static class Mapping
{
    public static ActivityDto ToDto(this Activity a, int participantCount, int questionCount) => new()
    {
        Id = a.Id,
        EventId = a.EventId,
        Order = a.Order,
        Type = a.Type,
        Title = a.Title,
        Description = a.Description,
        Status = a.Status,
        ImageUrl = a.ImageUrl,
        JoinCode = a.JoinCode,
        ScoringMode = a.ScoringMode,
        Measurement = a.Measurement,
        TargetValue = a.TargetValue,
        RandomizeQuestions = a.RandomizeQuestions,
        ScoreEntryMode = a.ScoreEntryMode,
        Latitude = a.Latitude,
        Longitude = a.Longitude,
        RadiusMeters = a.RadiusMeters,
        ParticipantCount = participantCount,
        QuestionCount = questionCount,
        CreatedUtc = a.CreatedUtc,
        StartedUtc = a.StartedUtc,
        FinishedUtc = a.FinishedUtc,
    };

    public static UserDto ToDto(this User u) => new() { Id = u.Id, Name = u.Name };

    public static EventDto ToDto(this Event e, List<ActivityDto> activities, List<UserDto> members) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
        ImageUrl = e.ImageUrl,
        TeamSize = e.TeamSize,
        Scoring = e.Scoring,
        JoinCode = e.JoinCode,
        CreatedUtc = e.CreatedUtc,
        Activities = activities,
        Members = members,
    };

    public static ParticipantDto ToDto(this Participant p) => new()
    {
        Id = p.Id,
        DisplayName = p.DisplayName,
        IsAdmin = p.IsAdmin,
        JoinedUtc = p.JoinedUtc,
    };

    /// <summary>Player-facing question: options have no correctness flag.</summary>
    public static QuestionDto ToPlayerDto(this Question q) => new()
    {
        Id = q.Id,
        Order = q.Order,
        Text = q.Text,
        Kind = q.Kind,
        Points = q.Points,
        ImageUrl = q.ImageUrl,
        Latitude = q.Latitude,
        Longitude = q.Longitude,
        RadiusMeters = q.RadiusMeters,
        Options = q.Options
            .OrderBy(o => o.Order)
            .Select(o => new AnswerOptionDto { Id = o.Id, Order = o.Order, Text = o.Text })
            .ToList(),
    };

    /// <summary>Admin question view including the answer key.</summary>
    public static QuestionAdminDto ToAdminDto(this Question q) => new()
    {
        Id = q.Id,
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
            .Select(o => new AnswerOptionAdminDto { Id = o.Id, Order = o.Order, Text = o.Text, IsCorrect = o.IsCorrect })
            .ToList(),
    };

    public static QuestionResultDto ToResultDto(this Question q) => new()
    {
        QuestionId = q.Id,
        CorrectOptionId = q.Options.FirstOrDefault(o => o.IsCorrect)?.Id,
        CorrectAnswerText = q.Kind == QuestionKind.FreeText
            ? q.AcceptedFreeTextAnswer
            : q.Options.FirstOrDefault(o => o.IsCorrect)?.Text,
    };

    public static ScoreEntryDto ToDto(this ScoreEntry s) => new()
    {
        Id = s.Id,
        ParticipantId = s.ParticipantId,
        ParticipantName = s.Participant?.DisplayName ?? string.Empty,
        UserId = s.UserId,
        UserName = s.User?.Name,
        Round = s.Round,
        Points = s.Points,
        Note = s.Note,
        RecordedUtc = s.RecordedUtc,
    };
}
