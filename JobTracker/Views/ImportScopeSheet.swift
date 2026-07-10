//
//  ImportScopeSheet.swift
//  JobTracker
//
//  Shown after the archive parse: lets the user pick how far back to
//  classify. Classification is batched (~8 emails per request), so the
//  counts and time estimates are shown up front.
//

import SwiftUI

struct ImportScopeSheet: View {
    @Environment(\.dismiss) private var dismiss
    let pending: SyncPipeline.PendingImport
    let onConfirm: (SyncPipeline.ImportScope) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            VStack(alignment: .leading, spacing: 4) {
                Text("Import Mail Archive").font(.title2.bold())
                Text("""
                Scanned \(pending.summary.totalMessages.formatted()) emails and found \
                \(pending.summary.candidates.formatted()) job-related candidates. \
                Classification runs in batches of 8 per API request — pick how far back to import.
                """)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            }

            VStack(spacing: 10) {
                scopeButton(
                    title: "Current cycle",
                    subtitle: "Since \(Self.cycleStartText) — what you're actively tracking",
                    count: pending.currentCycleCount,
                    scope: .currentCycle,
                    prominent: true
                )
                scopeButton(
                    title: "Last 12 months",
                    subtitle: "Includes the tail of the previous cycle",
                    count: pending.lastYearCount,
                    scope: .lastYear,
                    prominent: false
                )
                scopeButton(
                    title: "Everything",
                    subtitle: "The whole archive — may exceed free-tier API credits",
                    count: pending.candidates.count,
                    scope: .everything,
                    prominent: false
                )
            }

            HStack {
                Button("Cancel") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Spacer()
                Text("You can re-import a wider range later; already-imported emails are skipped.")
                    .font(.caption).foregroundStyle(.tertiary)
            }
        }
        .padding(24)
        .frame(width: 480)
    }

    private static var cycleStartText: String {
        SyncPipeline.PendingImport.currentCycleStart
            .formatted(.dateTime.month(.wide).year())
    }

    /// ~8 emails per request, ~2.5s per request round trip.
    private func estimate(_ count: Int) -> String {
        let seconds = Double(count) / 8.0 * 2.5
        if seconds < 60 {
            return "\(count.formatted()) emails · under a minute"
        }
        let minutes = Int(ceil(seconds / 60))
        return "\(count.formatted()) emails · ~\(minutes) min"
    }

    private func scopeButton(title: String, subtitle: String, count: Int,
                             scope: SyncPipeline.ImportScope, prominent: Bool) -> some View {
        Button {
            onConfirm(scope)
            dismiss()
        } label: {
            HStack {
                VStack(alignment: .leading, spacing: 2) {
                    Text(title).font(.headline)
                    Text(subtitle).font(.caption).foregroundStyle(.secondary)
                }
                Spacer()
                Text(estimate(count))
                    .font(.callout.monospacedDigit())
                    .foregroundStyle(prominent ? Color.accentColor : .secondary)
            }
            .padding(12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(.quaternary.opacity(prominent ? 0.6 : 0.3),
                        in: RoundedRectangle(cornerRadius: 10))
        }
        .buttonStyle(.plain)
        .disabled(count == 0)
    }
}
