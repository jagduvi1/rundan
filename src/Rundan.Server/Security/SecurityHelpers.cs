using System.Security.Cryptography;
using System.Text;

namespace Rundan.Server.Security;

public static class SecurityHelpers
{
    /// <summary>
    /// Constant-time comparison of secret codes. Both inputs are hashed to a fixed-size
    /// digest first, so neither the length nor the contents of the expected secret leak
    /// through timing (a raw FixedTimeEquals short-circuits on a length mismatch).
    /// </summary>
    public static bool FixedEquals(string? provided, string? expected)
    {
        if (provided is null || expected is null)
        {
            return false;
        }

        Span<byte> a = stackalloc byte[32];
        Span<byte> b = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(provided), a);
        SHA256.HashData(Encoding.UTF8.GetBytes(expected), b);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
