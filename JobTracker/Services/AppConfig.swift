//
//  AppConfig.swift
//  JobTracker
//
//  App-level (not user-level) configuration: OAuth client identity, API
//  endpoints, scopes, defaults. Nothing personal lives here — user secrets
//  are in the data-protection Keychain (Secrets.swift) and user data is in
//  their chosen store (iCloud or local).
//

import Foundation

enum AppConfig {

    // MARK: - Google OAuth (identifies the app, not the user)

    /// OAuth 2.0 Client ID (type: iOS) from the *user's own* Google Cloud
    /// project, entered during onboarding (Settings → Account to change).
    /// Deliberately NOT hardcoded: every user of this open-source app brings
    /// their own client so nobody shares anyone else's Google API quota.
    static var googleClientID: String { Preferences.shared.googleClientID }

    static let clientIDSuffix = ".apps.googleusercontent.com"

    static var reversedClientID: String {
        let bare = googleClientID.hasSuffix(clientIDSuffix)
            ? String(googleClientID.dropLast(clientIDSuffix.count))
            : googleClientID
        return "com.googleusercontent.apps.\(bare)"
    }

    static var redirectURI: String { "\(reversedClientID):/oauth2redirect" }
    static var callbackURLScheme: String { reversedClientID }

    static let authorizationEndpoint = URL(string: "https://accounts.google.com/o/oauth2/v2/auth")!
    static let tokenEndpoint = URL(string: "https://oauth2.googleapis.com/token")!
    static let gmailScope = "https://www.googleapis.com/auth/gmail.readonly"

    static var isGoogleConfigured: Bool {
        googleClientID.hasSuffix(clientIDSuffix) && googleClientID.count > clientIDSuffix.count
    }

    // MARK: - Gmail API

    static let gmailBaseURL = URL(string: "https://gmail.googleapis.com/gmail/v1")!

    // MARK: - NVIDIA NIM (OpenAI-compatible)

    static let nimChatCompletionsURL = URL(string: "https://integrate.api.nvidia.com/v1/chat/completions")!
    static let defaultModel = "nvidia/nemotron-3-ultra-550b-a55b"

    /// Emails classified per LLM request. Batching cuts request count and
    /// free-tier credit burn roughly by this factor.
    static let classificationBatchSize = 8

    // MARK: - Sync

    /// Safety overlap subtracted from the last-sync watermark so boundary
    /// emails are never missed (duplicates are filtered by message id).
    static let syncOverlap: TimeInterval = 3600

    /// Optional menu-bar watcher poll interval.
    static let defaultPollInterval: TimeInterval = 120

    // MARK: - Reset

    /// Bump to force a one-time wipe of data written by older architectures
    /// (store, defaults, AND keychain — a bump means a true fresh start).
    static let architectureVersion = 3
}
