using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Shared;

namespace Rundan.Server.Services;

/// <summary>
/// Seeds the pre-generated question library (reference data) when it's empty. Questions live in the
/// embedded <c>Data/question-library.json</c> file (1-X-2 multiple choice, tagged by topic, age and
/// difficulty) so the host can pull random ones into a quiz/tipspromenad. No runtime AI.
/// </summary>
public sealed class LibrarySeeder(AppDbContext db, ILogger<LibrarySeeder> logger)
{
    private const string ResourceName = "Rundan.Server.Data.question-library.json";

    public async Task<bool> SeedAsync(CancellationToken ct = default)
    {
        if (await db.QuestionTemplates.AnyAsync(ct))
        {
            return false;
        }

        var questions = LoadFromEmbeddedJson();
        if (questions.Count == 0)
        {
            logger.LogWarning("Question library JSON was empty or missing; no library questions seeded.");
            return false;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var q in questions)
        {
            if (string.IsNullOrWhiteSpace(q.Text) || q.Options is not { Count: 3 })
            {
                continue;
            }

            // Exactly one correct option (the 1-X-2 tradition); skip anything malformed.
            if (q.Options.Count(o => o.Correct) != 1)
            {
                continue;
            }

            // Dedupe by normalised question text so repeated facts across batches collapse.
            if (!seen.Add(Normalize(q.Text)))
            {
                continue;
            }

            Add(q);
            added++;

            // Keep the change-tracker from ballooning on a ~1000-row insert.
            if (added % 200 == 0)
            {
                await db.SaveChangesAsync(ct);
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} library questions.", added);
        return added > 0;
    }

    private List<LibraryQuestion> LoadFromEmbeddedJson()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            logger.LogWarning("Embedded resource {Resource} not found.", ResourceName);
            return new List<LibraryQuestion>();
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<List<LibraryQuestion>>(stream, options) ?? new List<LibraryQuestion>();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse the question library JSON.");
            return new List<LibraryQuestion>();
        }
    }

    private void Add(LibraryQuestion q)
    {
        var template = new QuestionTemplate { Text = q.Text.Trim(), Kind = QuestionKind.MultipleChoice, Points = 1 };
        var i = 0;
        foreach (var opt in q.Options)
        {
            template.Options.Add(new QuestionTemplateOption { Order = i++, Text = opt.Text.Trim(), IsCorrect = opt.Correct });
        }

        foreach (var tag in (q.Tags ?? new()).Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            template.Tags.Add(new QuestionTemplateTag { Tag = tag.Trim().ToLowerInvariant() });
        }

        db.QuestionTemplates.Add(template);
    }

    private static string Normalize(string text) =>
        new string(text.Where(c => !char.IsWhiteSpace(c) && !char.IsPunctuation(c)).ToArray()).ToLowerInvariant();

    // JSON shape of one library question (matches the generated Data/question-library.json).
    private sealed class LibraryQuestion
    {
        public string Text { get; set; } = string.Empty;
        public List<LibraryOption> Options { get; set; } = new();
        public List<string>? Tags { get; set; }
    }

    private sealed class LibraryOption
    {
        public string Text { get; set; } = string.Empty;
        public bool Correct { get; set; }
    }
}
