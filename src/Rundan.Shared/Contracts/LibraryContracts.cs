namespace Rundan.Shared.Contracts;

/// <summary>Asks the library for N random questions matching the given tags.</summary>
public class LibraryGenerateRequest
{
    public int Count { get; set; } = 10;
    public List<string> Tags { get; set; } = new();
}

/// <summary>Result of pulling questions from the library.</summary>
public class LibraryGenerateResult
{
    public int Added { get; set; }

    /// <summary>How many matching, unused questions remain after this pull.</summary>
    public int Available { get; set; }
}
