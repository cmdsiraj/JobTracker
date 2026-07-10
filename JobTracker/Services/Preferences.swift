//
//  Preferences.swift
//  JobTracker
//
//  Non-secret user preferences, backed by UserDefaults. Secrets go in the
//  data-protection Keychain (Secrets.swift).
//

import Foundation

/// Where the user chose to keep their data during onboarding.
enum StorageChoice: String {
    case iCloud
    case local
}

/// LLM providers the classifier can talk to. All expose OpenAI-compatible
/// chat-completions endpoints; each keeps its own API key in the Keychain,
/// so switching providers never loses a key.
enum LLMProvider: String, CaseIterable, Identifiable {
    case nvidia
    case openAI
    case openRouter
    case groq
    case custom

    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .nvidia:     return "NVIDIA NIM"
        case .openAI:     return "OpenAI"
        case .openRouter: return "OpenRouter"
        case .groq:       return "Groq"
        case .custom:     return "Custom (OpenAI-compatible)"
        }
    }

    /// Fixed endpoint; nil means the user supplies one (custom).
    var baseURL: URL? {
        switch self {
        case .nvidia:     return URL(string: "https://integrate.api.nvidia.com/v1/chat/completions")
        case .openAI:     return URL(string: "https://api.openai.com/v1/chat/completions")
        case .openRouter: return URL(string: "https://openrouter.ai/api/v1/chat/completions")
        case .groq:       return URL(string: "https://api.groq.com/openai/v1/chat/completions")
        case .custom:     return nil
        }
    }

    var suggestedModel: String {
        switch self {
        case .nvidia:     return AppConfig.defaultModel
        case .openAI:     return "gpt-5.2-mini"
        case .openRouter: return "meta-llama/llama-4-maverick"
        case .groq:       return "llama-4-maverick-17b"
        case .custom:     return ""
        }
    }

    var secretKey: SecretKey {
        switch self {
        case .nvidia:     return .nvidiaAPIKey
        case .openAI:     return .openAIKey
        case .openRouter: return .openRouterKey
        case .groq:       return .groqKey
        case .custom:     return .customLLMKey
        }
    }

    var keyPlaceholder: String {
        switch self {
        case .nvidia:     return "nvapi-…"
        case .openAI:     return "sk-…"
        case .openRouter: return "sk-or-…"
        case .groq:       return "gsk_…"
        case .custom:     return "API key"
        }
    }
}

@Observable
final class Preferences {
    static let shared = Preferences()

    private let defaults = UserDefaults.standard

    private enum Keys {
        static let googleClientID = "v2.googleClientID"
        static let model = "v2.model"
        static let provider = "v2.llmProvider"
        static let customBaseURL = "v2.customBaseURL"
        static let pollInterval = "v2.pollInterval"
        static let menuBarEnabled = "v2.menuBarEnabled"
        static let storageChoice = "v2.storageChoice"
        static let onboardingDone = "v2.onboardingDone"
        static let architectureVersion = "v2.architectureVersion"
    }

    /// The user's own Google OAuth Client ID, entered during onboarding.
    /// Not a secret — client IDs are public identifiers and the PKCE flow
    /// uses no client secret — but it IS personal quota, so it's never
    /// hardcoded in the (open-source) app.
    var googleClientID: String {
        didSet { defaults.set(googleClientID, forKey: Keys.googleClientID) }
    }

    var model: String {
        didSet { defaults.set(model, forKey: Keys.model) }
    }

    var provider: LLMProvider {
        didSet { defaults.set(provider.rawValue, forKey: Keys.provider) }
    }

    /// Endpoint for the `.custom` provider.
    var customBaseURL: String {
        didSet { defaults.set(customBaseURL, forKey: Keys.customBaseURL) }
    }

    /// Effective chat-completions endpoint for the active provider.
    var effectiveBaseURL: URL? {
        provider.baseURL ?? URL(string: customBaseURL)
    }

    /// Optional near-real-time watcher. Off by default: the app is
    /// launch-sync based to minimize battery/CPU (user-toggleable).
    var menuBarEnabled: Bool {
        didSet { defaults.set(menuBarEnabled, forKey: Keys.menuBarEnabled) }
    }

    var pollInterval: TimeInterval {
        didSet { defaults.set(pollInterval, forKey: Keys.pollInterval) }
    }

    var storageChoice: StorageChoice? {
        didSet { defaults.set(storageChoice?.rawValue, forKey: Keys.storageChoice) }
    }

    var onboardingDone: Bool {
        didSet { defaults.set(onboardingDone, forKey: Keys.onboardingDone) }
    }

    private init() {
        self.googleClientID = defaults.string(forKey: Keys.googleClientID) ?? ""
        self.model = defaults.string(forKey: Keys.model) ?? AppConfig.defaultModel
        self.provider = defaults.string(forKey: Keys.provider)
            .flatMap(LLMProvider.init) ?? .nvidia
        self.customBaseURL = defaults.string(forKey: Keys.customBaseURL) ?? ""
        self.menuBarEnabled = defaults.bool(forKey: Keys.menuBarEnabled)
        let interval = defaults.double(forKey: Keys.pollInterval)
        self.pollInterval = interval > 0 ? interval : AppConfig.defaultPollInterval
        self.storageChoice = defaults.string(forKey: Keys.storageChoice).flatMap(StorageChoice.init)
        self.onboardingDone = defaults.bool(forKey: Keys.onboardingDone)
    }

    // MARK: - One-time architecture reset

    /// True when this launch must wipe everything written by the previous
    /// architecture (legacy keychain items, old store, old defaults).
    static func needsArchitectureReset() -> Bool {
        UserDefaults.standard.integer(forKey: Keys.architectureVersion) < AppConfig.architectureVersion
    }

    static func markArchitectureReset() {
        UserDefaults.standard.set(AppConfig.architectureVersion, forKey: Keys.architectureVersion)
    }

    /// Removes every v1 default (they all lacked the "v2." prefix).
    static func wipeLegacyDefaults() {
        let defaults = UserDefaults.standard
        for key in defaults.dictionaryRepresentation().keys where key.hasPrefix("pref.") {
            defaults.removeObject(forKey: key)
        }
    }

    /// Full reset: back to first-launch state.
    func wipeAll() {
        for key in defaults.dictionaryRepresentation().keys
        where key.hasPrefix("v2.") || key.hasPrefix("pref.") {
            defaults.removeObject(forKey: key)
        }
        googleClientID = ""
        model = AppConfig.defaultModel
        provider = .nvidia
        customBaseURL = ""
        menuBarEnabled = false
        pollInterval = AppConfig.defaultPollInterval
        storageChoice = nil
        onboardingDone = false
    }
}
