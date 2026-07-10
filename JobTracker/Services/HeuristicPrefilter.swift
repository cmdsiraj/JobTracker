//
//  HeuristicPrefilter.swift
//  JobTracker
//
//  A cheap keyword/sender check that runs BEFORE the LLM, so obvious
//  non-job-related mail never costs an API call (protecting the free-tier
//  rate limit and reducing how much private mail is sent out).
//
//  Tuned against a real 14k-message Takeout archive: blanket domain matches
//  for LinkedIn/Indeed admit thousands of job-alert newsletters, so matching
//  is limited to ATS senders, career mailboxes, and phrases that indicate the
//  recipient's own application.
//

import Foundation

/// `nonisolated`: called from the detached mbox-parse task as well as the
/// main-actor classifier.
nonisolated enum HeuristicPrefilter {

    /// Senders that almost always mean application activity: ATS platforms
    /// and company career mailboxes.
    private static let atsSenderPatterns = [
        "greenhouse.io", "lever.co", "myworkday", "workday", "icims.com",
        "smartrecruiters.com", "ashbyhq.com", "jobvite.com", "taleo.net",
        "successfactors.com", "bamboohr.com", "workable", "hired.com",
        "amazon.jobs", "gem.com", "rippling", "wellfound.com", "otta.com",
        "careers@", "jobs@", "recruiting@", "talent@", "recruitment@",
        "noreply@careers", "no-reply@careers", "@careers.", "@jobs.",
        "hackerrank", "codesignal", "hirevue", "karat.com", "codility"
    ]

    /// Phrases in sender+subject that strongly indicate the recipient's own
    /// application/hiring process (not job-alert newsletters).
    private static let strongHeaderPhrases = [
        "your application", "thank you for applying", "thanks for applying",
        "we received your", "we've received your", "application received",
        "application to", "application for", "application status",
        "application update", "you applied", "your candidacy", "candidate",
        "interview", "phone screen", "assessment", "coding challenge",
        "online assessment", "next steps", "offer", "we regret",
        "unfortunately", "not moving forward", "moving forward",
        "requisition", "started your job application"
    ]

    /// Strong body phrases; two or more are required when the header alone
    /// isn't conclusive.
    private static let strongBodyPhrases = [
        "thank you for applying", "thanks for applying",
        "we received your application", "your application",
        "interview process", "schedule your interview", "phone screen",
        "online assessment", "coding challenge", "your candidacy",
        "we regret to inform", "not be moving forward", "not moving forward",
        "offer letter", "position you applied", "recruiting team",
        "talent acquisition", "hiring team", "hiring manager"
    ]

    /// Keywords that mark an outgoing (user-sent) email as plausibly
    /// job-related. Outgoing mail gets a deliberately lower bar: the user's
    /// own outreach rarely matches ATS/confirmation phrasing.
    private static let outgoingKeywords = [
        "position", "role", "opportunity", "application", "applying",
        "resume", "cv", "internship", "recruiter", "referral",
        "interested in", "opening"
    ]

    /// Direction-aware entry point. Outgoing messages pass if the subject or
    /// the first part of the body mentions any job-search keyword; incoming
    /// messages use the stricter ATS/phrase logic below.
    static func isLikelyJobRelated(sender: String, subject: String, body: String, isOutgoing: Bool) -> Bool {
        guard isOutgoing else {
            return isLikelyJobRelated(sender: sender, subject: subject, body: body)
        }
        let text = "\(subject) \(String(body.prefix(1500)))".lowercased()
        return outgoingKeywords.contains(where: { text.contains($0) })
    }

    static func isLikelyJobRelated(sender: String, subject: String, body: String) -> Bool {
        let senderLower = sender.lowercased()
        let headerLower = "\(sender) \(subject)".lowercased()

        // Job-alert / job-digest newsletters are never the user's own
        // applications, no matter the sender.
        let alertMarkers = ["job alert", "jobs for you", "new jobs", "recommended jobs",
                            "job recommendations", "jobs you may", "daily digest",
                            "weekly digest", "trending jobs", "hiring now", "job matches"]
        if alertMarkers.contains(where: { headerLower.contains($0) }) { return false }

        if atsSenderPatterns.contains(where: { senderLower.contains($0) }) { return true }

        if strongHeaderPhrases.contains(where: { headerLower.contains($0) }) { return true }

        let bodyHead = String(body.prefix(1500)).lowercased()
        let hits = strongBodyPhrases.filter { bodyHead.contains($0) }.count
        return hits >= 2
    }
}
