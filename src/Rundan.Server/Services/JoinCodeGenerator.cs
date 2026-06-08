using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Rundan.Server.Data;

namespace Rundan.Server.Services;

/// <summary>
/// Generates short, human-typable, unambiguous join codes like "FQK-723".
/// Excludes easily-confused characters (no O/0, I/1).
/// </summary>
public sealed class JoinCodeGenerator
{
    private const string Letters = "ABCDEFGHJKLMNPQRSTUVWXYZ"; // no I, O
    private const string Digits = "23456789";                  // no 0, 1

    /// <summary>Returns a join code not currently used by any activity.</summary>
    public async Task<string> NextAsync(AppDbContext db, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var code = Generate();
            var exists = await db.Activities.AnyAsync(a => a.JoinCode == code, ct)
                         || await db.Events.AnyAsync(e => e.JoinCode == code, ct);
            if (!exists)
            {
                return code;
            }
        }

        // Astronomically unlikely with a tiny friend group; fall back to a longer code.
        return Generate() + "-" + Generate();
    }

    private static string Generate()
    {
        Span<char> buffer = stackalloc char[7];
        buffer[0] = Letters[RandomNumberGenerator.GetInt32(Letters.Length)];
        buffer[1] = Letters[RandomNumberGenerator.GetInt32(Letters.Length)];
        buffer[2] = Letters[RandomNumberGenerator.GetInt32(Letters.Length)];
        buffer[3] = '-';
        buffer[4] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        buffer[5] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        buffer[6] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        return new string(buffer);
    }
}
