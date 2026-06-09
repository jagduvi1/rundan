using Microsoft.EntityFrameworkCore;
using Rundan.Server;
using Rundan.Server.Data;

namespace Rundan.Server.Services;

/// <summary>
/// Destructive admin maintenance: wipe every row and re-seed the demo day (the host "clean &amp;
/// seed" button). The whole wipe + reseed runs inside one transaction so a failure rolls back
/// rather than leaving the DB half-emptied — which the empty-guarded seeders could never self-heal.
/// A rolling backup copy of the SQLite file is taken first so an accidental wipe is recoverable.
/// Rows are deleted in the same file (the file is never dropped) so live connections keep working.
/// </summary>
public sealed class MaintenanceService(AppDbContext db, LibrarySeeder library, DataSeeder seeder, StoragePaths storage)
{
    public async Task CleanAndSeedAsync(CancellationToken ct = default)
    {
        // Every mapped table. The EF migrations-history table isn't a mapped entity, so it survives.
        var tables = db.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct()
            .ToList();

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            // Fold the WAL into the main file, then keep a recoverable copy before wiping.
            await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE)", ct);
            BackUpDatabaseFile();

            // FK enforcement can't be toggled inside a transaction, so set it on the connection first.
            // With it off we can delete in any order without untangling the FK graph.
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF", ct);

            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                foreach (var table in tables)
                {
                    await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"{table}\"", ct);
                }

                db.ChangeTracker.Clear();

                // Both seeders share this context, so their SaveChanges enlist in this transaction.
                // They only insert because we just emptied the DB (each no-ops on a non-empty one).
                await library.SeedAsync(ct);
                await seeder.SeedAsync(ct);

                // Once the work is done, don't let a client disconnect abort the commit itself.
                await tx.CommitAsync(CancellationToken.None);
            }
        }
        finally
        {
            // Always re-enable enforcement before this connection returns to the pool, and reclaim
            // the WAL space the bulk delete + reseed grew. Best-effort: never mask the real outcome.
            try { await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON", CancellationToken.None); } catch { /* ignore */ }
            try { await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE)", CancellationToken.None); } catch { /* ignore */ }
            await db.Database.CloseConnectionAsync();
        }

        // The wiped rows referenced uploaded images; drop the now-orphaned files. Housekeeping only.
        ClearUploads();
    }

    // A single rolling backup next to the live SQLite file, so an accidental wipe can be restored.
    private void BackUpDatabaseFile()
    {
        try
        {
            var source = db.Database.GetDbConnection().DataSource;
            if (!string.IsNullOrEmpty(source) && File.Exists(source))
            {
                File.Copy(source, source + ".pre-clean.bak", overwrite: true);
            }
        }
        catch { /* best-effort safety copy; never block the reset on it */ }
    }

    private void ClearUploads()
    {
        try
        {
            var dir = Path.GetFullPath(storage.UploadsDir);
            if (!Directory.Exists(dir))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                // Defensive: only delete files resolved to be inside the uploads directory.
                if (Path.GetFullPath(file).StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    try { File.Delete(file); } catch { /* skip a locked file */ }
                }
            }
        }
        catch { /* housekeeping only */ }
    }
}
