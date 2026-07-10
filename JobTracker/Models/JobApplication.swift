//
//  JobApplication.swift
//  JobTracker
//
//  A single job application, aggregated from one or more Gmail threads.
//  All properties have defaults and relationships are optional so the model
//  works with CloudKit sync (iCloud storage choice) as well as local-only.
//

import Foundation
import SwiftData

@Model
final class JobApplication {
    var id: UUID = UUID()
    var company: String = ""
    var roleTitle: String = ""

    /// Normalized company key ("Google LLC" → "google") maintained on every
    /// company edit; drives duplicate detection and matching.
    var companyKey: String = ""

    /// Backing storage for `status`. String for CloudKit compatibility.
    var statusRaw: String = ApplicationStatus.applied.rawValue

    var location: String?
    var source: String?

    /// Recruiting cycle, e.g. "Summer 2026", "Fall 2025", "New Grad 2026".
    var cycle: String = ""

    var appliedDate: Date?
    var lastUpdated: Date = Date()

    /// Date of the newest *email* on this application. Status-advance
    /// decisions compare against this — not `lastUpdated`, which manual
    /// edits bump to "now" (that used to block all later email updates).
    var lastEmailDate: Date?
    var notes: String = ""
    var nextAction: String = ""
    var tags: [String] = []
    var contactEmail: String?

    /// Gmail thread ids that belong to this application (strongest match key).
    var threadIds: [String] = []

    /// Set when the matcher attached an email with low confidence — surfaces
    /// the application in the "Needs Review" tray for manual merge/split.
    var needsReview: Bool = false

    @Relationship(deleteRule: .cascade, inverse: \EmailEvent.application)
    var events: [EmailEvent]? = []

    init(company: String, roleTitle: String, status: ApplicationStatus = .applied) {
        self.id = UUID()
        self.company = company
        self.roleTitle = roleTitle
        self.companyKey = Self.normalizeCompany(company)
        self.statusRaw = status.rawValue
        self.lastUpdated = Date()
    }

    var status: ApplicationStatus {
        get { ApplicationStatus(rawValue: statusRaw) ?? .applied }
        set { statusRaw = newValue.rawValue }
    }

    /// Events sorted newest-first, for the detail timeline.
    var sortedEvents: [EmailEvent] {
        (events ?? []).sorted { $0.receivedDate > $1.receivedDate }
    }

    // MARK: - Normalization

    /// Lowercases and strips legal/branding suffixes so "Google LLC",
    /// "Google, Inc." and "google" all collapse to the same key.
    static func normalizeCompany(_ name: String) -> String {
        var key = name.lowercased()
            .replacingOccurrences(of: "&", with: "and")
        for suffix in [", inc.", ", inc", " inc.", " inc", ", llc", " llc",
                       " corporation", " corp.", " corp", " company", " co.",
                       " technologies", " technology", " labs", " ltd.", " ltd",
                       " gmbh", " plc", " group"] {
            if key.hasSuffix(suffix) { key = String(key.dropLast(suffix.count)) }
        }
        return key
            .components(separatedBy: CharacterSet.alphanumerics.inverted)
            .filter { !$0.isEmpty }
            .joined(separator: " ")
    }
}
