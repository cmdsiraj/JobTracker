//
//  Lead.swift
//  JobTracker
//
//  A manually-added prospect: a job posting to apply to, or a person to
//  reach out to. Lives in its own "Leads" section until acted on.
//

import Foundation
import SwiftData

enum LeadType: String, Codable, CaseIterable, Identifiable {
    case jobPosting
    case person

    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .jobPosting: return "Job Posting"
        case .person:     return "Person / Contact"
        }
    }

    var systemImage: String {
        switch self {
        case .jobPosting: return "link"
        case .person:     return "person.crop.circle"
        }
    }
}

enum LeadStage: String, Codable, CaseIterable, Identifiable {
    case todo
    case contacted
    case applied
    case done

    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .todo:      return "To Do"
        case .contacted: return "Contacted"
        case .applied:   return "Applied"
        case .done:      return "Done"
        }
    }

    var color: String {
        switch self {
        case .todo:      return "orange"
        case .contacted: return "teal"
        case .applied:   return "blue"
        case .done:      return "green"
        }
    }
}

@Model
final class Lead {
    var id: UUID = UUID()
    var title: String = ""
    var company: String = ""
    var url: String = ""
    var personName: String = ""
    var typeRaw: String = LeadType.jobPosting.rawValue
    var stageRaw: String = LeadStage.todo.rawValue
    var notes: String = ""
    var createdDate: Date = Date()
    var lastUpdated: Date = Date()

    init(type: LeadType = .jobPosting) {
        self.id = UUID()
        self.typeRaw = type.rawValue
        self.createdDate = Date()
        self.lastUpdated = Date()
    }

    var type: LeadType {
        get { LeadType(rawValue: typeRaw) ?? .jobPosting }
        set { typeRaw = newValue.rawValue }
    }

    var stage: LeadStage {
        get { LeadStage(rawValue: stageRaw) ?? .todo }
        set { stageRaw = newValue.rawValue }
    }

    var linkURL: URL? {
        guard !url.isEmpty else { return nil }
        return URL(string: url.hasPrefix("http") ? url : "https://\(url)")
    }
}
