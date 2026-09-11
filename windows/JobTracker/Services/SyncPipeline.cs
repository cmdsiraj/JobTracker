// The staged sync/import pipeline. One shared processing path turns
// FetchedMessages (from the Gmail API or a Takeout mbox archive) into
// JobApplication / EmailEvent records: dedupe by message id, classify in
// batches with the LLM, match into applications, advance statuses with an
// auto-logged history trail, save.
//
// Sync is launch-triggered (timestamp watermark, inbox + sent) with an
// optional tray poll loop. Archive import runs in two phases: a local parse
// (no LLM calls), then — after the user confirms a date scope —
// classification through the shared path.

using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using JobTracker.Data;
using JobTracker.Models;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Services;

public abstract record SyncStage
{
    public sealed record Idle : SyncStage;
    public sealed record Connecting : SyncStage;
    public sealed record Downloading(int Found) : SyncStage;
    public sealed record Parsing(int Percent) : SyncStage;
    public sealed record Classifying(int Done, int Total) : SyncStage;
    public sealed record Matching : SyncStage;
    public sealed record Statistics : SyncStage;
    public sealed record Saving : SyncStage;
    public sealed record Finished(int Updates) : SyncStage;
    public sealed record Failed(string Message) : SyncStage;
}

public enum ImportScope { CurrentCycle, LastYear, Everything }

public static class ImportScopeExtensions
{
    public static List<FetchedMessage> Filter(this ImportScope scope, List<FetchedMessage> messages) => scope switch
    {
        ImportScope.CurrentCycle => messages.Where(m => m.Date.LocalDateTime >= PendingImport.CurrentCycleStart).ToList(),
        ImportScope.LastYear => messages.Where(m => m.Date.LocalDateTime >= DateTime.Now.AddYears(-1)).ToList(),
        ImportScope.Everything => messages,
        _ => messages,
    };
}

public sealed record PendingImport(List<FetchedMessage> Candidates, MboxParseSummary Summary)
{
    /// Start of the current recruiting cycle (Aug 1 of the most recent August).
    public static DateTime CurrentCycleStart
    {
        get
        {
            var now = DateTime.Now;
            var cycleYear = now.Month >= 8 ? now.Year : now.Year - 1;
            return new DateTime(cycleYear, 8, 1);
        }
    }

    public int CurrentCycleCount => Candidates.Count(c => c.Date.LocalDateTime >= CurrentCycleStart);
    public int LastYearCount => Candidates.Count(c => c.Date.LocalDateTime >= DateTime.Now.AddYears(-1));
}

public sealed partial class SyncPipeline : DispatcherObservableObject
{
    [ObservableProperty]
    private SyncStage _stage = new SyncStage.Idle();

    [ObservableProperty]
    private int _newlyUpdatedCount;

    [ObservableProperty]
    private string? _lastError;

    [ObservableProperty]
    private PendingImport? _pendingImport;

    /// True while any working stage is active.
    public bool IsRunning => Stage is not (SyncStage.Idle or SyncStage.Finished or SyncStage.Failed);

    private readonly JobTrackerDbContext _context;
    private readonly GmailAuthService _auth;
    private readonly Preferences _prefs;
    private readonly ActivityLog _log = ActivityLog.Shared;

    /// Only persist classifications we're reasonably confident about.
    private const double ConfidenceThreshold = 0.55;

    /// Consecutive whole-batch failures tolerated before aborting a run.
    private const int MaxConsecutiveBatchFailures = 5;

    private Task? _importTask;
    private CancellationTokenSource? _importCts;
    private CancellationTokenSource? _watcherCts;
    private bool _launchSyncStarted;

    public SyncPipeline(JobTrackerDbContext context, GmailAuthService auth, Preferences prefs)
    {
        _context = context;
        _auth = auth;
        _prefs = prefs;
    }

    partial void OnStageChanged(SyncStage value) => OnPropertyChanged(nameof(IsRunning));

    // MARK: - Launch sync

    /// Call once from the UI's startup path: kicks off a sync on launch when
    /// the user finished onboarding and is signed in. No-ops on later calls.
    public void SyncOnLaunchIfNeeded()
    {
        if (_launchSyncStarted || !_prefs.OnboardingDone || !_auth.IsSignedIn) return;
        _launchSyncStarted = true;
        _ = SyncNowAsync();
    }

    // MARK: - Sync

    public async Task SyncNowAsync()
    {
        if (IsRunning) return;
        if (!_auth.IsSignedIn || !AppConfig.IsGoogleConfigured)
        {
            _log.Info("Sync skipped: not connected to Gmail");
            return;
        }

        LastError = null;
        NewlyUpdatedCount = 0;
        Stage = new SyncStage.Connecting();
        _log.Info("Sync started");

        var api = new GmailApiClient(_auth);

        try
        {
            var state = GetOrCreateSyncState();
            if (state.AccountEmail is null)
            {
                try { state.AccountEmail = (await api.GetProfileAsync()).EmailAddress; }
                catch { /* best effort */ }
            }

            // Recorded BEFORE downloading so mail arriving mid-sync falls
            // after the next watermark (overlap covers the boundary).
            var syncStart = DateTimeOffset.UtcNow;

            List<string> messageIds;
            if (state.LastSyncTimestamp is { } watermark)
            {
                messageIds = await api.MessageIdsAfterAsync(watermark - AppConfig.SyncOverlap);
            }
            else if (state.InitialImportDone)
            {
                // Archive already covered history; just establish the watermark.
                messageIds = [];
            }
            else
            {
                messageIds = await api.RecentMessageIdsAsync(30);
            }

            Stage = new SyncStage.Downloading(messageIds.Count);
            _log.Info($"Sync: {messageIds.Count} message id(s) listed");

            // Single fetch of stored ids for the whole run (also reused by
            // the shared processing path).
            var existingIds = ExistingMessageIds();
            var messages = new List<FetchedMessage>();
            foreach (var id in messageIds.Where(id => !existingIds.Contains(id)))
            {
                try { messages.Add(await api.GetMessageAsync(id)); }
                catch (Exception ex) { _log.Warning($"Could not fetch message {id}: {ex.Message}"); }
            }

            await ProcessAsync(messages, existingIds);

            state.LastSyncTimestamp = syncStart;
            state.LastSyncDate = DateTimeOffset.UtcNow;
            state.LastError = null;
            _context.SaveChanges();

            Stage = new SyncStage.Finished(NewlyUpdatedCount);
            _log.Success($"Sync finished: {NewlyUpdatedCount} update(s) from {messages.Count} new email(s)");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Stage = new SyncStage.Failed(ex.Message);
            _log.Error($"Sync failed: {ex.Message}");
            TrySave();
        }
    }

    // MARK: - Mail archive import (Google Takeout mbox)

    /// Phase 1: parse the archive (no LLM calls, nothing leaves the machine).
    public void ImportMbox(string path)
    {
        if (IsRunning || _importTask is not null) return;
        _importCts = new CancellationTokenSource();
        var token = _importCts.Token;
        _importTask = Task.Run(async () =>
        {
            await RunParsePhaseAsync(path, token);
            _importTask = null;
        }, token);
    }

    /// Phase 2: user picked a scope; classify those candidates.
    public void ConfirmImport(ImportScope scope)
    {
        if (PendingImport is not { } pending || IsRunning || _importTask is not null) return;
        PendingImport = null;
        var messages = scope.Filter(pending.Candidates);
        _importCts = new CancellationTokenSource();
        var token = _importCts.Token;
        _importTask = Task.Run(async () =>
        {
            await RunClassifyPhaseAsync(messages, token);
            _importTask = null;
        }, token);
    }

    public void DiscardPendingImport()
    {
        PendingImport = null;
        Stage = new SyncStage.Idle();
        _log.Info("Import discarded before classification");
    }

    public void CancelImport() => _importCts?.Cancel();

    private async Task RunParsePhaseAsync(string path, CancellationToken token)
    {
        LastError = null;
        Stage = new SyncStage.Parsing(0);
        _log.Info($"Import started: {Path.GetFileName(path)}");

        try
        {
            var lastPercent = -1;
            var (candidates, summary) = await Task.Run(() =>
                MboxParser.CollectJobCandidates(path, fraction =>
                {
                    var percent = Math.Min(100, (int)(fraction * 100));
                    if (percent == lastPercent) return;
                    lastPercent = percent;
                    ReportParseProgress(percent);
                }), token);

            PendingImport = new PendingImport(candidates, summary);
            Stage = new SyncStage.Idle();
            _log.Info($"Parse done: {summary.TotalMessages} emails, {summary.SkippedSpamTrash} spam/trash skipped, {summary.Candidates} job-related candidates");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Stage = new SyncStage.Failed(ex.Message);
            _log.Error($"Archive parse failed: {ex.Message}");
        }
    }

    /// Only advances the parse stage; ignores stragglers after completion.
    private void ReportParseProgress(int percent)
    {
        if (Stage is SyncStage.Parsing) Stage = new SyncStage.Parsing(percent);
    }

    private async Task RunClassifyPhaseAsync(List<FetchedMessage> messages, CancellationToken token)
    {
        LastError = null;
        NewlyUpdatedCount = 0;
        _log.Info($"Import classification started: {messages.Count} candidate(s), model {_prefs.Model}");

        try
        {
            var existingIds = ExistingMessageIds();
            await ProcessAsync(messages, existingIds, token);

            // The archive replaces the API backfill window.
            var state = GetOrCreateSyncState();
            state.InitialImportDone = true;
            state.LastSyncTimestamp = messages.Count > 0 ? messages.Max(m => m.Date) : DateTimeOffset.UtcNow;
            state.LastSyncDate = DateTimeOffset.UtcNow;
            _context.SaveChanges();

            Stage = new SyncStage.Finished(NewlyUpdatedCount);
            _log.Success($"Import finished: {NewlyUpdatedCount} application update(s) from {messages.Count} email(s)");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Stage = new SyncStage.Failed(ex.Message);
            _log.Error($"Import failed: {ex.Message}");
            TrySave();
        }
    }

    // MARK: - Tray watcher

    /// Optional near-real-time poll loop; off by default (launch-sync keeps
    /// resource use minimal).
    public void SetTrayWatcher(bool enabled)
    {
        if (enabled)
        {
            if (_watcherCts is not null) return;
            _log.Info($"Tray watcher enabled (every {(int)_prefs.PollInterval.TotalSeconds}s)");
            _watcherCts = new CancellationTokenSource();
            var token = _watcherCts.Token;
            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try { await Task.Delay(_prefs.PollInterval, token); }
                    catch (TaskCanceledException) { return; }
                    if (token.IsCancellationRequested) return;
                    if (!IsRunning) await SyncNowAsync();
                }
            }, token);
        }
        else
        {
            if (_watcherCts is null) return;
            _watcherCts.Cancel();
            _watcherCts = null;
            _log.Info("Tray watcher disabled");
        }
    }

    // MARK: - Shared processing (sync + import)

    /// Classifies `messages` (oldest first, batched) and upserts every
    /// confident job-related result. `existingIds` must come from a single
    /// EmailEvent fetch performed by the caller for this run.
    private async Task ProcessAsync(List<FetchedMessage> messages, HashSet<string> existingIds, CancellationToken token = default)
    {
        // Dedupe against the store and within the run, then oldest first so
        // status progression follows real chronology.
        var seen = new HashSet<string>(existingIds);
        var pending = messages
            .Where(m => seen.Add(m.Id))
            .OrderBy(m => m.Date)
            .ToList();

        var skipped = messages.Count - pending.Count;
        if (skipped > 0) _log.Info($"Skipped {skipped} already-stored email(s)");

        var total = pending.Count;
        Stage = new SyncStage.Classifying(0, total);

        // Matcher indexes are built once per run from a single fetch.
        var applications = _context.Applications.ToList();
        var matcher = new ApplicationMatcher(applications);

        if (total > 0)
        {
            var classifier = new EmailClassifier(new NimClient(_prefs.Model));
            var done = 0;
            var consecutiveBatchFailures = 0;
            var index = 0;

            while (index < pending.Count)
            {
                if (token.IsCancellationRequested)
                {
                    _log.Warning($"Cancelled after {done}/{total} — {NewlyUpdatedCount} update(s) already saved");
                    break;
                }
                var upper = Math.Min(index + AppConfig.ClassificationBatchSize, pending.Count);
                var batch = pending[index..upper];
                index = upper;

                try
                {
                    var results = await ClassifyWithRetryAsync(batch, classifier);
                    consecutiveBatchFailures = 0;
                    for (var position = 0; position < batch.Count; position++)
                    {
                        if (!results.TryGetValue(position, out var result)) continue;
                        var message = batch[position];
                        if (result.IsJobRelated && result.Confidence >= ConfidenceThreshold)
                        {
                            Upsert(message, result, matcher);
                            NewlyUpdatedCount++;
                            var status = result.ApplicationStatus
                                ?? (message.IsOutgoing ? ApplicationStatus.Outreach : ApplicationStatus.Applied);
                            var cycle = result.Cycle is { Length: > 0 } c ? $" [{c}]" : "";
                            _log.Success($"{result.Company} — {result.Role} → {status.DisplayName()}{cycle}");
                        }
                        else if (result.IsJobRelated)
                        {
                            _log.Warning($"Low confidence ({result.Confidence:F2}): {Truncate(message.Subject, 60)}");
                        }
                        else
                        {
                            _log.Info($"Not job-related: {Truncate(message.Subject, 60)}");
                        }
                    }
                }
                catch (NimException ex) when (ex.Kind == NimErrorKind.MissingApiKey)
                {
                    throw; // abort: user must add a key
                }
                catch (Exception ex)
                {
                    consecutiveBatchFailures++;
                    _log.Error($"Batch {done / Math.Max(AppConfig.ClassificationBatchSize, 1) + 1} failed ({consecutiveBatchFailures} in a row): {ex.Message}");
                    if (consecutiveBatchFailures >= MaxConsecutiveBatchFailures)
                    {
                        _log.Error("stopping — likely out of API credits; re-run later to resume");
                        break;
                    }
                }

                done += batch.Count;
                Stage = new SyncStage.Classifying(done, total);
                TrySave();
                // Pacing under the provider's rate limit is enforced by
                // RequestThrottle inside NimClient — no extra delay needed.
            }
        }

        // Post-pass: collapse any duplicates that separate emails still
        // managed to create (and repair ones from earlier runs).
        Stage = new SyncStage.Matching();
        try { DuplicateMerger.Run(_context); } catch { /* best effort */ }
        Stage = new SyncStage.Statistics();
        await Task.Delay(250, CancellationToken.None);
        Stage = new SyncStage.Saving();
        _context.SaveChanges();
    }

    /// One retry (after 3s) on a rate-limit response; other errors and a
    /// second rate-limit propagate to the batch failure counter.
    private static async Task<Dictionary<int, ClassificationResult>> ClassifyWithRetryAsync(
        List<FetchedMessage> batch, EmailClassifier classifier)
    {
        try
        {
            return await classifier.ClassifyAsync(batch);
        }
        catch (NimException ex) when (ex.Kind == NimErrorKind.RateLimited)
        {
            await Task.Delay(3000);
            return await classifier.ClassifyAsync(batch);
        }
    }

    // MARK: - Upsert

    internal void Upsert(FetchedMessage message, ClassificationResult result, ApplicationMatcher matcher)
    {
        JobApplication application;
        double? reviewConfidence = null;

        var match = matcher.MatchMessage(message, result);
        if (match is not null)
        {
            application = match.Application;
            if (match.NeedsReview)
            {
                application.NeedsReview = true;
                reviewConfidence = match.Confidence;
            }
        }
        else
        {
            // No usable company from the LLM? Derive one from the sender
            // domain (careers@stripe.com → "Stripe") before giving up —
            // otherwise every such email used to spawn a nameless record.
            var company = result.Company;
            if (JobApplication.NormalizeCompany(company).Length == 0)
            {
                company = ApplicationMatcher.CompanyFromSender(message.Sender) ?? "";
            }
            if (JobApplication.NormalizeCompany(company).Length == 0 && result.Role.Length == 0)
            {
                _log.Warning($"Skipped (no company or role identifiable): {Truncate(message.Subject, 60)}");
                return;
            }

            // The user's own outreach starts the pipeline at Outreach when
            // the model gave no usable status.
            var status = result.ApplicationStatus
                ?? (message.IsOutgoing ? ApplicationStatus.Outreach : ApplicationStatus.Applied);
            var app = new JobApplication(company, result.Role, status)
            {
                AppliedDate = message.Date,
                LastUpdated = message.Date,
                ContactEmail = message.Sender,
                Source = result.Source,
                Location = result.Location,
            };
            if (message.ThreadId.Length > 0) app.ThreadIds.Add(message.ThreadId);
            _context.Applications.Add(app);
            matcher.Register(app);
            application = app;
        }

        // Recruiting cycle: prefer the LLM's extraction, fall back to a
        // regex over the subject + role text.
        if (application.Cycle.Length == 0)
        {
            var detected = result.Cycle is { Length: > 0 } c ? c : CycleDetector.Detect($"{message.Subject} {result.Role}");
            if (!string.IsNullOrEmpty(detected)) application.Cycle = detected;
        }

        if (message.ThreadId.Length > 0 && !application.ThreadIds.Contains(message.ThreadId))
        {
            application.ThreadIds.Add(message.ThreadId);
            // Keep the session index current so the NEXT email in this
            // thread matches at 1.0 (this omission caused duplicates).
            matcher.Associate(message.ThreadId, application);
        }

        var evt = new EmailEvent(message.Id, message.ThreadId, message.Date, message.Sender, message.Subject,
            message.Snippet, message.IsOutgoing ? EventDirection.Outgoing : EventDirection.Incoming)
        {
            DetectedStatusRaw = result.ApplicationStatus?.ToRaw(),
            Confidence = result.Confidence,
            RawJson = null,
            Application = application,
        };
        if (reviewConfidence is { } rc) evt.MatchConfidence = rc;
        _context.Events.Add(evt);

        // Advance status on a newer, different signal — compared against the
        // newest EMAIL date, not LastUpdated (manual edits bump LastUpdated
        // to "now", which used to block every later email update).
        if (result.ApplicationStatus is { } detectedStatus &&
            message.Date >= (application.LastEmailDate ?? DateTimeOffset.MinValue) &&
            detectedStatus != application.Status)
        {
            var change = EmailEvent.NoteOrStatusChange(EventKind.StatusChange,
                $"{application.Status.DisplayName()} → {detectedStatus.DisplayName()}", message.Date);
            change.Application = application;
            _context.Events.Add(change);
            application.Status = detectedStatus;
        }

        application.LastEmailDate = Max(application.LastEmailDate, message.Date);
        application.LastUpdated = application.LastUpdated > message.Date ? application.LastUpdated : message.Date;
        if (result.NextAction is { Length: > 0 } action) application.NextAction = action;
        application.Location ??= result.Location;
        application.Source ??= result.Source;
    }

    private static DateTimeOffset Max(DateTimeOffset? a, DateTimeOffset b) =>
        a is { } value && value > b ? value : b;

    // MARK: - Persistence helpers

    private SyncState GetOrCreateSyncState()
    {
        var existing = _context.SyncStates.FirstOrDefault();
        if (existing is not null) return existing;
        var state = new SyncState();
        _context.SyncStates.Add(state);
        return state;
    }

    /// All stored Gmail message ids, fetched once per run for O(1) dedupe.
    private HashSet<string> ExistingMessageIds() =>
        _context.Events.Select(e => e.GmailMessageId).Where(id => id != "").ToHashSet();

    private void TrySave()
    {
        try { _context.SaveChanges(); }
        catch (DbUpdateException) { /* best effort, matches macOS's try? modelContext.save() */ }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
