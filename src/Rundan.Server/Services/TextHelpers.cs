namespace Rundan.Server.Services;

/// <summary>Small shared text helpers used across endpoints.</summary>
internal static class TextHelpers
{
    /// <summary>Trims a string, returning null when it's null/blank (so empties don't persist as "").</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
