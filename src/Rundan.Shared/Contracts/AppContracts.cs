namespace Rundan.Shared.Contracts;

/// <summary>Public app info the client fetches before the access-code gate.</summary>
public class BootstrapDto
{
    public string AppName { get; set; } = "Rundan";
    public bool RequiresAccessCode { get; set; }
    public bool RequiresAdminCode { get; set; }
}

/// <summary>Body for the clean-and-seed wipe: the host's typed confirmation code (validated server-side).</summary>
public class CleanAndSeedRequest
{
    public string? Code { get; set; }
}
