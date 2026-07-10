//
//  ActivityLog.swift
//  JobTracker
//
//  In-app activity log so the user can watch what ingestion and sync are
//  doing (parse results, per-email classification, skips, errors). Entries
//  are also mirrored to the unified logging system for Console.app.
//

import Foundation
import os

@MainActor
@Observable
final class ActivityLog {
    static let shared = ActivityLog()

    enum Level: String {
        case info = "•"
        case success = "✓"
        case warning = "△"
        case error = "✕"
    }

    struct Entry: Identifiable {
        let id = UUID()
        let date: Date
        let level: Level
        let message: String
    }

    private(set) var entries: [Entry] = []
    private static let cap = 5000
    private let logger = Logger(subsystem: "com.jobtracker", category: "activity")

    private init() {}

    func info(_ message: String) { append(.info, message) }
    func success(_ message: String) { append(.success, message) }
    func warning(_ message: String) { append(.warning, message) }
    func error(_ message: String) { append(.error, message) }

    private func append(_ level: Level, _ message: String) {
        entries.append(Entry(date: Date(), level: level, message: message))
        if entries.count > Self.cap {
            entries.removeFirst(entries.count - Self.cap)
        }
        switch level {
        case .error:   logger.error("\(message, privacy: .public)")
        case .warning: logger.warning("\(message, privacy: .public)")
        default:       logger.info("\(message, privacy: .public)")
        }
    }

    func clear() { entries.removeAll() }

    /// Plain-text dump for the copy button.
    var text: String {
        let formatter = DateFormatter()
        formatter.dateFormat = "HH:mm:ss"
        return entries.map { "[\(formatter.string(from: $0.date))] \($0.level.rawValue) \($0.message)" }
            .joined(separator: "\n")
    }
}
