//
//  SyncPipeline.swift
//  JobTracker
//
//  The staged sync/import pipeline. One shared processing path turns
//  FetchedMessages (from the Gmail API or a Takeout mbox archive) into
//  JobApplication / EmailEvent records: dedupe by message id, classify in
//  batches with the LLM, match into applications, advance statuses with an
//  auto-logged history trail, save.
//
//  Sync is launch-triggered (timestamp watermark, inbox + sent) with an
//  optional menu-bar poll loop. Archive import runs in two phases: a local
//  parse (no LLM calls), then — after the user confirms a date scope —
//  classification through the shared path.
//

import Foundation
import SwiftData

@MainActor
@Observable
final class SyncPipeline {

    // MARK: - Stage

    enum Stage: Equatable {
        case idle
        case connecting
        case downloading(found: Int)
        case parsing(percent: Int)          // mbox archive reading
        case classifying(done: Int, total: Int)
        case matching
        case statistics
        case saving
        case finished(updates: Int)
        case failed(String)
    }

    var stage: Stage = .idle

    /// True while any working stage is active.
    var isRunning: Bool {
        switch stage {
        case .idle, .finished, .failed: return false
        case .connecting, .downloading, .parsing, .classifying,
             .matching, .statistics, .saving: return true
        }
    }

    var newlyUpdatedCount = 0
    var lastError: String?

    // MARK: - Dependencies

    private let modelContext: ModelContext
    private let auth: GmailAuthService
    private let prefs: Preferences
    private let log = ActivityLog.shared

    /// Only persist classifications we're reasonably confident about.
    private let confidenceThreshold = 0.55

    /// Consecutive whole-batch failures tolerated before aborting a run.
    private let maxConsecutiveBatchFailures = 5

    private var importTask: Task<Void, Never>?
    private var watcherTask: Task<Void, Never>?
    private var launchSyncStarted = false

    init(modelContext: ModelContext, auth: GmailAuthService, prefs: Preferences) {
        self.modelContext = modelContext
        self.auth = auth
        self.prefs = prefs
    }

    // MARK: - Launch sync

    /// Call once from the UI's `.task`: kicks off a sync on launch when the
    /// user finished onboarding and is signed in. No-ops on later calls.
    func syncOnLaunchIfNeeded() {
        guard !launchSyncStarted, prefs.onboardingDone, auth.isSignedIn else { return }
        launchSyncStarted = true
        Task { await self.syncNow() }
    }

    // MARK: - Sync

    func syncNow() async {
        guard !isRunning else { return }
        guard auth.isSignedIn, AppConfig.isGoogleConfigured else {
            log.info("Sync skipped: not connected to Gmail")
            return
        }

        lastError = nil
        newlyUpdatedCount = 0
        stage = .connecting
        log.info("Sync started")

        let api = GmailAPIClient(auth: auth)

        do {
            let state = try syncState()
            if state.accountEmail == nil {
                state.accountEmail = try? await api.profile().emailAddress
            }

            // Recorded BEFORE downloading so mail arriving mid-sync falls
            // after the next watermark (overlap covers the boundary).
            let syncStart = Date()

            let messageIDs: [String]
            if let watermark = state.lastSyncTimestamp {
                messageIDs = try await api.messageIDs(
                    after: watermark.addingTimeInterval(-AppConfig.syncOverlap))
            } else if state.initialImportDone {
                // Archive already covered history; just establish the watermark.
                messageIDs = []
            } else {
                messageIDs = try await api.recentMessageIDs(days: 30)
            }

            stage = .downloading(found: messageIDs.count)
            log.info("Sync: \(messageIDs.count) message id(s) listed")

            // Single fetch of stored ids for the whole run (also reused by
            // the shared processing path).
            let existingIds = try existingMessageIds()
            var messages: [FetchedMessage] = []
            for id in messageIDs where !existingIds.contains(id) {
                do {
                    messages.append(try await api.message(id: id))
                } catch {
                    log.warning("Could not fetch message \(id): \(error.localizedDescription)")
                }
            }

            try await process(messages, existingIds: existingIds)

            state.lastSyncTimestamp = syncStart
            state.lastSyncDate = Date()
            state.lastError = nil
            try modelContext.save()

            stage = .finished(updates: newlyUpdatedCount)
            log.success("Sync finished: \(newlyUpdatedCount) update(s) from \(messages.count) new email(s)")
        } catch {
            lastError = error.localizedDescription
            stage = .failed(error.localizedDescription)
            log.error("Sync failed: \(error.localizedDescription)")
            try? modelContext.save()
        }
    }

    // MARK: - Mail archive import (Google Takeout mbox)

    /// Import runs in two phases: a fast local parse of the whole archive,
    /// then — after the user confirms a date scope — LLM classification.
    /// The pause exists because each candidate batch costs an NIM request
    /// and archives can contain thousands of candidates.
    struct PendingImport {
        let candidates: [FetchedMessage]
        let summary: MboxParseSummary

        /// Start of the current recruiting cycle (Aug 1 of the most recent
        /// August).
        static var currentCycleStart: Date {
            let now = Date()
            let calendar = Calendar.current
            let year = calendar.component(.year, from: now)
            let cycleYear = calendar.component(.month, from: now) >= 8 ? year : year - 1
            return DateComponents(calendar: calendar, year: cycleYear, month: 8, day: 1).date ?? now
        }

        var currentCycleCount: Int {
            candidates.filter { $0.date >= Self.currentCycleStart }.count
        }
        var lastYearCount: Int {
            let cutoff = Calendar.current.date(byAdding: .year, value: -1, to: Date()) ?? Date()
            return candidates.filter { $0.date >= cutoff }.count
        }
    }

    enum ImportScope {
        case currentCycle
        case lastYear
        case everything

        func filter(_ messages: [FetchedMessage]) -> [FetchedMessage] {
            switch self {
            case .currentCycle:
                return messages.filter { $0.date >= PendingImport.currentCycleStart }
            case .lastYear:
                let cutoff = Calendar.current.date(byAdding: .year, value: -1, to: Date()) ?? Date()
                return messages.filter { $0.date >= cutoff }
            case .everything:
                return messages
            }
        }
    }

    /// Set after the parse phase; the UI presents a scope confirmation.
    var pendingImport: PendingImport?

    /// Phase 1: parse the archive (no LLM calls, nothing leaves the Mac).
    func importMbox(url: URL) {
        guard !isRunning, importTask == nil else { return }
        importTask = Task { [weak self] in
            await self?.runParsePhase(url: url)
            self?.importTask = nil
        }
    }

    /// Phase 2: user picked a scope; classify those candidates.
    func confirmImport(scope: ImportScope) {
        guard let pending = pendingImport, !isRunning, importTask == nil else { return }
        pendingImport = nil
        let messages = scope.filter(pending.candidates)
        importTask = Task { [weak self] in
            await self?.runClassifyPhase(messages: messages)
            self?.importTask = nil
        }
    }

    func discardPendingImport() {
        pendingImport = nil
        stage = .idle
        log.info("Import discarded before classification")
    }

    func cancelImport() {
        importTask?.cancel()
    }

    private func runParsePhase(url: URL) async {
        lastError = nil
        stage = .parsing(percent: 0)

        let hasScope = url.startAccessingSecurityScopedResource()
        defer { if hasScope { url.stopAccessingSecurityScopedResource() } }

        log.info("Import started: \(url.lastPathComponent)")
        do {
            // Heavy parsing runs off the main actor; progress hops back.
            let (candidates, summary) = try await Task.detached(priority: .userInitiated) { [weak self] in
                var lastPercent = -1
                return try MboxParser.collectJobCandidates(url: url) { fraction in
                    let percent = min(100, Int(fraction * 100))
                    guard percent != lastPercent else { return }
                    lastPercent = percent
                    Task { @MainActor [weak self] in
                        self?.reportParseProgress(percent)
                    }
                }
            }.value

            pendingImport = PendingImport(candidates: candidates, summary: summary)
            stage = .idle
            log.info("Parse done: \(summary.totalMessages) emails, \(summary.skippedSpamTrash) spam/trash skipped, \(summary.candidates) job-related candidates")
        } catch {
            lastError = error.localizedDescription
            stage = .failed(error.localizedDescription)
            log.error("Archive parse failed: \(error.localizedDescription)")
        }
    }

    /// Only advances the parse stage; ignores stragglers after completion.
    private func reportParseProgress(_ percent: Int) {
        if case .parsing = stage { stage = .parsing(percent: percent) }
    }

    private func runClassifyPhase(messages: [FetchedMessage]) async {
        lastError = nil
        newlyUpdatedCount = 0
        log.info("Import classification started: \(messages.count) candidate(s), model \(prefs.model)")

        do {
            let existingIds = try existingMessageIds()
            try await process(messages, existingIds: existingIds)

            // The archive replaces the API backfill window.
            let state = try syncState()
            state.initialImportDone = true
            state.lastSyncTimestamp = messages.map(\.date).max() ?? Date()
            state.lastSyncDate = Date()
            try modelContext.save()

            stage = .finished(updates: newlyUpdatedCount)
            log.success("Import finished: \(newlyUpdatedCount) application update(s) from \(messages.count) email(s)")
        } catch {
            lastError = error.localizedDescription
            stage = .failed(error.localizedDescription)
            log.error("Import failed: \(error.localizedDescription)")
            try? modelContext.save()
        }
    }

    // MARK: - Menu bar watcher

    /// Optional near-real-time poll loop; off by default (launch-sync keeps
    /// battery/CPU use minimal).
    func setMenuBarWatcher(enabled: Bool) {
        if enabled {
            guard watcherTask == nil else { return }
            log.info("Menu bar watcher enabled (every \(Int(prefs.pollInterval))s)")
            watcherTask = Task { [weak self] in
                while !Task.isCancelled {
                    guard let self else { return }
                    try? await Task.sleep(for: .seconds(self.prefs.pollInterval))
                    if Task.isCancelled { return }
                    if !self.isRunning { await self.syncNow() }
                }
            }
        } else {
            guard watcherTask != nil else { return }
            watcherTask?.cancel()
            watcherTask = nil
            log.info("Menu bar watcher disabled")
        }
    }

    // MARK: - Shared processing (sync + import)

    /// Classifies `messages` (oldest first, batched) and upserts every
    /// confident job-related result. `existingIds` must come from a single
    /// EmailEvent fetch performed by the caller for this run.
    private func process(_ messages: [FetchedMessage], existingIds: Set<String>) async throws {
        // Dedupe against the store and within the run, then oldest first so
        // status progression follows real chronology.
        var seen = existingIds
        let pending = messages
            .filter { seen.insert($0.id).inserted }
            .sorted { $0.date < $1.date }

        let skipped = messages.count - pending.count
        if skipped > 0 { log.info("Skipped \(skipped) already-stored email(s)") }

        let total = pending.count
        stage = .classifying(done: 0, total: total)

        // Matcher indexes are built once per run from a single fetch.
        let applications = try modelContext.fetch(FetchDescriptor<JobApplication>())
        let matcher = ApplicationMatcher(existing: applications)

        if total > 0 {
            let classifier = EmailClassifier(nim: NIMClient(model: prefs.model))
            var done = 0
            var consecutiveBatchFailures = 0
            var index = 0

            while index < pending.count {
                if Task.isCancelled {
                    log.warning("Cancelled after \(done)/\(total) — \(newlyUpdatedCount) update(s) already saved")
                    break
                }
                let upper = min(index + AppConfig.classificationBatchSize, pending.count)
                let batch = Array(pending[index..<upper])
                index = upper

                do {
                    let results = try await classifyWithRetry(batch, classifier: classifier)
                    consecutiveBatchFailures = 0
                    for (position, message) in batch.enumerated() {
                        guard let result = results[position] else { continue }
                        if result.isJobRelated, result.confidence >= confidenceThreshold {
                            upsert(message: message, result: result, matcher: matcher)
                            newlyUpdatedCount += 1
                            let status = result.applicationStatus
                                ?? (message.isOutgoing ? .outreach : .applied)
                            let cycle = result.cycle.map { " [\($0)]" } ?? ""
                            log.success("\(result.company) — \(result.role) → \(status.displayName)\(cycle)")
                        } else if result.isJobRelated {
                            log.warning("Low confidence (\(String(format: "%.2f", result.confidence))): \(message.subject.prefix(60))")
                        } else {
                            log.info("Not job-related: \(message.subject.prefix(60))")
                        }
                    }
                } catch NIMError.missingAPIKey {
                    throw NIMError.missingAPIKey  // abort: user must add a key
                } catch {
                    consecutiveBatchFailures += 1
                    log.error("Batch \(done / max(AppConfig.classificationBatchSize, 1) + 1) failed (\(consecutiveBatchFailures) in a row): \(error.localizedDescription)")
                    if consecutiveBatchFailures >= maxConsecutiveBatchFailures {
                        log.error("stopping — likely out of API credits; re-run later to resume")
                        break
                    }
                }

                done += batch.count
                stage = .classifying(done: done, total: total)
                try? modelContext.save()
                // Pacing under the 40 req/min free tier is enforced by
                // RequestThrottle inside NIMClient — no extra sleep needed.
            }
        }

        // Post-pass: collapse any duplicates that separate emails still
        // managed to create (and repair ones from earlier runs).
        stage = .matching
        _ = try? DuplicateMerger.run(in: modelContext)
        stage = .statistics
        try? await Task.sleep(for: .milliseconds(250))
        stage = .saving
        try modelContext.save()
    }

    /// One retry (after 3 s) on a rate-limit response; other errors and a
    /// second rate-limit propagate to the batch failure counter.
    private func classifyWithRetry(_ batch: [FetchedMessage],
                                   classifier: EmailClassifier) async throws -> [Int: ClassificationResult] {
        do {
            return try await classifier.classify(batch: batch)
        } catch NIMError.rateLimited {
            try? await Task.sleep(for: .seconds(3))
            return try await classifier.classify(batch: batch)
        }
    }

    // MARK: - Upsert

    private func upsert(message: FetchedMessage,
                        result: ClassificationResult,
                        matcher: ApplicationMatcher) {
        let application: JobApplication
        var reviewConfidence: Double?

        if let match = matcher.match(message: message, result: result) {
            application = match.application
            if match.needsReview {
                application.needsReview = true
                reviewConfidence = match.confidence
            }
        } else {
            // No usable company from the LLM? Derive one from the sender
            // domain (careers@stripe.com → "Stripe") before giving up —
            // otherwise every such email used to spawn a nameless record.
            var company = result.company
            if JobApplication.normalizeCompany(company).isEmpty {
                company = ApplicationMatcher.companyFromSender(message.sender) ?? ""
            }
            guard !JobApplication.normalizeCompany(company).isEmpty || !result.role.isEmpty else {
                log.warning("Skipped (no company or role identifiable): \(message.subject.prefix(60))")
                return
            }

            // The user's own outreach starts the pipeline at .outreach when
            // the model gave no usable status.
            let status = result.applicationStatus
                ?? (message.isOutgoing ? .outreach : .applied)
            let app = JobApplication(company: company,
                                     roleTitle: result.role,
                                     status: status)
            app.appliedDate = message.date
            app.lastUpdated = message.date
            app.contactEmail = message.sender
            app.source = result.source
            app.location = result.location
            if !message.threadId.isEmpty { app.threadIds.append(message.threadId) }
            modelContext.insert(app)
            matcher.register(app)
            application = app
        }

        // Recruiting cycle: prefer the LLM's extraction, fall back to a
        // regex over the subject + role text.
        if application.cycle.isEmpty {
            let detected = result.cycle
                ?? CycleDetector.detect(in: "\(message.subject) \(result.role)")
            if let detected, !detected.isEmpty { application.cycle = detected }
        }

        if !message.threadId.isEmpty, !application.threadIds.contains(message.threadId) {
            application.threadIds.append(message.threadId)
            // Keep the session index current so the NEXT email in this
            // thread matches at 1.0 (this omission caused duplicates).
            matcher.associate(threadId: message.threadId, with: application)
        }

        let event = EmailEvent(gmailMessageId: message.id,
                               threadId: message.threadId,
                               receivedDate: message.date,
                               sender: message.sender,
                               subject: message.subject,
                               snippet: message.snippet,
                               direction: message.isOutgoing ? .outgoing : .incoming)
        event.detectedStatusRaw = result.applicationStatus?.rawValue
        event.confidence = result.confidence
        event.rawJSON = nil
        if let reviewConfidence { event.matchConfidence = reviewConfidence }
        event.application = application
        modelContext.insert(event)

        // Advance status on a newer, different signal — compared against the
        // newest EMAIL date, not lastUpdated (manual edits bump lastUpdated
        // to "now", which used to block every later email update).
        if let detected = result.applicationStatus,
           message.date >= (application.lastEmailDate ?? .distantPast),
           detected != application.status {
            let change = EmailEvent(kind: .statusChange,
                                    text: "\(application.status.displayName) → \(detected.displayName)",
                                    date: message.date)
            change.application = application
            modelContext.insert(change)
            application.status = detected
        }

        application.lastEmailDate = max(application.lastEmailDate ?? .distantPast, message.date)
        application.lastUpdated = max(application.lastUpdated, message.date)
        if let action = result.nextAction, !action.isEmpty { application.nextAction = action }
        if application.location == nil { application.location = result.location }
        if application.source == nil { application.source = result.source }
    }

    // MARK: - Persistence helpers

    private func syncState() throws -> SyncState {
        let existing = try modelContext.fetch(FetchDescriptor<SyncState>())
        if let state = existing.first { return state }
        let state = SyncState()
        modelContext.insert(state)
        return state
    }

    /// All stored Gmail message ids, fetched once per run for O(1) dedupe.
    private func existingMessageIds() throws -> Set<String> {
        var descriptor = FetchDescriptor<EmailEvent>()
        descriptor.propertiesToFetch = [\.gmailMessageId]
        let events = try modelContext.fetch(descriptor)
        return Set(events.lazy.map(\.gmailMessageId).filter { !$0.isEmpty })
    }
}
