using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;
using Rundan.Server.Data.Entities;
using Rundan.Shared.Contracts;

namespace Rundan.Server.Services;

/// <summary>
/// The word game: each team gets a fixed set of 20 letter tiles (deterministic from the
/// participant id, so it's stable across reloads), may open up to 10, and submits the
/// longest word they can build from the opened letters within the time limit. The word's
/// length is the team's score (highest wins) and is stored as a normal score line.
/// </summary>
public sealed class WordGameService(AppDbContext db, TimeProvider clock)
{
    public const int TileCount = 20;
    public const int MaxOpen = 10;
    public const int Seconds = 60;

    // Vowel-heavy bag so words are formable; weighting by repetition.
    private const string Bag = "AAAAAAEEEEEEEIIIIIOOOOOUUUNNNNTTTTRRRRSSSSLLLLDDDGGGBBCCMMPPFFHHVVKJ";

    public List<string> Tiles(int participantId)
    {
        var rng = new Random(participantId);
        var tiles = new List<string>(TileCount);
        for (var i = 0; i < TileCount; i++)
        {
            tiles.Add(Bag[rng.Next(Bag.Length)].ToString());
        }

        return tiles;
    }

    public async Task<WordGameDto> GetAsync(Participant participant, CancellationToken ct = default)
    {
        var latest = await db.ScoreEntries.AsNoTracking()
            .Where(s => s.ActivityId == participant.ActivityId && s.ParticipantId == participant.Id)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        return new WordGameDto
        {
            Tiles = Tiles(participant.Id),
            MaxOpen = MaxOpen,
            Seconds = Seconds,
            SubmittedWord = latest?.Note,
            SubmittedScore = latest is null ? null : latest.Points,
        };
    }

    public async Task<WordGameDto> SubmitAsync(Participant participant, SubmitWordRequest req, CancellationToken ct = default)
    {
        var tiles = Tiles(participant.Id);

        // The letters the team is allowed to use: the (≤10) distinct opened tiles.
        var opened = req.OpenedIndices
            .Where(i => i >= 0 && i < TileCount)
            .Distinct()
            .Take(MaxOpen)
            .Select(i => tiles[i][0])
            .ToList();

        var word = (req.Word ?? string.Empty).Trim().ToUpperInvariant();
        if (word.Length == 0)
        {
            throw new RuleViolationException("Enter a word.");
        }

        // Every letter of the word must come from an opened tile (each tile used once).
        var available = opened.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
        foreach (var c in word)
        {
            if (!available.TryGetValue(c, out var n) || n == 0)
            {
                throw new RuleViolationException("Your word uses letters you haven't opened.");
            }

            available[c] = n - 1;
        }

        // One submission per team — replace any earlier word.
        db.ScoreEntries.RemoveRange(db.ScoreEntries
            .Where(s => s.ActivityId == participant.ActivityId && s.ParticipantId == participant.Id));
        db.ScoreEntries.Add(new ScoreEntry
        {
            ActivityId = participant.ActivityId,
            ParticipantId = participant.Id,
            Round = 1,
            Points = word.Length,
            Note = word,
            RecordedUtc = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);

        return new WordGameDto
        {
            Tiles = tiles,
            MaxOpen = MaxOpen,
            Seconds = Seconds,
            SubmittedWord = word,
            SubmittedScore = word.Length,
        };
    }
}
