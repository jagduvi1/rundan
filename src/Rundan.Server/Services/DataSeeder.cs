using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Shared;

namespace Rundan.Server.Services;

/// <summary>
/// Seeds sample data for testing: a pre-registered roster, one event (team size 2) with
/// a Tipspromenad, a Quiz and a Boule game, auto-generated reshuffled teams, and team
/// results so the per-player combined standings is already populated. No-op if data exists.
/// </summary>
public sealed class DataSeeder(AppDbContext db, TimeProvider clock)
{
    private static readonly string[] RosterNames =
        { "Anna", "Erik", "Johan", "Sara", "Maja", "Olof", "Petra", "Sven" };

    public async Task<bool> SeedAsync(CancellationToken ct = default)
    {
        if (await db.Events.AnyAsync(ct) || await db.Activities.AnyAsync(ct) || await db.Users.AnyAsync(ct))
        {
            return false;
        }

        var now = clock.GetUtcNow();

        // 1) Global roster.
        var users = RosterNames.Select(n => new User { Name = n, CreatedUtc = now }).ToList();
        db.Users.AddRange(users);
        await db.SaveChangesAsync(ct);

        // 2) Event (team size 2) with the whole roster as members.
        var ev = new Event
        {
            Name = "Sommarfesten på bryggan",
            Description = "Lagtävling i tre grenar. Ni får ny lagkamrat inför varje gren — "
                          + "varje grens placering ger poäng (1:a = antal lag) till båda i laget. Högsta individuella total vinner!",
            TeamSize = 2,
            Scoring = EventScoring.Placement,
            JoinCode = "SOMMAR",
            CreatedUtc = now,
        };
        db.Events.Add(ev);
        await db.SaveChangesAsync(ct);

        db.EventMembers.AddRange(users.Select(u => new EventMember
        {
            EventId = ev.Id, UserId = u.Id, Token = Guid.NewGuid(),
            IsAdmin = u.Name == "Erik", // Erik is a co-host (event admin) to demo the role
            AddedUtc = now,
        }));
        await db.SaveChangesAsync(ct);

        // 3) Activities.
        var walk = NewActivity(ev.Id, 1, ActivityType.Tipspromenad, "Skärgårdsrundan", "WALK", now,
            "Gå laget runt till varje station. När ni är inom 30 m dyker frågan upp på telefonen.", ActivityStatus.Finished);
        walk.Questions.Add(GeoMc(1, "Vilket träd dominerar i skärgården?", 59.3251, 18.1000,
            ("Tall · Pine", true), ("Ek · Oak", false), ("Bok · Beech", false)));
        walk.Questions.Add(GeoMc(2, "Sveriges inofficiella nationaldjur?", 59.3258, 18.1016,
            ("Älg · Moose", true), ("Räv · Fox", false), ("Igelkott · Hedgehog", false)));
        walk.Questions.Add(GeoMc(3, "Vilken fågel hörs ofta vid bryggan?", 59.3262, 18.0989,
            ("Mås · Gull", true), ("Uggla · Owl", false), ("Örn · Eagle", false)));
        walk.Questions.Add(GeoMc(4, "Vad används en eka till?", 59.3244, 18.1009,
            ("Ro · Rowing", true), ("Flyga · Flying", false), ("Gräva · Digging", false)));

        var quiz = NewActivity(ev.Id, 2, ActivityType.Quiz, "Kvällsquizet", "QUIZ", now,
            "Fem frågor. En i taget — nästa fråga dyker upp när ni svarat.", ActivityStatus.Finished);
        quiz.Questions.Add(Mc(1, "Vad är Sveriges huvudstad?", ("Stockholm", true), ("Göteborg", false), ("Malmö", false), ("Oslo", false)));
        quiz.Questions.Add(Mc(2, "Hur många ben har en spindel?", ("8", true), ("6", false), ("10", false), ("4", false)));
        quiz.Questions.Add(Mc(3, "Blå och gul blir?", ("Grön · Green", true), ("Lila · Purple", false), ("Orange", false)));
        quiz.Questions.Add(Mc(4, "Vad heter Pippi Långstrumps häst?", ("Lilla Gubben", true), ("Herr Nilsson", false), ("Blixten", false)));
        quiz.Questions.Add(Mc(5, "Vilket hav ligger öster om Sverige?", ("Östersjön · Baltic", true), ("Atlanten", false), ("Medelhavet", false)));

        var boule = NewActivity(ev.Id, 3, ActivityType.Boule, "Boulekulan", "BOULE", now,
            "Bäst av flera omgångar. Närmast lillen vinner omgången.", ActivityStatus.Live);

        db.Activities.AddRange(walk, quiz, boule);
        await db.SaveChangesAsync(ct);

        // 4) Teams per activity (reshuffled), and team results.
        var walkTeams = MakeTeams(walk, users, ev.TeamSize, now);
        var quizTeams = MakeTeams(quiz, users, ev.TeamSize, now);
        var bouleTeams = MakeTeams(boule, users, ev.TeamSize, now);
        await db.SaveChangesAsync(ct);

        // Distinct results so the four teams rank 1-2-3-4 in each finished activity.
        SeedTeamAnswers(walk, walkTeams, new[] { 4, 3, 2, 1 }, now);
        SeedTeamAnswers(quiz, quizTeams, new[] { 5, 4, 3, 2 }, now);
        // Boule is still live (not yet counted in placement) — distinct team scores.
        AddTeamScore(boule.Id, bouleTeams[0], now, 13);
        AddTeamScore(boule.Id, bouleTeams[1], now, 10);
        AddTeamScore(boule.Id, bouleTeams[2], now, 7);
        AddTeamScore(boule.Id, bouleTeams[3], now, 4);

        await db.SaveChangesAsync(ct);
        return true;
    }

    private Activity NewActivity(int eventId, int order, ActivityType type, string title, string code,
        DateTimeOffset now, string rules, ActivityStatus status) => new()
    {
        EventId = eventId,
        Order = order,
        Type = type,
        Title = title,
        Description = rules,
        JoinCode = code,
        ScoringMode = ScoringMode.HigherWins,
        Status = status,
        CreatedUtc = now,
        StartedUtc = now,
        FinishedUtc = status == ActivityStatus.Finished ? now : null,
    };

    private static Question Mc(int order, string text, params (string Text, bool Correct)[] options)
    {
        var q = new Question { Order = order, Text = text, Kind = QuestionKind.MultipleChoice, Points = 1 };
        var i = 0;
        foreach (var (t, c) in options)
        {
            q.Options.Add(new AnswerOption { Order = i++, Text = t, IsCorrect = c });
        }

        return q;
    }

    private static Question GeoMc(int order, string text, double lat, double lng, params (string Text, bool Correct)[] options)
    {
        var q = Mc(order, text, options);
        q.Latitude = lat;
        q.Longitude = lng;
        q.RadiusMeters = 30;
        return q;
    }

    private List<Participant> MakeTeams(Activity activity, IReadOnlyList<User> users, int teamSize, DateTimeOffset now)
    {
        var groups = PartnerMixer.MakeTeams(users.OrderBy(u => u.Name).ToList(), teamSize, activity.Order);
        var teams = new List<Participant>();
        foreach (var group in groups)
        {
            var team = new Participant
            {
                ActivityId = activity.Id,
                DisplayName = string.Join(" & ", group.Select(u => u.Name)),
                IsTeam = true,
                Token = Guid.NewGuid(),
                JoinedUtc = now,
                Members = group.Select(u => new ParticipantMember { UserId = u.Id }).ToList(),
            };
            db.Participants.Add(team);
            teams.Add(team);
        }

        return teams;
    }

    private void SeedTeamAnswers(Activity activity, List<Participant> teams, int[] correctCounts, DateTimeOffset now)
    {
        var questions = activity.Questions.OrderBy(q => q.Order).ToList();
        for (var t = 0; t < teams.Count && t < correctCounts.Length; t++)
        {
            foreach (var q in questions.Take(correctCounts[t]))
            {
                var correct = q.Options.First(o => o.IsCorrect);
                db.Answers.Add(new Answer
                {
                    QuestionId = q.Id,
                    ParticipantId = teams[t].Id,
                    SelectedOptionId = correct.Id,
                    IsCorrect = true,
                    AwardedPoints = q.Points,
                    SubmittedUtc = now,
                });
            }
        }
    }

    private void AddTeamScore(int activityId, Participant team, DateTimeOffset now, params int[] rounds)
    {
        var round = 1;
        foreach (var points in rounds)
        {
            db.ScoreEntries.Add(new ScoreEntry
            {
                ActivityId = activityId, ParticipantId = team.Id, Round = round++, Points = points, RecordedUtc = now,
            });
        }
    }
}
