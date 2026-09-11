// Presentation helpers for SyncStage — compact status text (toolbar/tray)
// and the checklist-step index used by the sync overlay.

using JobTracker.Services;

namespace JobTracker.ViewModels;

public enum SyncStep { Connecting, Downloading, Parsing, Classifying, Matching, Statistics, Saving }

public static class SyncStageExtensions
{
    public static readonly SyncStep[] AllSteps =
    [
        SyncStep.Connecting, SyncStep.Downloading, SyncStep.Parsing,
        SyncStep.Classifying, SyncStep.Matching, SyncStep.Statistics, SyncStep.Saving,
    ];

    public static string Title(this SyncStep step) => step switch
    {
        SyncStep.Connecting => "Connecting to Gmail",
        SyncStep.Downloading => "Downloading emails",
        SyncStep.Parsing => "Parsing messages",
        SyncStep.Classifying => "Classifying with AI",
        SyncStep.Matching => "Matching applications",
        SyncStep.Statistics => "Updating statistics",
        SyncStep.Saving => "Saving",
        _ => "",
    };

    /// One-line summary for compact status displays (toolbar, tray).
    public static string ShortDescription(this SyncStage stage) => stage switch
    {
        SyncStage.Idle => "Idle",
        SyncStage.Connecting => "Connecting to Gmail…",
        SyncStage.Downloading d => d.Found > 0 ? $"Downloading — {d.Found} found…" : "Downloading emails…",
        SyncStage.Parsing p => $"Parsing archive — {p.Percent}%",
        SyncStage.Classifying c => $"Classifying {c.Done}/{c.Total}…",
        SyncStage.Matching => "Matching applications…",
        SyncStage.Statistics => "Updating statistics…",
        SyncStage.Saving => "Saving…",
        SyncStage.Finished f => f.Updates == 0 ? "Up to date" : $"Updated {f.Updates}",
        SyncStage.Failed failed => $"Failed: {failed.Message}",
        _ => "",
    };

    /// Index of the active checklist step for the given stage; null while
    /// idle/finished/failed (checklist hidden in those states).
    public static int? CurrentStepIndex(this SyncStage stage) => stage switch
    {
        SyncStage.Idle => null,
        SyncStage.Connecting => (int)SyncStep.Connecting,
        SyncStage.Downloading => (int)SyncStep.Downloading,
        SyncStage.Parsing => (int)SyncStep.Parsing,
        SyncStage.Classifying => (int)SyncStep.Classifying,
        SyncStage.Matching => (int)SyncStep.Matching,
        SyncStage.Statistics => (int)SyncStep.Statistics,
        SyncStage.Saving => (int)SyncStep.Saving,
        SyncStage.Finished => AllSteps.Length,
        SyncStage.Failed => null,
        _ => null,
    };

    public static string? DetailText(this SyncStage stage) => stage switch
    {
        SyncStage.Downloading d => d.Found > 0 ? $"{d.Found} found" : null,
        SyncStage.Parsing p => $"{p.Percent}%",
        SyncStage.Classifying c => $"{c.Done} / {c.Total}",
        _ => null,
    };

    public static bool IsCancellable(this SyncStage stage) => stage is SyncStage.Classifying or SyncStage.Parsing;
}
