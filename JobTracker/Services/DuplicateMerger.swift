//
//  DuplicateMerger.swift
//  JobTracker
//
//  Post-sync cleanup: collapses applications that are really the same
//  process — same company with token-similar roles (or one side missing a
//  role, close in time). Runs automatically at the end of every sync/import
//  and repairs duplicates that earlier, stricter matching created.
//

import Foundation
import SwiftData

@MainActor
enum DuplicateMerger {

    private static let proximityWindow: TimeInterval = 120 * 24 * 3600

    /// Merges duplicates in place. Returns the number of applications merged
    /// away.
    @discardableResult
    static func run(in context: ModelContext) throws -> Int {
        let applications = try context.fetch(FetchDescriptor<JobApplication>())
        let groups = Dictionary(grouping: applications.filter { !$0.companyKey.isEmpty },
                                by: \.companyKey)
        var merged = 0

        for (_, group) in groups where group.count > 1 {
            let ordered = group.sorted {
                ($0.appliedDate ?? $0.lastUpdated) < ($1.appliedDate ?? $1.lastUpdated)
            }

            // Pass 1 — records WITH roles: merge only on real role similarity.
            // Multiple distinct roles at one company (even applied the same
            // day) are separate applications and must never collapse.
            var survivors: [JobApplication] = []
            var roleless: [JobApplication] = []
            for app in ordered {
                if ApplicationMatcher.roleTokens(app.roleTitle).isEmpty {
                    roleless.append(app)
                    continue
                }
                if let target = survivors.first(where: { rolesMatch($0, app) }) {
                    merge(app, into: target, context: context)
                    merged += 1
                } else {
                    survivors.append(app)
                }
            }

            // Pass 2 — role-less records: merge only when the target is
            // UNAMBIGUOUS (exactly one candidate in the time window).
            // Ambiguous ones are kept and flagged for manual review instead
            // of being guessed into the wrong application.
            for app in roleless {
                let anchor = app.appliedDate ?? app.lastUpdated
                let inWindow = survivors.filter {
                    abs(($0.appliedDate ?? $0.lastUpdated).timeIntervalSince(anchor)) < proximityWindow
                }
                if inWindow.count == 1 {
                    merge(app, into: inWindow[0], context: context)
                    merged += 1
                } else if inWindow.count > 1 {
                    app.needsReview = true
                    survivors.append(app)
                } else {
                    survivors.append(app)
                }
            }
        }

        if merged > 0 {
            try context.save()
            ActivityLog.shared.success("Merged \(merged) duplicate application(s)")
        }
        return merged
    }

    /// Same company (guaranteed by grouping) + genuinely similar roles.
    private static func rolesMatch(_ a: JobApplication, _ b: JobApplication) -> Bool {
        ApplicationMatcher.similarity(
            ApplicationMatcher.roleTokens(a.roleTitle),
            ApplicationMatcher.roleTokens(b.roleTitle)
        ) >= 0.55
    }

    private static func merge(_ source: JobApplication,
                              into target: JobApplication,
                              context: ModelContext) {
        // Move the communication log.
        for event in source.events ?? [] {
            event.application = target
        }

        // Union thread ids so future thread matches land on the survivor.
        for threadId in source.threadIds where !target.threadIds.contains(threadId) {
            target.threadIds.append(threadId)
        }

        // Prefer filled-in fields; keep the earliest applied date and the
        // status of whichever record heard from the company most recently.
        if target.roleTitle.isEmpty { target.roleTitle = source.roleTitle }
        if target.cycle.isEmpty { target.cycle = source.cycle }
        if target.location == nil { target.location = source.location }
        if target.source == nil { target.source = source.source }
        if target.contactEmail == nil { target.contactEmail = source.contactEmail }
        if target.nextAction.isEmpty { target.nextAction = source.nextAction }
        for tag in source.tags where !target.tags.contains(tag) {
            target.tags.append(tag)
        }
        if !source.notes.isEmpty {
            target.notes = target.notes.isEmpty
                ? source.notes
                : target.notes + "\n" + source.notes
        }

        target.appliedDate = [target.appliedDate, source.appliedDate]
            .compactMap { $0 }.min()
        if (source.lastEmailDate ?? .distantPast) > (target.lastEmailDate ?? .distantPast) {
            target.lastEmailDate = source.lastEmailDate
            if source.status != target.status {
                let change = EmailEvent(kind: .statusChange,
                                        text: "\(target.status.displayName) → \(source.status.displayName)",
                                        date: source.lastEmailDate ?? Date())
                change.application = target
                context.insert(change)
                target.status = source.status
            }
        }
        target.lastUpdated = max(target.lastUpdated, source.lastUpdated)
        target.needsReview = target.needsReview || source.needsReview

        let note = EmailEvent(kind: .note,
                              text: "Merged duplicate entry (\(source.company) — \(source.roleTitle.isEmpty ? "no role" : source.roleTitle))")
        note.application = target
        context.insert(note)

        context.delete(source)
    }
}
