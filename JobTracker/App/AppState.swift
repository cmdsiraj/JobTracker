//
//  AppState.swift
//  JobTracker
//
//  Top-level coordinator holding the shared services and onboarding status,
//  injected into the SwiftUI environment.
//

import Foundation
import SwiftData

@MainActor
@Observable
final class AppState {
    let auth: GmailAuthService
    let prefs: Preferences
    let pipeline: SyncPipeline

    /// Tracks whether the ACTIVE provider has a key, so onboarding/settings
    /// can react. Refreshed on provider switches via refreshKeyState().
    var hasNVIDIAKey: Bool

    init(modelContext: ModelContext) {
        let auth = GmailAuthService()
        self.auth = auth
        self.prefs = Preferences.shared
        self.pipeline = SyncPipeline(modelContext: modelContext,
                                     auth: auth,
                                     prefs: Preferences.shared)
        self.hasNVIDIAKey = Secrets.shared.get(Preferences.shared.provider.secretKey)?.isEmpty == false

        pipeline.setMenuBarWatcher(enabled: prefs.menuBarEnabled)
    }

    var isGoogleConfigured: Bool { AppConfig.isGoogleConfigured }

    /// Onboarding shows until the wizard has been completed once.
    var needsOnboarding: Bool { !prefs.onboardingDone }

    /// Saves the key for the ACTIVE provider (Settings → Account picks it).
    func saveNVIDIAKey(_ key: String) {
        let trimmed = key.trimmingCharacters(in: .whitespacesAndNewlines)
        Secrets.shared.set(trimmed, for: prefs.provider.secretKey)
        hasNVIDIAKey = !trimmed.isEmpty
    }

    /// Re-reads key presence after the provider changed.
    func refreshKeyState() {
        hasNVIDIAKey = Secrets.shared.get(prefs.provider.secretKey)?.isEmpty == false
    }

    /// Sends a minimal completion request through the active provider to
    /// verify the stored key (and, for custom providers, the endpoint).
    func validateNVIDIAKey() async -> Bool {
        let client = NIMClient(model: prefs.model)
        do {
            _ = try await client.complete(
                messages: [.init(role: "user", content: "Reply with OK")],
                maxTokens: 10
            )
            return true
        } catch {
            ActivityLog.shared.warning("\(prefs.provider.displayName) key validation failed: \(error.localizedDescription)")
            return false
        }
    }
}
