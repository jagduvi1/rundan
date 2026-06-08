using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Shared;

namespace Rundan.Server.Services;

/// <summary>
/// Seeds the starting day: a pre-registered roster and one event ("Skärgårdsdagen") with the
/// eight planned activities — a quiz walk, catching/throwing games, fastest-time and tallest-tower
/// measured games, a closest-to-target timing game, a boule knockout and a word game. Teams are
/// pre-generated; results start empty so the host runs each activity during the day. No-op if data exists.
/// </summary>
public sealed class DataSeeder(AppDbContext db, TimeProvider clock)
{
    private static readonly string[] RosterNames =
        { "Malis", "Palle", "LillMonica", "Jimmy BK", "Maria", "Björn", "Malin", "Calle" };

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

        // 2) The day's event (teams of 2, placement scoring) with the whole roster.
        var ev = new Event
        {
            Name = "Skärgårdsdagen",
            Description = "En dag med åtta grenar. Ni byter lagkamrat inför varje gren — varje grens "
                          + "placering ger poäng (1:a = antal lag) till båda i laget. Högsta individuella total vinner!",
            TeamSize = 2,
            Scoring = EventScoring.Placement,
            JoinCode = "RUNDAN",
            CreatedUtc = now,
        };
        db.Events.Add(ev);
        await db.SaveChangesAsync(ct);

        db.EventMembers.AddRange(users.Select(u => new EventMember
        {
            EventId = ev.Id, UserId = u.Id, Token = Guid.NewGuid(),
            IsAdmin = u.Name == "Palle", // Palle is a co-host (event admin) to demo the role
            AddedUtc = now,
        }));
        await db.SaveChangesAsync(ct);

        var activities = new List<Activity>();

        // 1 — Poängpromenad (runs all day): 10 questions (1·X·2), random order.
        var walk = NewActivity(ev.Id, 1, ActivityType.Tipspromenad, "Poängpromenad", "PROM", now,
            "Pågår under hela dagen. Start i Husvik – avslut vid Utkiken. 1 poäng för varje rätt svar.",
            ActivityStatus.Live);
        walk.RandomizeQuestions = true;
        walk.IsPublic = true; // reusable from the library
        walk.Questions.Add(GeoMc(1, "Sveriges största sjö?", 59.3250, 18.1000, ("Vänern", true), ("Vättern", false), ("Mälaren", false)));
        walk.Questions.Add(GeoMc(2, "Hur många kommuner har Sverige?", 59.3256, 18.1012, ("90", false), ("290", true), ("490", false)));
        walk.Questions.Add(GeoMc(3, "Vilket är skärgårdens vanligaste träd?", 59.3262, 18.0994, ("Tall", true), ("Ek", false), ("Bok", false)));
        walk.Questions.Add(GeoMc(4, "Vad heter Sveriges nationaldjur (inofficiellt)?", 59.3268, 18.1006, ("Räv", false), ("Älg", true), ("Igelkott", false)));
        walk.Questions.Add(GeoMc(5, "Hur djup är Östersjön som mest (ca)?", 59.3245, 18.1018, ("160 m", false), ("330 m", false), ("459 m", true)));
        walk.Questions.Add(GeoMc(6, "Vilken färg får man av blått och gult?", 59.3239, 18.0998, ("Grön", true), ("Lila", false), ("Orange", false)));
        walk.Questions.Add(GeoMc(7, "Vad används en eka till?", 59.3271, 18.0985, ("Ro", true), ("Flyga", false), ("Gräva", false)));
        walk.Questions.Add(GeoMc(8, "Vilket år firade Sverige 500 år som nation? (ca)", 59.3233, 18.1024, ("1923", false), ("2023", true), ("1523-firande", false)));
        walk.Questions.Add(GeoMc(9, "Hur många ben har en spindel?", 59.3277, 18.1003, ("6", false), ("8", true), ("10", false)));
        walk.Questions.Add(GeoMc(10, "Vad heter Pippis häst?", 59.3228, 18.0991, ("Lilla Gubben", true), ("Herr Nilsson", false), ("Blixten", false)));

        // 2 — Marshmallowfångst: 1 p per caught marshmallow (15 per par).
        var marsh = NewActivity(ev.Id, 2, ActivityType.ScoreGame, "Marshmallowfångst", "MARSH", now,
            "Fånga så många marshmallows som möjligt i en skål eller kastrull. En person sitter med ryggen "
            + "mot kastaren som slungar marshmallows över axeln/huvudet. 15 marshmallows per par, 1 p per fångad.",
            ActivityStatus.Open);
        marsh.ScoreEntryMode = ScoreEntryMode.PerPlayer; // each catcher's catches add to the team
        marsh.IsPublic = true; // reusable from the library

        // 3 — Skala potatis: fastest time wins.
        var potato = NewActivity(ev.Id, 3, ActivityType.ScoreGame, "Skala potatis", "POTATIS", now,
            "Två och två – skala en potatis så snabbt som möjligt. En person håller potatisen, den andra "
            + "skalar. Varje person får bara använda en hand. Kortast tid vinner.",
            ActivityStatus.Open);
        potato.Measurement = Measurement.TimeSeconds;
        potato.ScoringMode = ScoringMode.LowerWins;

        // 4 — Boule (knockout): placement decides points. (Bracket engine pending.)
        var boule = NewActivity(ev.Id, 4, ActivityType.Boule, "Boule – utslagsspel", "BOULE", now,
            "Lagen möts i en utslagstävling. Appen lottar matcherna. Vinnarna går vidare till nästa omgång "
            + "och en final; förlorarna lottas in i ett förlorarträd. Placeringen avgör poängen.",
            ActivityStatus.Open);
        boule.CourtLabel = "Bana";
        boule.Courts.Add(new Court { Order = 1, Name = "Bana 1" });
        boule.Courts.Add(new Court { Order = 2, Name = "Bana 2" });

        // 5 — Bygga högst torn: tallest (mm) wins.
        var tower = NewActivity(ev.Id, 5, ActivityType.ScoreGame, "Bygga högst torn", "TORN", now,
            "Bygg det högsta tornet du kan på 4 minuter av saker du hittar utomhus. När tiden är ute: släpp "
            + "tornet och mät med tumstock. Högst torn vinner.",
            ActivityStatus.Open);
        tower.Measurement = Measurement.Millimetres;
        tower.ScoringMode = ScoringMode.HigherWins;

        // 6 — Cornhole Flight: sum of throws (rings worth 1/2/3).
        var corn = NewActivity(ev.Id, 6, ActivityType.ScoreGame, "Cornhole Flight", "CORN", now,
            "Kasta påsar i ringar gjorda av snöre på olika avstånd. Ringarna är märkta 1, 2 eller 3 poäng "
            + "beroende på avstånd. Plussa ihop poängen från kasten.",
            ActivityStatus.Open);
        corn.ScoreEntryMode = ScoreEntryMode.PerPlayer;

        // 7 — Gå ur ringen i rätt tid: closest to 2:27 (147 s).
        var ring = NewActivity(ev.Id, 7, ActivityType.ScoreGame, "Gå ur ringen i rätt tid", "RING", now,
            "Lämna ringen så nära 2 min 27 sek som möjligt. Gå in i ringen när aktiviteten startar och kliv "
            + "ut när du tror att exakt 2:27 har gått. Ingen klocka tillåten – känn på dig! Närmast rätt tid vinner.",
            ActivityStatus.Open);
        ring.Measurement = Measurement.TimeSeconds;
        ring.ScoringMode = ScoringMode.ClosestToTarget;
        ring.TargetValue = 147; // 2:27

        // 8 — Ordbygge med lappar: longest word wins. (Letter game pending.)
        var words = NewActivity(ev.Id, 8, ActivityType.WordGame, "Ordbygge med lappar", "ORD", now,
            "Bilda så långt ord som möjligt på 60 sekunder. Appen lottar 20 bokstavslappar upp och ner; ni "
            + "får vända upp 10 av dem. Längst ord vinner.",
            ActivityStatus.Open);

        words.IsPublic = true; // reusable from the library

        activities.AddRange([walk, marsh, potato, boule, tower, corn, ring, words]);
        db.Activities.AddRange(activities);
        await db.SaveChangesAsync(ct);

        // Pre-generate reshuffled teams for every activity (no results yet — the host runs them live).
        foreach (var a in activities)
        {
            MakeTeams(a, users, ev.TeamSize, now);
        }

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
        StartedUtc = status is ActivityStatus.Live or ActivityStatus.Finished ? now : null,
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
}
