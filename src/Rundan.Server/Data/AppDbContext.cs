using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data.Entities;

namespace Rundan.Server.Data;

/// <summary>
/// The single EF Core context, backed by one SQLite file on the App Service
/// persistent disk. SQLite is single-writer, which is exactly why the app runs on a
/// single App Service instance (no scale-out).
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventMember> EventMembers => Set<EventMember>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<ParticipantMember> ParticipantMembers => Set<ParticipantMember>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<AnswerOption> AnswerOptions => Set<AnswerOption>();
    public DbSet<Answer> Answers => Set<Answer>();
    public DbSet<ScoreEntry> ScoreEntries => Set<ScoreEntry>();
    public DbSet<MapCity> MapCities => Set<MapCity>();
    public DbSet<BracketMatch> BracketMatches => Set<BracketMatch>();
    public DbSet<Court> Courts => Set<Court>();
    public DbSet<EventViewer> EventViewers => Set<EventViewer>();
    public DbSet<Slap> Slaps => Set<Slap>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ActivityPhoto> ActivityPhotos => Set<ActivityPhoto>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<MemoryCard> MemoryCards => Set<MemoryCard>();
    public DbSet<QuestionTemplate> QuestionTemplates => Set<QuestionTemplate>();
    public DbSet<QuestionTemplateOption> QuestionTemplateOptions => Set<QuestionTemplateOption>();
    public DbSet<QuestionTemplateTag> QuestionTemplateTags => Set<QuestionTemplateTag>();
    public DbSet<QuestionTemplateUsage> QuestionTemplateUsages => Set<QuestionTemplateUsage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<User>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<Event>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.ImageUrl).HasMaxLength(500);
            e.Property(x => x.JoinCode).HasMaxLength(16).IsRequired();
            e.HasIndex(x => x.JoinCode).IsUnique();
        });

        b.Entity<EventMember>(e =>
        {
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => new { x.EventId, x.UserId }).IsUnique();
            e.HasOne(x => x.Event).WithMany(ev => ev.Members)
                .HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany(u => u.EventMemberships)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ParticipantMember>(e =>
        {
            e.HasIndex(x => new { x.ParticipantId, x.UserId }).IsUnique();
            e.HasOne(x => x.Participant).WithMany(p => p.Members)
                .HasForeignKey(x => x.ParticipantId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Activity>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.ImageUrl).HasMaxLength(500);
            e.Property(x => x.JoinCode).HasMaxLength(16).IsRequired();
            e.HasIndex(x => x.JoinCode).IsUnique();
            e.HasIndex(x => new { x.EventId, x.Order });
            e.HasOne(x => x.Event)
                .WithMany(ev => ev.Activities)
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Participant>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(60).IsRequired();
            e.HasIndex(x => x.Token).IsUnique();
            // A name is unique within one activity (prevents two "Anna"s confusing the scoreboard).
            e.HasIndex(x => new { x.ActivityId, x.DisplayName }).IsUnique();
            e.HasOne(x => x.Activity)
                .WithMany(a => a.Participants)
                .HasForeignKey(x => x.ActivityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Question>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(1000).IsRequired();
            e.Property(x => x.ImageUrl).HasMaxLength(500);
            e.Property(x => x.AcceptedFreeTextAnswer).HasMaxLength(200);
            e.Property(x => x.SpotifyUrl).HasMaxLength(500);
            e.Property(x => x.AcceptedArtist).HasMaxLength(200);
            e.HasIndex(x => new { x.ActivityId, x.Order });
            e.HasOne(x => x.Activity)
                .WithMany(a => a.Questions)
                .HasForeignKey(x => x.ActivityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AnswerOption>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(300).IsRequired();
            e.HasOne(x => x.Question)
                .WithMany(q => q.Options)
                .HasForeignKey(x => x.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Answer>(e =>
        {
            e.Property(x => x.FreeText).HasMaxLength(500);
            e.Property(x => x.ArtistText).HasMaxLength(500);
            // One answer per participant per question.
            e.HasIndex(x => new { x.QuestionId, x.ParticipantId }).IsUnique();

            e.HasOne(x => x.Question)
                .WithMany(q => q.Answers)
                .HasForeignKey(x => x.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Participant)
                .WithMany(p => p.Answers)
                .HasForeignKey(x => x.ParticipantId)
                .OnDelete(DeleteBehavior.Cascade);

            // The selected option lives under the same question (which cascades the answer
            // away anyway). Use NoAction here to avoid a second cascade path to Answer;
            // question editing is only allowed before anyone answers (Draft status).
            e.HasOne(x => x.SelectedOption)
                .WithMany()
                .HasForeignKey(x => x.SelectedOptionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<ScoreEntry>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasIndex(x => new { x.ActivityId, x.ParticipantId });

            e.HasOne(x => x.Activity)
                .WithMany(a => a.ScoreEntries)
                .HasForeignKey(x => x.ActivityId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Participant)
                .WithMany(p => p.ScoreEntries)
                .HasForeignKey(x => x.ParticipantId)
                .OnDelete(DeleteBehavior.Cascade);

            // Per-player attribution (team scoring). Keep the entry if the user is removed.
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<MapCity>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasIndex(x => new { x.ActivityId, x.Order });
            e.HasOne(x => x.Activity)
                .WithMany(a => a.MapCities)
                .HasForeignKey(x => x.ActivityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<EventViewer>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.EventId);
            e.HasOne(x => x.Event)
                .WithMany()
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<QuestionTemplate>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(1000).IsRequired();
            e.Property(x => x.AcceptedFreeTextAnswer).HasMaxLength(200);
        });

        b.Entity<QuestionTemplateOption>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(300).IsRequired();
            e.HasOne(x => x.QuestionTemplate).WithMany(t => t.Options)
                .HasForeignKey(x => x.QuestionTemplateId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<QuestionTemplateTag>(e =>
        {
            e.Property(x => x.Tag).HasMaxLength(60).IsRequired();
            e.HasIndex(x => x.Tag);
            e.HasOne(x => x.QuestionTemplate).WithMany(t => t.Tags)
                .HasForeignKey(x => x.QuestionTemplateId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<QuestionTemplateUsage>(e =>
        {
            e.HasIndex(x => x.QuestionTemplateId).IsUnique();
        });

        b.Entity<Slap>(e =>
        {
            e.HasIndex(x => x.ActivityId).IsUnique(); // at most one slap per activity
            e.HasOne(x => x.Event)
                .WithMany()
                .HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ChatMessage>(e =>
        {
            e.Property(x => x.Author).HasMaxLength(60).IsRequired();
            e.Property(x => x.Text).HasMaxLength(1000).IsRequired();
            e.HasIndex(x => new { x.EventId, x.Id });
            e.HasOne(x => x.Event).WithMany()
                .HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ActivityPhoto>(e =>
        {
            e.Property(x => x.Author).HasMaxLength(60).IsRequired();
            e.Property(x => x.Url).HasMaxLength(500).IsRequired();
            e.HasIndex(x => new { x.ActivityId, x.Id });
            e.HasOne(x => x.Activity).WithMany()
                .HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MemoryCard>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(120).IsRequired();
            e.HasIndex(x => new { x.ActivityId, x.Order });
            e.HasOne(x => x.Activity).WithMany()
                .HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PushSubscription>(e =>
        {
            e.Property(x => x.Endpoint).HasMaxLength(800).IsRequired();
            e.Property(x => x.P256dh).HasMaxLength(200).IsRequired();
            e.Property(x => x.Auth).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Endpoint).IsUnique();
            e.HasIndex(x => x.EventId);
            e.HasOne(x => x.Event).WithMany()
                .HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Court>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.HasIndex(x => new { x.ActivityId, x.Order });
            e.HasOne(x => x.Activity)
                .WithMany(a => a.Courts)
                .HasForeignKey(x => x.ActivityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BracketMatch>(e =>
        {
            e.HasIndex(x => new { x.ActivityId, x.Side, x.Round, x.Slot });
            e.HasOne(x => x.Activity)
                .WithMany()
                .HasForeignKey(x => x.ActivityId)
                .OnDelete(DeleteBehavior.Cascade);

            // Court assignment — cleared manually before a court is removed (avoids a 2nd cascade path).
            e.HasOne(x => x.Court)
                .WithMany()
                .HasForeignKey(x => x.CourtId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
