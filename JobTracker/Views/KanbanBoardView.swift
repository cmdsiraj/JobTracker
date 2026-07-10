//
//  KanbanBoardView.swift
//  JobTracker
//
//  A horizontally-scrolling board with one column per pipeline status. Cards
//  can be dragged between columns; every move updates the status instantly
//  and logs a statusChange event on the application's timeline.
//

import SwiftUI
import SwiftData

struct KanbanBoardView: View {
    @Environment(\.modelContext) private var modelContext
    let applications: [JobApplication]
    var columns: [ApplicationStatus] = ApplicationStatus.boardColumns
    @Binding var selection: JobApplication?

    /// Cards rendered per column before "Show all" (keeps huge datasets snappy).
    private static let initialColumnCap = 40
    @State private var expandedColumns: Set<ApplicationStatus> = []

    var body: some View {
        // Group ONCE instead of filtering the whole array per column.
        let grouped = Dictionary(grouping: applications, by: \.status)

        if applications.isEmpty {
            ContentUnavailableView(
                "No Applications Here",
                systemImage: "briefcase",
                description: Text("New job-related emails show up automatically after a sync, or drag cards into this column.")
            )
        } else {
            ScrollView(.horizontal, showsIndicators: true) {
                HStack(alignment: .top, spacing: 16) {
                    ForEach(columns) { status in
                        column(for: status, items: grouped[status] ?? [])
                    }
                }
                .padding()
            }
        }
    }

    private func column(for status: ApplicationStatus,
                        items: [JobApplication]) -> some View {
        let isExpanded = expandedColumns.contains(status)
        let visible = isExpanded ? items : Array(items.prefix(Self.initialColumnCap))
        return VStack(alignment: .leading, spacing: 10) {
            HStack {
                Image(systemName: status.systemImage)
                    .foregroundStyle(status.color)
                Text(status.displayName).font(.headline)
                Spacer()
                Text("\(items.count)")
                    .font(.caption.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .padding(.horizontal, 7).padding(.vertical, 2)
                    .background(.quaternary, in: Capsule())
                    .contentTransition(.numericText())
            }

            ScrollView {
                LazyVStack(spacing: 10) {
                    ForEach(visible) { app in
                        ApplicationCardView(application: app)
                            .onTapGesture { selection = app }
                            .draggable(app.id.uuidString)
                    }
                    if !isExpanded, items.count > Self.initialColumnCap {
                        Button("Show all \(items.count)…") {
                            withAnimation { _ = expandedColumns.insert(status) }
                        }
                        .buttonStyle(.plain)
                        .font(.callout)
                        .foregroundStyle(Color.accentColor)
                        .padding(.vertical, 6)
                    }
                }
            }
        }
        .padding(12)
        .frame(width: columns.count == 1 ? 340 : 260)
        .background(status.color.opacity(0.06), in: RoundedRectangle(cornerRadius: 12))
        .dropDestination(for: String.self) { items, _ in
            guard let idString = items.first, let id = UUID(uuidString: idString) else { return false }
            return move(applicationID: id, to: status)
        }
    }

    private func move(applicationID: UUID, to status: ApplicationStatus) -> Bool {
        guard let app = applications.first(where: { $0.id == applicationID }),
              app.status != status else { return false }

        let previous = app.status
        withAnimation(.spring(duration: 0.35)) {
            app.status = status
            app.lastUpdated = Date()
        }

        let event = EmailEvent(kind: .statusChange,
                               text: "\(previous.displayName) → \(status.displayName)")
        modelContext.insert(event)
        event.application = app

        try? modelContext.save()
        return true
    }
}
