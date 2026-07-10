//
//  SettingsView.swift
//  JobTracker
//
//  Preferences window: General (model, menu-bar watcher, poll interval),
//  Account (Gmail, NVIDIA key, storage mode), and Data (import, full erase).
//

import SwiftUI
import SwiftData
import UniformTypeIdentifiers

struct SettingsView: View {
    var body: some View {
        TabView {
            GeneralSettings()
                .tabItem { Label("General", systemImage: "gearshape") }
            AccountSettings()
                .tabItem { Label("Account", systemImage: "person.crop.circle") }
            DataSettings()
                .tabItem { Label("Data", systemImage: "externaldrive") }
        }
        .frame(width: 480, height: 420)
    }
}

// MARK: - General

private struct GeneralSettings: View {
    @Environment(AppState.self) private var appState

    var body: some View {
        @Bindable var prefs = appState.prefs

        Form {
            Section("Menu Bar Watcher") {
                Toggle("Watch for new email in the background", isOn: Binding(
                    get: { prefs.menuBarEnabled },
                    set: { enabled in
                        prefs.menuBarEnabled = enabled
                        appState.pipeline.setMenuBarWatcher(enabled: enabled)
                    }
                ))
                Text("Off by default — JobTracker normally syncs on launch to save battery.")
                    .font(.caption).foregroundStyle(.secondary)

                if prefs.menuBarEnabled {
                    Stepper("Check every \(Int(prefs.pollInterval)) seconds",
                            value: $prefs.pollInterval, in: 30...600, step: 30)
                }
            }
        }
        .formStyle(.grouped)
    }
}

// MARK: - Account

private struct AccountSettings: View {
    @Environment(AppState.self) private var appState
    @State private var nvidiaKey = ""
    @State private var keyState: KeyState = .idle
    private enum KeyState: Equatable { case idle, validating, valid, invalid }

    var body: some View {
        @Bindable var prefs = appState.prefs

        Form {
            Section("Gmail") {
                LabeledContent("Status",
                               value: appState.auth.isSignedIn ? "Connected" : "Not connected")
                if let email = appState.auth.accountEmail {
                    LabeledContent("Account", value: email)
                }
                TextField("OAuth Client ID", text: $prefs.googleClientID,
                          prompt: Text("….apps.googleusercontent.com"))
                    .font(.callout.monospaced())
                    .autocorrectionDisabled()
                Text("Your own Google Cloud OAuth client (iOS type, Gmail API enabled). Changing it signs you out.")
                    .font(.caption).foregroundStyle(.secondary)
                HStack {
                    Button("Reconnect") { Task { try? await appState.auth.signIn() } }
                        .disabled(!appState.isGoogleConfigured)
                    Button("Sign Out", role: .destructive) { appState.auth.signOut() }
                        .disabled(!appState.auth.isSignedIn)
                }
            }

            aiProviderSection

            Section("Storage") {
                LabeledContent("Mode", value: storageDescription)
                Text("Chosen during onboarding. Erase all data to pick again.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }

    /// Provider, model, endpoint (for custom), and per-provider API key.
    private var aiProviderSection: some View {
        @Bindable var prefs = appState.prefs
        return Section("AI Provider") {
            Picker("Provider", selection: Binding(
                get: { prefs.provider },
                set: { newProvider in
                    prefs.provider = newProvider
                    // Suggest that provider's model unless the user already
                    // customized one for it this session.
                    if !newProvider.suggestedModel.isEmpty {
                        prefs.model = newProvider.suggestedModel
                    }
                    keyState = .idle
                    nvidiaKey = ""
                    appState.refreshKeyState()
                }
            )) {
                ForEach(LLMProvider.allCases) { provider in
                    Text(provider.displayName).tag(provider)
                }
            }

            TextField("Model", text: $prefs.model)
            if prefs.provider == .custom {
                TextField("Endpoint URL (…/v1/chat/completions)", text: $prefs.customBaseURL)
            }

            LabeledContent("API Key", value: appState.hasNVIDIAKey ? "Set" : "Missing")
            HStack {
                SecureField(prefs.provider.keyPlaceholder, text: $nvidiaKey)
                Button {
                    Task { await validateAndSave() }
                } label: {
                    if keyState == .validating {
                        ProgressView().controlSize(.small)
                    } else {
                        Text("Validate & Save")
                    }
                }
                .disabled(nvidiaKey.trimmingCharacters(in: .whitespaces).isEmpty
                          || keyState == .validating)
            }
            switch keyState {
            case .valid:
                Label("Key validated and saved.", systemImage: "checkmark.circle.fill")
                    .font(.caption).foregroundStyle(.green)
            case .invalid:
                Label("Key saved, but validation failed. Check the key, model id, or endpoint.",
                      systemImage: "exclamationmark.triangle.fill")
                    .font(.caption).foregroundStyle(.orange)
            default:
                Text("Each provider keeps its own key in the Keychain — switching back restores it.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    private var storageDescription: String {
        switch appState.prefs.storageChoice {
        case .iCloud: return "iCloud (synced across your devices)"
        case .local:  return "This Mac only"
        case nil:     return "Not chosen yet"
        }
    }

    private func validateAndSave() async {
        keyState = .validating
        appState.saveNVIDIAKey(nvidiaKey)
        let ok = await appState.validateNVIDIAKey()
        keyState = ok ? .valid : .invalid
        if ok { nvidiaKey = "" }
    }
}

// MARK: - Data

private struct DataSettings: View {
    @Environment(AppState.self) private var appState
    @Environment(\.modelContext) private var modelContext
    @State private var showingImporter = false
    @State private var confirmingClear = false
    @State private var confirmingErase = false
    @State private var clearedMessage: String?

    var body: some View {
        Form {
            Section("Import") {
                Button("Import Mail Archive…", systemImage: "square.and.arrow.down") {
                    showingImporter = true
                }
                .disabled(appState.pipeline.isRunning)
                Text("Bring in a Google Takeout .mbox archive. Already-imported emails are skipped automatically.")
                    .font(.caption).foregroundStyle(.secondary)
            }

            Section("Clear Data") {
                Button("Clear All Applications & Events…", role: .destructive) {
                    confirmingClear = true
                }
                .disabled(appState.pipeline.isRunning)
                if let clearedMessage {
                    Label(clearedMessage, systemImage: "checkmark.circle.fill")
                        .font(.caption).foregroundStyle(.green)
                }
                Text("Empties the tracker (applications, timelines, leads, sync history) so you can re-ingest from scratch. Keeps your Gmail connection, API keys, and settings.")
                    .font(.caption).foregroundStyle(.secondary)
            }

            Section("Danger Zone") {
                Button("Erase All Data & Restart Onboarding…", role: .destructive) {
                    confirmingErase = true
                }
                Text("Deletes every application, event, secret, and preference on this Mac. This cannot be undone.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .fileImporter(isPresented: $showingImporter,
                      allowedContentTypes: importTypes) { result in
            if case .success(let url) = result {
                appState.pipeline.importMbox(url: url)
            }
        }
        .confirmationDialog(
            "Clear all applications, events, and leads?",
            isPresented: $confirmingClear,
            titleVisibility: .visible
        ) {
            Button("Clear Data", role: .destructive) { clearData() }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("The tracker will be emptied. Gmail, API keys, and settings are kept — the next sync or archive import starts fresh.")
        }
        .confirmationDialog(
            "Erase all JobTracker data?",
            isPresented: $confirmingErase,
            titleVisibility: .visible
        ) {
            Button("Erase Everything", role: .destructive) { eraseAll() }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("All applications, timelines, keys, and settings will be removed. JobTracker will return to onboarding.")
        }
    }

    private var importTypes: [UTType] {
        var types: [UTType] = [.data]
        if let mbox = UTType(filenameExtension: "mbox") { types.insert(mbox, at: 0) }
        return types
    }

    /// In-app data clear: batch-deletes every record but leaves auth,
    /// secrets, and preferences untouched. No restart needed.
    private func clearData() {
        do {
            try modelContext.delete(model: EmailEvent.self)
            try modelContext.delete(model: JobApplication.self)
            try modelContext.delete(model: Lead.self)
            try modelContext.delete(model: SyncState.self)
            try modelContext.save()
            ActivityLog.shared.success("All data cleared — tracker is empty, ready for fresh ingestion")
            withAnimation { clearedMessage = "Data cleared." }
        } catch {
            ActivityLog.shared.error("Clear data failed: \(error.localizedDescription)")
            withAnimation { clearedMessage = nil }
        }
    }

    private func eraseAll() {
        StoreManager.performArchitectureResetIfNeeded(force: true)
        appState.auth.signOut()
        appState.hasNVIDIAKey = false
        // wipeAll (called by the forced reset) sets onboardingDone back to
        // false, which flips RootView to onboarding on the next render.
    }
}
