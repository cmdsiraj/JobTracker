// Persisted sync bookkeeping. The watermark is a timestamp (not Gmail's
// historyId and never read/unread flags): every sync asks Gmail only for
// mail received after the last successful sync, minus a small overlap.

namespace JobTracker.Models;

public class SyncState
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// Received-date of the newest email that has been fully processed.
    /// Null until the initial import/sync has happened.
    public DateTimeOffset? LastSyncTimestamp { get; set; }

    public DateTimeOffset? LastSyncDate { get; set; }
    public string? LastError { get; set; }
    public bool InitialImportDone { get; set; }
    public string? AccountEmail { get; set; }
}
