//
//  SyncState.swift
//  JobTracker
//
//  Persisted sync bookkeeping. The watermark is a timestamp (not Gmail's
//  historyId and never read/unread flags): every sync asks Gmail only for
//  mail received after the last successful sync, minus a small overlap.
//

import Foundation
import SwiftData

@Model
final class SyncState {
    var id: UUID = UUID()

    /// Received-date of the newest email that has been fully processed.
    /// Nil until the initial import/sync has happened.
    var lastSyncTimestamp: Date?

    var lastSyncDate: Date?
    var lastError: String?
    var initialImportDone: Bool = false
    var accountEmail: String?

    init() {
        self.id = UUID()
    }
}
