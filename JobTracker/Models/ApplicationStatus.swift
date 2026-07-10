//
//  ApplicationStatus.swift
//  JobTracker
//
//  The full pipeline of a job application, including proactive outreach.
//  Stored as a raw String on the model for SwiftData/CloudKit friendliness.
//

import SwiftUI

enum ApplicationStatus: String, CaseIterable, Codable, Identifiable {
    case outreach         // user reached out (cold email, referral ask)
    case applied
    case assessment       // online assessment / take-home
    case recruiterCall    // recruiter screen / intro call
    case interview        // technical interview rounds
    case finalRound       // onsite / final loop
    case offer
    case rejected

    var id: String { rawValue }

    /// Columns shown on the Kanban board, in order.
    static var boardColumns: [ApplicationStatus] { allCases }

    /// Stages used in the analytics funnel, in pipeline order.
    static var funnelStages: [ApplicationStatus] {
        [.applied, .assessment, .recruiterCall, .interview, .finalRound, .offer]
    }

    var displayName: String {
        switch self {
        case .outreach:      return "Outreach"
        case .applied:       return "Applied"
        case .assessment:    return "Assessment"
        case .recruiterCall: return "Recruiter Call"
        case .interview:     return "Interview"
        case .finalRound:    return "Final Round"
        case .offer:         return "Offer"
        case .rejected:      return "Rejected"
        }
    }

    /// Higher means further along the pipeline. Used to decide whether a
    /// newer email should advance an application's status.
    var rank: Int {
        switch self {
        case .outreach:      return 0
        case .applied:       return 1
        case .assessment:    return 2
        case .recruiterCall: return 3
        case .interview:     return 4
        case .finalRound:    return 5
        case .offer:         return 6
        case .rejected:      return 7
        }
    }

    /// True while the application is still in play.
    var isActive: Bool {
        switch self {
        case .offer, .rejected: return false
        default:                return true
        }
    }

    var color: Color {
        switch self {
        case .outreach:      return .orange
        case .applied:       return .blue
        case .assessment:    return .cyan
        case .recruiterCall: return .teal
        case .interview:     return .purple
        case .finalRound:    return .indigo
        case .offer:         return .green
        case .rejected:      return .red
        }
    }

    var systemImage: String {
        switch self {
        case .outreach:      return "arrow.up.right.circle.fill"
        case .applied:       return "paperplane.fill"
        case .assessment:    return "laptopcomputer"
        case .recruiterCall: return "phone.fill"
        case .interview:     return "person.2.fill"
        case .finalRound:    return "flag.checkered"
        case .offer:         return "checkmark.seal.fill"
        case .rejected:      return "xmark.circle.fill"
        }
    }

    /// Maps the strings the LLM returns (and v1 statuses) onto the pipeline.
    static func from(llmString raw: String) -> ApplicationStatus? {
        switch raw.lowercased().trimmingCharacters(in: .whitespaces) {
        case "outreach", "cold email", "reached out", "follow-up", "followup":
            return .outreach
        case "applied", "application", "submitted":
            return .applied
        case "assessment", "oa", "online assessment",
             "take-home", "takehome", "coding challenge":
            return .assessment
        case "screening", "recruiter", "recruiter call",
             "phone screen", "recruitercall":
            return .recruiterCall
        case "interview", "technical", "technical interview":
            return .interview
        case "final", "final round", "finalround", "onsite":
            return .finalRound
        case "offer":
            return .offer
        case "rejected", "rejection", "declined":
            return .rejected
        default:
            return nil
        }
    }
}
