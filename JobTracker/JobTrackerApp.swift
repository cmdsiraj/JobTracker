//
//  JobTrackerApp.swift
//  JobTracker
//
//  App entry point: performs the one-time architecture reset, opens the
//  store the user chose during onboarding (iCloud or local), and wires the
//  shared AppState into every scene.
//

import SwiftUI
import SwiftData

@main
struct JobTrackerApp: App {
    let container: ModelContainer
    @State private var appState: AppState

    init() {
        StoreManager.performArchitectureResetIfNeeded()
        let container = StoreManager.makeContainer(choice: Preferences.shared.storageChoice)
        self.container = container
        _appState = State(initialValue: AppState(modelContext: container.mainContext))
    }

    var body: some Scene {
        WindowGroup(id: "main") {
            RootView()
                .environment(appState)
        }
        .modelContainer(container)

        MenuBarExtra(
            "JobTracker",
            systemImage: "briefcase.fill",
            isInserted: Binding(
                get: { appState.prefs.menuBarEnabled },
                set: { appState.prefs.menuBarEnabled = $0 }
            )
        ) {
            MenuBarContentView()
                .environment(appState)
                .modelContainer(container)
        }
        .menuBarExtraStyle(.window)

        Window("Activity Log", id: "activity-log") {
            ActivityLogView()
        }
        .keyboardShortcut("l", modifiers: [.command, .shift])

        Settings {
            SettingsView()
                .environment(appState)
        }
    }
}
