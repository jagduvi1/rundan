namespace Rundan.Server;

/// <summary>Resolved on-disk locations (under the App Service persistent /home directory).</summary>
public sealed class StoragePaths
{
    /// <summary>Folder where admin-uploaded images are stored and served from at /uploads.</summary>
    public string UploadsDir { get; init; } = string.Empty;

    /// <summary>Best-effort delete of uploaded files by their public /uploads URLs — so removing an event
    /// or activity doesn't orphan its image + photo files on the persistent disk. Ignores blanks and IO
    /// errors (a leftover file is harmless; failing the delete over it is not).</summary>
    public void DeleteUploads(IEnumerable<string?> urls)
    {
        foreach (var url in urls)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            try
            {
                var full = Path.Combine(UploadsDir, Path.GetFileName(url));
                if (File.Exists(full))
                {
                    File.Delete(full);
                }
            }
            catch
            {
                // best-effort only
            }
        }
    }
}
