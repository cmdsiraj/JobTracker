//
//  StoreManager.swift
//  JobTracker
//
//  Creates the SwiftData container for the storage location the user chose
//  during onboarding (their own iCloud account, or this Mac only), and
//  performs the one-time wipe of everything the previous architecture wrote.
//
//  There is deliberately no shared/central database: each user's data lives
//  entirely in their iCloud container or on their machine. The same model
//  code runs unchanged on iOS.
//

import Foundation
import SwiftData

@MainActor
enum StoreManager {

    static let schema = Schema([
        JobApplication.self,
        EmailEvent.self,
        SyncState.self,
        Lead.self,
    ])

    /// v2 store file, separate from the abandoned v1 "default.store".
    private static var storeURL: URL {
        URL.applicationSupportDirectory.appending(path: "JobTrackerV2.store")
    }

    /// Builds the container for the user's storage choice. Before onboarding
    /// completes (choice == nil) a local container is used; if the user picks
    /// iCloud, the same store is re-opened with CloudKit mirroring, which
    /// uploads existing local records automatically.
    static func makeContainer(choice: StorageChoice?) -> ModelContainer {
        if choice == .iCloud {
            do {
                let config = ModelConfiguration(schema: schema,
                                                url: storeURL,
                                                cloudKitDatabase: .automatic)
                return try ModelContainer(for: schema, configurations: [config])
            } catch {
                // Missing iCloud entitlement/account: fall back to local so
                // the app still works; Settings shows the effective mode.
                ActivityLog.shared.warning("iCloud store unavailable (\(error.localizedDescription)) — using local storage")
            }
        }
        do {
            let config = ModelConfiguration(schema: schema,
                                            url: storeURL,
                                            cloudKitDatabase: .none)
            return try ModelContainer(for: schema, configurations: [config])
        } catch {
            fatalError("Could not create data store: \(error)")
        }
    }

    // MARK: - Architecture reset

    /// One-time transition from the v1 architecture: removes the old store,
    /// legacy keychain items, and v1 defaults. Runs before the container is
    /// created. Also used (with `force`) by Settings → Erase All Data.
    static func performArchitectureResetIfNeeded(force: Bool = false) {
        guard force || Preferences.needsArchitectureReset() else { return }

        // 1. Old + new SwiftData stores (".store", "-shm", "-wal" siblings).
        let fileManager = FileManager.default
        let candidates = [
            URL.applicationSupportDirectory.appending(path: "default.store"),
            storeURL
        ]
        for base in candidates {
            for suffix in ["", "-shm", "-wal"] {
                try? fileManager.removeItem(at: URL(fileURLWithPath: base.path + suffix))
            }
        }

        // 2. Keychain: a version bump (or explicit erase) is a true fresh
        // start — all secrets go, including Gmail tokens and API keys.
        Secrets.shared.wipeAll()

        // 3. Defaults.
        Preferences.wipeLegacyDefaults()
        Preferences.shared.wipeAll()

        Preferences.markArchitectureReset()
    }
}
