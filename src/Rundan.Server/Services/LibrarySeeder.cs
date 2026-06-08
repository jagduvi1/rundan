using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Shared;

namespace Rundan.Server.Services;

/// <summary>
/// Seeds the pre-generated question library (reference data) when it's empty. Questions are tagged
/// by age and topic so the host can pull random ones into a quiz/tipspromenad. No runtime AI.
/// </summary>
public sealed class LibrarySeeder(AppDbContext db)
{
    public async Task<bool> SeedAsync(CancellationToken ct = default)
    {
        if (await db.QuestionTemplates.AnyAsync(ct))
        {
            return false;
        }

        // Kids
        Add("Vilken färg har en banan?", "age:kids,topic:nature,difficulty:easy", ("Gul", true), ("Blå", false), ("Röd", false));
        Add("Hur många ben har en hund?", "age:kids,topic:nature,difficulty:easy", ("4", true), ("2", false), ("6", false));
        Add("Vad säger en ko?", "age:kids,topic:nature,difficulty:easy", ("Mu", true), ("Mjau", false), ("Voff", false));
        Add("Vilket djur är störst?", "age:kids,topic:nature,difficulty:easy", ("Elefant", true), ("Mus", false), ("Katt", false));
        Add("Vad blir 2 + 2?", "age:kids,topic:science,difficulty:easy", ("4", true), ("3", false), ("5", false));
        Add("Vilken frukt är röd och växer på träd?", "age:kids,topic:nature,difficulty:easy", ("Äpple", true), ("Banan", false), ("Citron", false));
        Add("Hur många dagar har en vecka?", "age:kids,topic:science,difficulty:easy", ("7", true), ("5", false), ("10", false));
        Add("Vilket är minst av 7, 3 och 9?", "age:kids,topic:science,difficulty:easy", ("3", true), ("7", false), ("9", false));

        // Family / general
        Add("Vad heter Sveriges huvudstad?", "age:family,topic:sweden,topic:geography,difficulty:easy", ("Stockholm", true), ("Göteborg", false), ("Malmö", false));
        Add("Vilken färg får man av blått och gult?", "age:family,topic:science,difficulty:easy", ("Grön", true), ("Lila", false), ("Orange", false));
        Add("Vilket är världens största hav?", "age:family,topic:geography", ("Stilla havet", true), ("Atlanten", false), ("Indiska oceanen", false));
        Add("Vilket land har formen av en stövel?", "age:family,topic:geography", ("Italien", true), ("Spanien", false), ("Grekland", false));
        Add("Hur många ben har en spindel?", "age:family,topic:nature", ("8", true), ("6", false), ("10", false));
        Add("Vad heter Pippi Långstrumps apa?", "age:family,topic:film", ("Herr Nilsson", true), ("Lilla Gubben", false), ("Goliat", false));
        Add("Vilken planet kallas den röda planeten?", "age:family,topic:science", ("Mars", true), ("Venus", false), ("Jupiter", false));
        Add("Vilket hav ligger öster om Sverige?", "age:family,topic:sweden,topic:geography", ("Östersjön", true), ("Nordsjön", false), ("Atlanten", false));
        Add("Hur många strängar har en vanlig gitarr?", "age:family,topic:music", ("6", true), ("4", false), ("8", false));
        Add("Vilken stad är Italiens huvudstad?", "age:family,topic:geography", ("Rom", true), ("Milano", false), ("Venedig", false));
        Add("Hur många spelare har ett fotbollslag på planen?", "age:family,topic:sport", ("11", true), ("10", false), ("9", false));
        Add("Vad heter Sveriges största sjö?", "age:family,topic:sweden,topic:geography", ("Vänern", true), ("Vättern", false), ("Mälaren", false));
        Add("Hur många kontinenter finns det?", "age:family,topic:geography", ("7", true), ("5", false), ("6", false));
        Add("Vilket är det snabbaste landlevande djuret?", "age:family,topic:nature", ("Gepard", true), ("Lejon", false), ("Häst", false));
        Add("Vid hur många grader Celsius fryser vatten?", "age:family,topic:science", ("0", true), ("100", false), ("32", false));
        Add("Hur många minuter är en fotbollsmatch (ordinarie tid)?", "age:family,topic:sport", ("90", true), ("60", false), ("120", false));
        Add("Vad heter huvudpersonen i Bröderna Lejonhjärta?", "age:family,topic:film", ("Skorpan", true), ("Emil", false), ("Madicken", false));

        // Adults / harder
        Add("Vilket år föll Berlinmuren?", "age:adults,topic:history,difficulty:hard", ("1989", true), ("1991", false), ("1985", false));
        Add("Vem målade Mona Lisa?", "age:adults,topic:history", ("Leonardo da Vinci", true), ("Picasso", false), ("Michelangelo", false));
        Add("Vad är Sveriges högsta berg?", "age:adults,topic:sweden,topic:geography,difficulty:hard", ("Kebnekaise", true), ("Sarek", false), ("Åreskutan", false));
        Add("Vilket grundämne har beteckningen Au?", "age:adults,topic:science,difficulty:hard", ("Guld", true), ("Silver", false), ("Aluminium", false));
        Add("I vilket land hölls sommar-OS 2016?", "age:adults,topic:sport,difficulty:hard", ("Brasilien", true), ("Kina", false), ("Storbritannien", false));
        Add("Vem skrev Hamlet?", "age:adults,topic:history", ("Shakespeare", true), ("Dickens", false), ("Tolstoj", false));
        Add("Vilket är Sveriges till ytan största landskap?", "age:adults,topic:sweden,difficulty:hard", ("Lappland", true), ("Skåne", false), ("Dalarna", false));
        Add("Vilket band sjöng Dancing Queen?", "age:adults,topic:music", ("ABBA", true), ("Roxette", false), ("Kent", false));
        Add("Vilken svensk artist vann Eurovision med Euphoria?", "age:adults,topic:music,topic:sweden", ("Loreen", true), ("Zara Larsson", false), ("Robyn", false));
        Add("Vilket land har flest invånare?", "age:adults,topic:geography,difficulty:hard", ("Indien", true), ("Kina", false), ("USA", false));

        await db.SaveChangesAsync(ct);
        return true;
    }

    private void Add(string text, string tags, params (string Text, bool Correct)[] options)
    {
        var template = new QuestionTemplate { Text = text, Kind = QuestionKind.MultipleChoice, Points = 1 };
        var i = 0;
        foreach (var (t, correct) in options)
        {
            template.Options.Add(new QuestionTemplateOption { Order = i++, Text = t, IsCorrect = correct });
        }

        foreach (var tag in tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            template.Tags.Add(new QuestionTemplateTag { Tag = tag.ToLowerInvariant() });
        }

        db.QuestionTemplates.Add(template);
    }
}
