using Rundan.Server.Data.Entities;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Services;

/// <summary>
/// Builds the multiple-choice artist options for a Kahoot-style ("pick the artist") music quiz.
/// Wrong answers come from the other artists in the same quiz first, then a built-in pool of
/// well-known acts so a small quiz still has four plausible choices. Deterministic per track (seeded
/// by the question id) so the options + their order are stable across reloads and identical for
/// everyone. The correct artist is never flagged here — the option text is matched server-side.
/// </summary>
public static class MusicChoices
{
    // A spread of widely-known artists across eras/genres, used to fill out the wrong options when a
    // quiz doesn't have enough of its own artists.
    private static readonly string[] Pool =
    {
        "ABBA", "Queen", "The Beatles", "Madonna", "Michael Jackson", "Elton John", "David Bowie",
        "U2", "Coldplay", "Adele", "Beyoncé", "Rihanna", "Taylor Swift", "Ed Sheeran", "Bruno Mars",
        "Drake", "Eminem", "Kanye West", "Lady Gaga", "Katy Perry", "Justin Bieber", "Maroon 5",
        "Bob Dylan", "Bruce Springsteen", "Prince", "Whitney Houston", "Mariah Carey", "Stevie Wonder",
        "Nirvana", "Red Hot Chili Peppers", "Metallica", "AC/DC", "Pink Floyd", "Led Zeppelin",
        "The Rolling Stones", "Fleetwood Mac", "Daft Punk", "The Weeknd", "Dua Lipa", "Billie Eilish",
        "Avicii", "Robyn", "Roxette", "Kent", "Veronica Maggio", "Håkan Hellström",
    };

    public static void Populate(List<QuestionDto> dtos, List<Question> questions)
    {
        var quizArtists = questions
            .Select(q => (q.AcceptedArtist ?? string.Empty).Trim())
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var q in questions)
        {
            var correct = (q.AcceptedArtist ?? string.Empty).Trim();
            if (correct.Length == 0)
            {
                continue; // an artist-less track stays a typed track (the player UI falls back)
            }

            var dto = dtos.FirstOrDefault(d => d.Id == q.Id);
            if (dto is null)
            {
                continue;
            }

            var rng = new Random(q.Id);
            var distractors = quizArtists
                .Where(a => !a.Equals(correct, StringComparison.OrdinalIgnoreCase))
                .Concat(Pool.Where(p => !quizArtists.Contains(p, StringComparer.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(_ => rng.Next())
                .Take(3)
                .ToList();

            dto.Options = distractors.Append(correct)
                .OrderBy(_ => rng.Next())
                .Select((text, i) => new AnswerOptionDto { Id = i, Order = i, Text = text })
                .ToList();
        }
    }
}
