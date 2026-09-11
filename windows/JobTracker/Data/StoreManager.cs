// Creates the EF Core/SQLite store. Unlike the macOS app there's no iCloud
// storage option (no CloudKit on Windows) — data always lives locally under
// %LOCALAPPDATA%. Also performs the one-time wipe of everything an older
// architecture version wrote, mirroring StoreManager.swift.

using System.IO;
using JobTracker.Services;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Data;

public static class StoreManager
{
    private static string StorePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JobTracker", "JobTrackerV2.db");

    public static JobTrackerDbContext MakeContext()
    {
        var dir = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(dir);
        var options = new DbContextOptionsBuilder<JobTrackerDbContext>()
            .UseSqlite($"Data Source={StorePath}")
            .Options;
        var context = new JobTrackerDbContext(options);
        context.Database.Migrate();
        return context;
    }

    // MARK: - Architecture reset

    /// One-time transition between architecture versions: removes the old
    /// store, secrets, and preferences. Runs before the context is created.
    public static void PerformArchitectureResetIfNeeded(bool force = false)
    {
        if (!force && !Preferences.NeedsArchitectureReset()) return;

        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            try { File.Delete(StorePath + suffix); } catch { /* best effort */ }
        }

        Secrets.Shared.WipeAll();
        Preferences.Shared.WipeAll();
        Preferences.MarkArchitectureReset();
    }
}
