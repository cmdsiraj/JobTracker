//
//  Secrets.swift
//  JobTracker
//
//  Secret storage that (a) never triggers repeated keychain password
//  prompts and (b) actually persists across restarts regardless of how the
//  app is signed:
//
//  - Writes try the data-protection keychain first (silent, modern; works
//    when the app is signed with a real identity).
//  - Ad-hoc signed builds (no application-identifier entitlement, so the
//    data-protection keychain rejects writes with -34018) fall back to an
//    owner-only file inside the app's sandbox container. Files never show
//    keychain ACL prompts — the previous legacy-keychain fallback demanded
//    the login password on every app update because each ad-hoc build has
//    a new code identity.
//  - Every failure is logged instead of silently dropping credentials.
//  - Reads are cached; storage is hit once per launch.
//

import Foundation
import Security
import os

enum SecretKey: String, CaseIterable, Codable {
    case nvidiaAPIKey
    case openAIKey
    case openRouterKey
    case groqKey
    case customLLMKey
    case gmailAccessToken
    case gmailRefreshToken
    case gmailAccessTokenExpiry
}

final class Secrets: @unchecked Sendable {
    static let shared = Secrets()

    private static let service = "com.jobtracker.v2.secrets"
    private static let legacyService = "com.jobtracker.secrets"
    private static let blobAccount = "all-secrets"

    private let logger = Logger(subsystem: "com.jobtracker", category: "secrets")
    private let lock = NSLock()

    /// nil until the blob has been loaded once this launch.
    private var cache: [SecretKey: String]?

    private init() {}

    // MARK: - Public API

    func get(_ key: SecretKey) -> String? {
        lock.lock()
        defer { lock.unlock() }
        loadIfNeededLocked()
        let value = cache?[key]
        return (value?.isEmpty == false) ? value : nil
    }

    func set(_ value: String?, for key: SecretKey) {
        lock.lock()
        defer { lock.unlock() }
        loadIfNeededLocked()
        var blob = cache ?? [:]
        if let value, !value.isEmpty {
            blob[key] = value
        } else {
            blob.removeValue(forKey: key)
        }
        cache = blob
        persistLocked(blob)
    }

    /// Removes every secret this app has ever stored, across all storage
    /// paths and both architectures.
    func wipeAll() {
        lock.lock()
        defer { lock.unlock() }
        cache = [:]
        try? FileManager.default.removeItem(at: Self.fileURL)
        for service in [Self.service, Self.legacyService] {
            for dataProtection in [true, false] {
                var query: [String: Any] = [
                    kSecClass as String: kSecClassGenericPassword,
                    kSecAttrService as String: service
                ]
                if dataProtection {
                    query[kSecUseDataProtectionKeychain as String] = true
                }
                SecItemDelete(query as CFDictionary)
            }
        }
    }

    /// Kept for StoreManager compatibility; removes v1 items only.
    func purgeLegacyItems() {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: Self.legacyService
        ]
        SecItemDelete(query as CFDictionary)
    }

    // MARK: - Blob load/persist (call with lock held)

    private func loadIfNeededLocked() {
        guard cache == nil else { return }

        // Preferred: data-protection keychain (silent; works when the app
        // is signed with a real identity).
        if let data = readBlob(dataProtection: true),
           let blob = try? JSONDecoder().decode([SecretKey: String].self, from: data) {
            cache = blob
            return
        }

        // Fallback: protected file in the sandbox container. Files never
        // show keychain ACL prompts — the legacy-keychain fallback used to
        // prompt for the login password on EVERY app update, because each
        // ad-hoc build has a new code identity.
        if let data = try? Data(contentsOf: Self.fileURL),
           let blob = try? JSONDecoder().decode([SecretKey: String].self, from: data) {
            cache = blob
            purgePromptingItemsOnce()
            return
        }

        // One-time migration from the legacy keychain item written by the
        // previous build. This read may show ONE final password prompt;
        // afterwards the item is deleted and never touched again. If the
        // user denies it, they just re-enter credentials in Settings.
        if let data = readBlob(dataProtection: false),
           let blob = try? JSONDecoder().decode([SecretKey: String].self, from: data) {
            cache = blob
            persistLocked(blob)
            purgePromptingItemsOnce()
            return
        }

        cache = [:]
        purgePromptingItemsOnce()
    }

    private func persistLocked(_ blob: [SecretKey: String]) {
        guard let data = try? JSONEncoder().encode(blob) else { return }

        // Try the silent, modern keychain first (properly signed builds).
        if writeBlob(data, dataProtection: true) == errSecSuccess {
            try? FileManager.default.removeItem(at: Self.fileURL)
            return
        }

        // Ad-hoc builds: owner-only file inside the app's sandbox container.
        do {
            try data.write(to: Self.fileURL, options: [.atomic])
            try FileManager.default.setAttributes([.posixPermissions: 0o600],
                                                  ofItemAtPath: Self.fileURL.path)
        } catch {
            logger.error("Secret store write FAILED: \(error.localizedDescription)")
            Task { @MainActor in
                ActivityLog.shared.error("Could not save credentials (\(error.localizedDescription)). They may be lost when the app quits.")
            }
        }
    }

    /// Owner-only secrets file, protected by the app sandbox + POSIX 600.
    private static var fileURL: URL {
        URL.applicationSupportDirectory.appending(path: "secrets.v2")
    }

    /// Deletes the legacy keychain items that caused password prompts.
    /// SecItemDelete never prompts, so this is silent.
    private var purgedPromptingItems = false
    private func purgePromptingItemsOnce() {
        guard !purgedPromptingItems else { return }
        purgedPromptingItems = true
        for service in [Self.service, Self.legacyService] {
            let query: [String: Any] = [
                kSecClass as String: kSecClassGenericPassword,
                kSecAttrService as String: service
            ]
            SecItemDelete(query as CFDictionary)
        }
    }

    // MARK: - Keychain primitives

    private func blobQuery(dataProtection: Bool) -> [String: Any] {
        var query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: Self.service,
            kSecAttrAccount as String: Self.blobAccount
        ]
        if dataProtection {
            query[kSecUseDataProtectionKeychain as String] = true
        }
        return query
    }

    private func readBlob(dataProtection: Bool) -> Data? {
        var query = blobQuery(dataProtection: dataProtection)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        guard status == errSecSuccess, let data = result as? Data else { return nil }
        return data
    }

    private func writeBlob(_ data: Data, dataProtection: Bool) -> OSStatus {
        let update = SecItemUpdate(
            blobQuery(dataProtection: dataProtection) as CFDictionary,
            [kSecValueData as String: data] as CFDictionary
        )
        if update == errSecSuccess { return errSecSuccess }
        if update != errSecItemNotFound, !dataProtection {
            // Legacy update failed for a real reason (e.g. ACL from an older
            // build); recreate the item.
            SecItemDelete(blobQuery(dataProtection: false) as CFDictionary)
        }
        var insert = blobQuery(dataProtection: dataProtection)
        insert[kSecValueData as String] = data
        insert[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
        return SecItemAdd(insert as CFDictionary, nil)
    }

}
