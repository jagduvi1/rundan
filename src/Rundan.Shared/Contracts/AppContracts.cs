namespace Rundan.Shared.Contracts;

/// <summary>Public app info the client fetches before the access-code gate.</summary>
public class BootstrapDto
{
    public string AppName { get; set; } = "Rundan";
    public bool RequiresAccessCode { get; set; }
    public bool RequiresAdminCode { get; set; }
}

/// <summary>Confirmation codes for destructive host actions. Hardcoded, not secrets — a deliberate
/// barrier against accidental/casual use; for real protection, configure an admin code instead.</summary>
public static class SeedCodes
{
    public const string CleanAndSeed = "CALLE";
}

/// <summary>Body for the clean-and-seed wipe: the host's typed confirmation code.</summary>
public class CleanAndSeedRequest
{
    public string? Code { get; set; }
}
