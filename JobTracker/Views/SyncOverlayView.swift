//
//  SyncOverlayView.swift
//  JobTracker
//
//  Floating staged-progress card shown while the sync pipeline runs: a
//  vertical checklist of stages with live counts, spring-animated
//  checkmarks, and a success burst on completion. The parent controls
//  visibility from pipeline.stage.
//

import SwiftUI

struct SyncOverlayView: View {
    @Environment(AppState.self) private var appState

    /// The checklist rows, in pipeline order.
    private enum Step: Int, CaseIterable, Identifiable {
        case connecting, downloading, parsing, classifying, matching, statistics, saving

        var id: Int { rawValue }

        var title: String {
            switch self {
            case .connecting:  return "Connecting to Gmail"
            case .downloading: return "Downloading emails"
            case .parsing:     return "Parsing messages"
            case .classifying: return "Classifying with AI"
            case .matching:    return "Matching applications"
            case .statistics:  return "Updating statistics"
            case .saving:      return "Saving"
            }
        }
    }

    var body: some View {
        let stage = appState.pipeline.stage

        VStack(alignment: .leading, spacing: 14) {
            header(for: stage)

            if case .failed(let message) = stage {
                failureRow(message)
            } else if case .finished = stage {
                EmptyView()
            } else {
                checklist(for: stage)
            }

            if isCancellable(stage) {
                Button(role: .cancel) {
                    appState.pipeline.cancelImport()
                } label: {
                    Label("Cancel", systemImage: "xmark.circle")
                        .frame(maxWidth: .infinity)
                }
                .controlSize(.small)
            }
        }
        .padding(16)
        .frame(width: 300)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 16))
        .overlay(
            RoundedRectangle(cornerRadius: 16)
                .strokeBorder(.quaternary, lineWidth: 1)
        )
        .shadow(color: .black.opacity(0.18), radius: 18, y: 8)
        .animation(.spring(duration: 0.4), value: currentStepIndex(for: stage))
    }

    // MARK: Header

    @ViewBuilder
    private func header(for stage: SyncPipeline.Stage) -> some View {
        switch stage {
        case .finished(let updates):
            HStack(spacing: 10) {
                Image(systemName: "checkmark.circle.fill")
                    .font(.title2)
                    .foregroundStyle(.green)
                    .symbolEffect(.bounce, options: .nonRepeating)
                VStack(alignment: .leading, spacing: 1) {
                    Text("Sync Complete").font(.headline)
                    Text(updates == 0
                         ? "Everything is up to date."
                         : "^[\(updates) application](inflect: true) updated.")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
        case .failed:
            HStack(spacing: 10) {
                Image(systemName: "exclamationmark.triangle.fill")
                    .font(.title2)
                    .foregroundStyle(.orange)
                Text("Sync Failed").font(.headline)
            }
        default:
            HStack(spacing: 10) {
                Image(systemName: "arrow.triangle.2.circlepath")
                    .font(.title3)
                    .foregroundStyle(.tint)
                    .symbolEffect(.rotate, options: .repeating)
                Text("Syncing").font(.headline)
            }
        }
    }

    // MARK: Checklist

    private func checklist(for stage: SyncPipeline.Stage) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            ForEach(Step.allCases) { step in
                stepRow(step, stage: stage)
            }
        }
    }

    @ViewBuilder
    private func stepRow(_ step: Step, stage: SyncPipeline.Stage) -> some View {
        let current = currentStepIndex(for: stage)
        let state: StepState =
            current == nil ? .pending
            : step.rawValue < current! ? .done
            : step.rawValue == current! ? .active
            : .pending

        HStack(spacing: 10) {
            switch state {
            case .done:
                Image(systemName: "checkmark.circle.fill")
                    .foregroundStyle(.green)
                    .transition(.scale.combined(with: .opacity))
            case .active:
                ProgressView()
                    .controlSize(.small)
                    .frame(width: 16, height: 16)
            case .pending:
                Image(systemName: "circle.dotted")
                    .foregroundStyle(.quaternary)
            }

            VStack(alignment: .leading, spacing: 1) {
                Text(step.title + (state == .active ? "…" : ""))
                    .font(.callout)
                    .foregroundStyle(state == .pending ? .tertiary : .primary)
                if state == .active, let detail = detailText(for: stage) {
                    Text(detail)
                        .font(.caption.monospacedDigit())
                        .foregroundStyle(.secondary)
                        .contentTransition(.numericText())
                }
            }
            Spacer(minLength: 0)
        }
        .animation(.spring(duration: 0.35), value: state)
    }

    private enum StepState: Equatable { case pending, active, done }

    /// Index of the active step for the given stage; nil while idle.
    private func currentStepIndex(for stage: SyncPipeline.Stage) -> Int? {
        switch stage {
        case .idle:        return nil
        case .connecting:  return Step.connecting.rawValue
        case .downloading: return Step.downloading.rawValue
        case .parsing:     return Step.parsing.rawValue
        case .classifying: return Step.classifying.rawValue
        case .matching:    return Step.matching.rawValue
        case .statistics:  return Step.statistics.rawValue
        case .saving:      return Step.saving.rawValue
        case .finished:    return Step.allCases.count
        case .failed:      return nil
        }
    }

    private func detailText(for stage: SyncPipeline.Stage) -> String? {
        switch stage {
        case .downloading(let found):
            return found > 0 ? "\(found.formatted()) found" : nil
        case .parsing(let percent):
            return "\(percent)%"
        case .classifying(let done, let total):
            return "\(done.formatted()) / \(total.formatted())"
        default:
            return nil
        }
    }

    private func isCancellable(_ stage: SyncPipeline.Stage) -> Bool {
        switch stage {
        case .classifying, .parsing: return true
        default:                     return false
        }
    }

    private func failureRow(_ message: String) -> some View {
        Text(message)
            .font(.caption)
            .foregroundStyle(.secondary)
            .lineLimit(4)
            .fixedSize(horizontal: false, vertical: true)
    }
}

// MARK: - Short status line (toolbar / menu bar)

extension SyncPipeline.Stage {
    /// One-line summary for compact status displays.
    var shortDescription: String {
        switch self {
        case .idle:
            return "Idle"
        case .connecting:
            return "Connecting to Gmail…"
        case .downloading(let found):
            return found > 0 ? "Downloading — \(found.formatted()) found…" : "Downloading emails…"
        case .parsing(let percent):
            return "Parsing archive — \(percent)%"
        case .classifying(let done, let total):
            return "Classifying \(done.formatted())/\(total.formatted())…"
        case .matching:
            return "Matching applications…"
        case .statistics:
            return "Updating statistics…"
        case .saving:
            return "Saving…"
        case .finished(let updates):
            return updates == 0 ? "Up to date" : "Updated \(updates.formatted())"
        case .failed(let message):
            return "Failed: \(message)"
        }
    }
}
