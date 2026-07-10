//
//  ReviewView.swift
//  JobTracker
//
//  Triage tray for applications where the matcher attached an email with
//  low confidence. The user confirms the match, opens the detail to fix it,
//  or deletes the application.
//

import SwiftUI
import SwiftData

struct ReviewView: View {
    @Environment(\.modelContext) private var modelContext
    @Query(filter: #Predicate<JobApplication> { $0.needsReview },
           sort: \JobApplication.lastUpdated, order: .reverse)
    private var applications: [JobApplication]

    @State private var selectedApp: JobApplication?
    @State private var mergeResult: Int?

    var body: some View {
        Group {
            if applications.isEmpty {
                ContentUnavailableView(
                    "All Clear",
                    systemImage: "checkmark.seal",
                    description: Text("Nothing needs review. Low-confidence email matches will land here after a sync.")
                )
            } else {
                List(applications) { app in
                    ReviewRow(app: app,
                              onConfirm: { confirm(app) },
                              onOpen: { selectedApp = app },
                              onDelete: { delete(app) })
                }
                .listStyle(.inset)
            }
        }
        .sheet(item: $selectedApp) { app in
            ApplicationDetailView(application: app)
                .frame(minWidth: 560, minHeight: 580)
        }
        .toolbar {
            ToolbarItem {
                Button {
                    let count = (try? DuplicateMerger.run(in: modelContext)) ?? 0
                    withAnimation { mergeResult = count }
                } label: {
                    Label("Merge Duplicates", systemImage: "arrow.triangle.merge")
                }
                .help("Scan all applications and merge duplicates (same company, same role)")
            }
        }
        .overlay(alignment: .bottom) {
            if let mergeResult {
                Text(mergeResult == 0
                     ? "No duplicates found"
                     : "Merged \(mergeResult) duplicate\(mergeResult == 1 ? "" : "s")")
                    .font(.callout.weight(.medium))
                    .padding(.horizontal, 14).padding(.vertical, 8)
                    .background(.regularMaterial, in: Capsule())
                    .padding(.bottom, 16)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
                    .task {
                        try? await Task.sleep(for: .seconds(2.5))
                        withAnimation { self.mergeResult = nil }
                    }
            }
        }
    }

    private func confirm(_ app: JobApplication) {
        withAnimation(.spring(duration: 0.35)) {
            app.needsReview = false
            app.lastUpdated = Date()
        }
        try? modelContext.save()
    }

    private func delete(_ app: JobApplication) {
        withAnimation(.spring(duration: 0.35)) {
            modelContext.delete(app)
        }
        try? modelContext.save()
    }
}

private struct ReviewRow: View {
    let app: JobApplication
    let onConfirm: () -> Void
    let onOpen: () -> Void
    let onDelete: () -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 10) {
                Image(systemName: app.status.systemImage)
                    .foregroundStyle(app.status.color)
                VStack(alignment: .leading, spacing: 1) {
                    Text(app.company.isEmpty ? "Unknown Company" : app.company)
                        .font(.headline)
                    if !app.roleTitle.isEmpty {
                        Text(app.roleTitle)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
                Spacer()
                Button("Looks Right", systemImage: "checkmark.circle",
                       action: onConfirm)
                    .controlSize(.small)
                Button("Open", systemImage: "arrow.up.forward.square",
                       action: onOpen)
                    .controlSize(.small)
                Button("Delete", systemImage: "trash", role: .destructive,
                       action: onDelete)
                    .controlSize(.small)
            }

            ForEach(lowConfidenceEvents.prefix(3)) { event in
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Label(event.matchConfidence.formatted(.percent.precision(.fractionLength(0))),
                          systemImage: "questionmark.circle")
                        .font(.caption2)
                        .foregroundStyle(.yellow)
                    VStack(alignment: .leading, spacing: 1) {
                        Text(event.subject).font(.caption).lineLimit(1)
                        Text(event.snippet)
                            .font(.caption2)
                            .foregroundStyle(.secondary)
                            .lineLimit(2)
                    }
                    Spacer()
                    Text(event.receivedDate, format: .dateTime.month().day())
                        .font(.caption2)
                        .foregroundStyle(.tertiary)
                }
                .padding(8)
                .background(.quaternary.opacity(0.3), in: RoundedRectangle(cornerRadius: 8))
            }
        }
        .padding(.vertical, 6)
    }

    private var lowConfidenceEvents: [EmailEvent] {
        app.sortedEvents.filter { $0.kind == .email && $0.matchConfidence < 0.9 }
    }
}
