//
//  EmailEvent.swift
//  JobTracker
//
//  One entry in an application's communication log. Three kinds:
//    - email:        a Gmail message (incoming or outgoing/outreach)
//    - note:         a manual note the user added ("process paused", …)
//    - statusChange: auto-logged whenever the status moves
//  Together these make the timeline a complete record of everything that
//  happened on an application.
//

import Foundation
import SwiftData

enum EventKind: String, Codable {
    case email
    case note
    case statusChange
}

enum EventDirection: String, Codable {
    case incoming   // company/recruiter → user
    case outgoing   // user → company (outreach, replies, follow-ups)
}

@Model
final class EmailEvent {
    var id: UUID = UUID()

    /// email | note | statusChange (String for CloudKit compatibility).
    var kindRaw: String = EventKind.email.rawValue

    /// incoming | outgoing; meaningful for kind == .email.
    var directionRaw: String = EventDirection.incoming.rawValue

    var gmailMessageId: String = ""
    var threadId: String = ""
    var receivedDate: Date = Date()
    var sender: String = ""
    var subject: String = ""
    var snippet: String = ""

    /// Status the classifier detected for this specific message.
    var detectedStatusRaw: String?
    var confidence: Double = 0

    /// How confident the matcher was when attaching this email to its
    /// application (1.0 = same thread, lower = fuzzy match).
    var matchConfidence: Double = 1.0

    /// Raw JSON returned by the LLM, kept for debugging/auditing.
    var rawJSON: String?

    var application: JobApplication?

    init(gmailMessageId: String,
         threadId: String,
         receivedDate: Date,
         sender: String,
         subject: String,
         snippet: String,
         direction: EventDirection = .incoming) {
        self.id = UUID()
        self.kindRaw = EventKind.email.rawValue
        self.directionRaw = direction.rawValue
        self.gmailMessageId = gmailMessageId
        self.threadId = threadId
        self.receivedDate = receivedDate
        self.sender = sender
        self.subject = subject
        self.snippet = snippet
    }

    /// Manual note or auto-logged status change.
    init(kind: EventKind, text: String, date: Date = Date()) {
        self.id = UUID()
        self.kindRaw = kind.rawValue
        self.directionRaw = EventDirection.incoming.rawValue
        self.receivedDate = date
        self.snippet = text
        self.subject = kind == .statusChange ? "Status changed" : "Note"
    }

    var kind: EventKind { EventKind(rawValue: kindRaw) ?? .email }
    var direction: EventDirection { EventDirection(rawValue: directionRaw) ?? .incoming }

    var detectedStatus: ApplicationStatus? {
        ApplicationStatus(rawValue: detectedStatusRaw ?? "")
    }

    /// Deep link that opens this message in the Gmail web client. Messages
    /// synced via the API use the hex message id; messages imported from a
    /// Takeout archive carry an RFC 822 Message-ID and use Gmail search.
    var gmailURL: URL? {
        guard kind == .email, !gmailMessageId.isEmpty else { return nil }
        if gmailMessageId.contains("@") {
            let encoded = gmailMessageId.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? gmailMessageId
            return URL(string: "https://mail.google.com/mail/u/0/#search/rfc822msgid:\(encoded)")
        }
        return URL(string: "https://mail.google.com/mail/u/0/#all/\(gmailMessageId)")
    }
}
