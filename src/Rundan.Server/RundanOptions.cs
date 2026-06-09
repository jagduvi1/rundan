namespace Rundan.Server;

/// <summary>
/// App configuration, bound from the "Rundan" section of configuration
/// (appsettings.json / environment variables / App Service settings).
/// </summary>
public class RundanOptions
{
    public const string SectionName = "Rundan";

    /// <summary>Friendly app name shown in the UI.</summary>
    public string AppName { get; set; } = "Rundan";

    /// <summary>
    /// Shared site password. If set, every API/SignalR call must present it
    /// (header <c>X-Rundan-Access</c> or SignalR access token). Empty = open (dev only).
    /// </summary>
    public string? AccessCode { get; set; }

    /// <summary>
    /// Admin password required to create/manage activities. Empty = anyone may
    /// administer (dev only).
    /// </summary>
    public string? AdminCode { get; set; }

    /// <summary>
    /// Optional explicit path to the SQLite file. If empty, the path is derived
    /// from the App Service persistent <c>HOME</c> directory.
    /// </summary>
    public string? DatabasePath { get; set; }

    /// <summary>If true, populates sample data (one event with several activities) on startup when the database is empty.</summary>
    public bool SeedOnStartup { get; set; }

    /// <summary>Confirmation code the host types to run the destructive clean-and-seed wipe. Defaults
    /// to "CALLE"; override it (e.g. <c>Rundan__SeedCode</c>) for a real secret that isn't in the source.</summary>
    public string SeedCode { get; set; } = "CALLE";

    public bool RequiresAccessCode => !string.IsNullOrWhiteSpace(AccessCode);
    public bool RequiresAdminCode => !string.IsNullOrWhiteSpace(AdminCode);
}
