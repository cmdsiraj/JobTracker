//
//  MenuBarContentView.swift
//  JobTracker
//
//  The menu-bar popover: stage-aware sync status, recent updates, and quick
//  actions.
//

import SwiftUI
import SwiftData

struct MenuBarContentView: View {
    @Environment(AppState.self) private var appState
    @Environment(\.openWindow) private var openWindow
    @Query(sort: \JobApplication.lastUpdated, order: .reverse)
    private var applications: [JobApplication]

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text("JobTracker").font(.headline)
                Spacer()
                statusIndicator
            }

            Divider()

            if applications.isEmpty {
                Text("No applications tracked yet.")
                    .font(.callout).foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
            } else {
                Text("Recent Updates").font(.caption).foregroundStyle(.secondary)
                ForEach(applications.prefix(6)) { app in
                    HStack(spacing: 8) {
                        Image(systemName: app.status.systemImage)
                            .foregroundStyle(app.status.color)
                        VStack(alignment: .leading, spacing: 1) {
                            Text(app.company).font(.callout).lineLimit(1)
                            Text(app.status.displayName)
                                .font(.caption2).foregroundStyle(.secondary)
                        }
                        Spacer()
                        Text(app.lastUpdated, format: .relative(presentation: .named))
                            .font(.caption2).foregroundStyle(.tertiary)
                    }
                }
            }

            Divider()

            HStack {
                Button {
                    Task { await appState.pipeline.syncNow() }
                } label: {
                    Label("Sync Now", systemImage: "arrow.clockwise")
                }
                .disabled(appState.pipeline.isRunning)

                Spacer()

                Button("Logs") {
                    openWindow(id: "activity-log")
                    NSApp.activate(ignoringOtherApps: true)
                }

                Button("Open") {
                    openWindow(id: "main")
                    NSApp.activate(ignoringOtherApps: true)
                }
            }

            Button("Quit JobTracker") { NSApp.terminate(nil) }
                .font(.caption)
                .buttonStyle(.plain)
                .foregroundStyle(.secondary)
        }
        .padding()
        .frame(width: 300)
    }

    private var statusIndicator: some View {
        HStack(spacing: 5) {
            if appState.pipeline.isRunning {
                ProgressView().controlSize(.small)
            } else {
                Circle()
                    .fill(appState.auth.isSignedIn ? Color.green : Color.orange)
                    .frame(width: 8, height: 8)
            }
            Text(statusText)
                .font(.caption).foregroundStyle(.secondary)
                .lineLimit(1)
        }
    }

    private var statusText: String {
        if appState.pipeline.isRunning {
            return appState.pipeline.stage.shortDescription
        }
        if case .failed(let message) = appState.pipeline.stage {
            return "Failed: \(message)"
        }
        return appState.auth.isSignedIn ? "Up to date" : "Gmail not connected"
    }
}
