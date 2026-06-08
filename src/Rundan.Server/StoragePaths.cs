namespace Rundan.Server;

/// <summary>Resolved on-disk locations (under the App Service persistent /home directory).</summary>
public sealed class StoragePaths
{
    /// <summary>Folder where admin-uploaded images are stored and served from at /uploads.</summary>
    public string UploadsDir { get; init; } = string.Empty;
}
