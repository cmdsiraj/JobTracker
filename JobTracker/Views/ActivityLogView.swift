//
//  ActivityLogView.swift
//  JobTracker
//
//  Live activity log window: shows what ingestion/sync is doing in real
//  time, with auto-scroll, copy, and clear.
//

import SwiftUI
import AppKit

struct ActivityLogView: View {
    private var log = ActivityLog.shared
    @State private var autoScroll = true

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()
            if log.entries.isEmpty {
                ContentUnavailableView(
                    "No Activity Yet",
                    systemImage: "text.alignleft",
                    description: Text("Import an archive or run a sync to see live progress here.")
                )
                .frame(maxHeight: .infinity)
            } else {
                logList
            }
        }
        .frame(minWidth: 560, minHeight: 360)
    }

    private var header: some View {
        HStack {
            Text("\(log.entries.count) entries")
                .font(.caption).foregroundStyle(.secondary)
            Spacer()
            Toggle("Auto-scroll", isOn: $autoScroll)
                .toggleStyle(.checkbox)
                .font(.caption)
            Button {
                NSPasteboard.general.clearContents()
                NSPasteboard.general.setString(log.text, forType: .string)
            } label: {
                Label("Copy", systemImage: "doc.on.doc")
            }
            .controlSize(.small)
            Button(role: .destructive) {
                log.clear()
            } label: {
                Label("Clear", systemImage: "trash")
            }
            .controlSize(.small)
        }
        .padding(8)
    }

    private var logList: some View {
        ScrollViewReader { proxy in
            List(log.entries) { entry in
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(entry.date, format: .dateTime.hour().minute().second())
                        .font(.caption.monospaced())
                        .foregroundStyle(.tertiary)
                    Text(entry.level.rawValue)
                        .font(.caption.monospaced().bold())
                        .foregroundStyle(color(for: entry.level))
                    Text(entry.message)
                        .font(.caption.monospaced())
                        .textSelection(.enabled)
                }
                .listRowSeparator(.hidden)
                .id(entry.id)
            }
            .listStyle(.plain)
            .onChange(of: log.entries.count) {
                if autoScroll, let last = log.entries.last {
                    proxy.scrollTo(last.id, anchor: .bottom)
                }
            }
        }
    }

    private func color(for level: ActivityLog.Level) -> Color {
        switch level {
        case .info:    return .secondary
        case .success: return .green
        case .warning: return .orange
        case .error:   return .red
        }
    }
}
