// In-app activity log so the user can watch what ingestion/sync are doing
// (parse results, per-email classification, skips, errors). Entries are
// also mirrored to System.Diagnostics.Trace.
//
// Framework-agnostic by design (no WPF Dispatcher dependency) so it's usable
// from background sync tasks and from unit tests alike; subscribe to
// Changed and marshal to the UI thread from the ViewModel side.

using System.Diagnostics;
using System.Threading;

namespace JobTracker.Services;

public sealed class ActivityLog
{
    public static readonly ActivityLog Shared = new();

    public enum Level { Info, Success, Warning, Error }

    public sealed record Entry(Guid Id, DateTimeOffset Date, Level Level, string Message);

    private const int Cap = 5000;
    private readonly Lock _lock = new();
    private readonly List<Entry> _entries = [];

    /// Raised (possibly off the UI thread) whenever entries change.
    public event Action? Changed;

    private ActivityLog() { }

    public IReadOnlyList<Entry> Entries
    {
        get { lock (_lock) { return _entries.ToList(); } }
    }

    public void Info(string message) => Append(Level.Info, message);
    public void Success(string message) => Append(Level.Success, message);
    public void Warning(string message) => Append(Level.Warning, message);
    public void Error(string message) => Append(Level.Error, message);

    private void Append(Level level, string message)
    {
        lock (_lock)
        {
            _entries.Add(new Entry(Guid.NewGuid(), DateTimeOffset.Now, level, message));
            if (_entries.Count > Cap)
            {
                _entries.RemoveRange(0, _entries.Count - Cap);
            }
        }
        switch (level)
        {
            case Level.Error: Trace.TraceError(message); break;
            case Level.Warning: Trace.TraceWarning(message); break;
            default: Trace.TraceInformation(message); break;
        }
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_lock) { _entries.Clear(); }
        Changed?.Invoke();
    }

    /// Plain-text dump for the copy button.
    public string Text
    {
        get
        {
            lock (_lock)
            {
                return string.Join('\n', _entries.Select(e =>
                    $"[{e.Date:HH:mm:ss}] {e.Level.Glyph()} {e.Message}"));
            }
        }
    }
}

public static class ActivityLogLevelExtensions
{
    public static string Glyph(this ActivityLog.Level level) => level switch
    {
        ActivityLog.Level.Info => "•",
        ActivityLog.Level.Success => "✓",
        ActivityLog.Level.Warning => "△",
        ActivityLog.Level.Error => "✕",
        _ => "•",
    };
}
