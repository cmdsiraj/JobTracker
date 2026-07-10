//
//  ApplicationMatcher.swift
//  JobTracker
//
//  Groups classified emails into job applications using layered signals:
//
//    1. Gmail thread id                     → certain match  (1.0)
//    2. same company + similar role         → strong match   (0.9)
//    3. same company + partial role overlap → probable       (0.75, review)
//    4. same company + a missing role,
//       close in time                       → probable       (0.7, review)
//    5. known sender address                → weak            (0.6, review)
//
//  Role comparison is token-based (season/year tokens stripped) so
//  "Software Engineer Intern" matches "Software Engineering Intern —
//  Summer 2026". Company keys match exactly or by whole-token containment
//  ("amazon" ⊂ "amazon web services").
//
//  Indexes are built once per sync session and updated incrementally —
//  including thread ids attached to existing applications mid-run, so the
//  second email of a thread always matches at 1.0.
//

import Foundation

@MainActor
final class ApplicationMatcher {

    struct Match {
        let application: JobApplication
        let confidence: Double
        var needsReview: Bool { confidence < 0.9 }
    }

    /// How close in time a role-less email must be to an application with
    /// the same company to be considered the same process.
    private static let proximityWindow: TimeInterval = 120 * 24 * 3600

    private var byThread: [String: JobApplication] = [:]
    private var byCompany: [String: [JobApplication]] = [:]
    private var bySender: [String: JobApplication] = [:]

    init(existing: [JobApplication]) {
        for application in existing { register(application) }
    }

    /// Adds a new application to the session indexes.
    func register(_ application: JobApplication) {
        for threadId in application.threadIds where !threadId.isEmpty {
            byThread[threadId] = application
        }
        if !application.companyKey.isEmpty {
            byCompany[application.companyKey, default: []].append(application)
        }
        if let contact = application.contactEmail.flatMap(Self.emailAddress) {
            bySender[contact] = application
        }
    }

    /// Records that `threadId` now belongs to `application` — called
    /// whenever the pipeline appends a thread to an existing application so
    /// follow-ups in that thread match at 1.0 instead of re-running fuzzy
    /// matching (the old behavior caused duplicates).
    func associate(threadId: String, with application: JobApplication) {
        guard !threadId.isEmpty else { return }
        byThread[threadId] = application
    }

    // MARK: - Matching

    func match(message: FetchedMessage, result: ClassificationResult) -> Match? {
        // 1. Same Gmail thread — same application, always.
        if !message.threadId.isEmpty, let app = byThread[message.threadId] {
            return Match(application: app, confidence: 1.0)
        }

        let companyKey = JobApplication.normalizeCompany(result.company)
        let candidates = companyCandidates(for: companyKey)

        if !candidates.apps.isEmpty {
            let incomingTokens = Self.roleTokens(result.role)

            // 2./3. Role-similarity ranking within the company.
            var bestSimilar: (app: JobApplication, similarity: Double)?
            var windowCandidates: [JobApplication] = []
            for candidate in candidates.apps {
                let candidateTokens = Self.roleTokens(candidate.roleTitle)
                if incomingTokens.isEmpty || candidateTokens.isEmpty {
                    // 4. A side lacks a role: candidate only if close in time.
                    let anchor = candidate.lastEmailDate ?? candidate.lastUpdated
                    if abs(anchor.timeIntervalSince(message.date)) < Self.proximityWindow {
                        windowCandidates.append(candidate)
                    }
                    continue
                }
                let similarity = Self.similarity(incomingTokens, candidateTokens)
                if similarity > (bestSimilar?.similarity ?? 0) {
                    bestSimilar = (candidate, similarity)
                }
            }

            if let best = bestSimilar {
                if best.similarity >= 0.6 {
                    // Same role in different words. Exact-company gets full
                    // strength; containment-matched company keeps review.
                    return Match(application: best.app,
                                 confidence: candidates.exactCompany ? 0.9 : 0.75)
                }
                if best.similarity >= 0.35 {
                    return Match(application: best.app, confidence: 0.75)
                }
            }

            // Role-less matching must respect multiple parallel applications
            // at the same company (e.g. several roles applied the same day):
            // unambiguous single candidate attaches; multiple candidates
            // attach to the nearest-in-time but flagged for review so the
            // user can detach if the guess is wrong.
            if incomingTokens.isEmpty || candidates.apps.allSatisfy({ Self.roleTokens($0.roleTitle).isEmpty }) {
                if windowCandidates.count == 1 {
                    return Match(application: windowCandidates[0], confidence: 0.7)
                }
                if windowCandidates.count > 1 {
                    let nearest = windowCandidates.min { a, b in
                        let anchorA = a.lastEmailDate ?? a.lastUpdated
                        let anchorB = b.lastEmailDate ?? b.lastUpdated
                        return abs(anchorA.timeIntervalSince(message.date)) <
                               abs(anchorB.timeIntervalSince(message.date))
                    }
                    if let nearest {
                        return Match(application: nearest, confidence: 0.6)
                    }
                }
            }
            // Distinct roles at the same company → genuinely separate
            // applications; fall through to create a new one.
        }

        // 5. Known recruiter/sender address, when the model found no company.
        if companyKey.isEmpty,
           let sender = Self.emailAddress(message.sender),
           let app = bySender[sender] {
            return Match(application: app, confidence: 0.6)
        }

        return nil
    }

    /// Applications at the same company: exact key match, else whole-token
    /// containment ("amazon" ⊂ "amazon web services", either direction).
    private func companyCandidates(for key: String) -> (apps: [JobApplication], exactCompany: Bool) {
        guard !key.isEmpty else { return ([], false) }
        if let exact = byCompany[key] { return (exact, true) }

        let tokens = Set(key.split(separator: " ").map(String.init))
        var contained: [JobApplication] = []
        for (otherKey, apps) in byCompany {
            let otherTokens = Set(otherKey.split(separator: " ").map(String.init))
            if tokens.isSubset(of: otherTokens) || otherTokens.isSubset(of: tokens) {
                contained.append(contentsOf: apps)
            }
        }
        return (contained, false)
    }

    // MARK: - Role similarity

    /// Tokens that describe the cycle, not the role, and connective noise.
    /// NOTE: "intern"/"grad" are deliberately KEPT — an internship and a
    /// full-time role at the same company are different applications.
    private static let noiseTokens: Set<String> = [
        "summer", "fall", "autumn", "winter", "spring",
        "the", "a", "an", "of", "and", "for", "at", "in", "position", "role"
    ]

    /// Lowercased word tokens with cycle years/seasons and noise removed.
    nonisolated static func roleTokens(_ role: String) -> Set<String> {
        Set(
            role.lowercased()
                .components(separatedBy: CharacterSet.alphanumerics.inverted)
                .filter { !$0.isEmpty }
                .filter { !noiseTokens.contains($0) }
                .filter { !($0.count == 4 && $0.hasPrefix("20") && Int($0) != nil) }
                .map { token in
                    // Light stemming so "engineering" matches "engineer".
                    token.hasSuffix("ing") && token.count > 6
                        ? String(token.dropLast(3)) : token
                }
        )
    }

    /// Jaccard similarity of two token sets.
    nonisolated static func similarity(_ a: Set<String>, _ b: Set<String>) -> Double {
        guard !a.isEmpty, !b.isEmpty else { return 0 }
        let intersection = a.intersection(b).count
        let union = a.union(b).count
        return Double(intersection) / Double(union)
    }

    // MARK: - Sender helpers

    /// Extracts "user@host" from a From header like "Name <user@host>".
    nonisolated static func emailAddress(_ from: String) -> String? {
        if let open = from.lastIndex(of: "<"), let close = from.lastIndex(of: ">"),
           open < close {
            return String(from[from.index(after: open)..<close]).lowercased()
        }
        let trimmed = from.trimmingCharacters(in: .whitespaces).lowercased()
        return trimmed.contains("@") ? trimmed : nil
    }

    /// Mail providers and ATS platforms whose domain never names the
    /// hiring company.
    private static let genericDomains: Set<String> = [
        "gmail", "googlemail", "outlook", "hotmail", "yahoo", "icloud", "aol",
        "greenhouse", "lever", "myworkday", "workday", "icims",
        "smartrecruiters", "ashbyhq", "jobvite", "taleo", "successfactors",
        "bamboohr", "workable", "linkedin", "indeed", "ziprecruiter",
        "hackerrank", "codesignal", "hirevue", "karat", "codility",
        "workablemail", "candidates", "notifications", "mail", "email", "no-reply"
    ]

    /// Best-effort company name from a sender address, e.g.
    /// "careers@stripe.com" → "Stripe". Nil for generic/ATS domains.
    nonisolated static func companyFromSender(_ from: String) -> String? {
        guard let address = emailAddress(from),
              let atIndex = address.firstIndex(of: "@") else { return nil }
        let host = String(address[address.index(after: atIndex)...])
        let labels = host.split(separator: ".").map(String.init)
        // Take the label left of the TLD ("careers.tiktok.com" → "tiktok").
        guard labels.count >= 2 else { return nil }
        let label = labels[labels.count - 2]
        guard label.count > 1, !genericDomains.contains(label),
              !labels.contains(where: { genericDomains.contains($0) }) else { return nil }
        return label.prefix(1).uppercased() + label.dropFirst()
    }
}
