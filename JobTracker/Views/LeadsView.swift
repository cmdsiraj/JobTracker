//
//  LeadsView.swift
//  JobTracker
//
//  Manually-tracked prospects: job postings to apply to and people to reach
//  out to. Leads advance through a lightweight stage flow and can be
//  converted into full applications.
//

import SwiftUI
import SwiftData

struct LeadsView: View {
    @Environment(\.modelContext) private var modelContext
    @Query(sort: \Lead.lastUpdated, order: .reverse) private var leads: [Lead]

    @State private var stageFilter: LeadStage?
    @State private var showingAddSheet = false

    var body: some View {
        Group {
            if leads.isEmpty {
                ContentUnavailableView {
                    Label("No Leads Yet", systemImage: "sparkles")
                } description: {
                    Text("Save job postings you want to apply to and people you want to reach out to.")
                } actions: {
                    Button("Add Lead", systemImage: "plus") {
                        showingAddSheet = true
                    }
                    .buttonStyle(.borderedProminent)
                }
            } else {
                list
            }
        }
        .toolbar {
            ToolbarItem {
                Picker("Stage", selection: $stageFilter) {
                    Text("All").tag(LeadStage?.none)
                    ForEach(LeadStage.allCases) { stage in
                        Text(stage.displayName).tag(LeadStage?.some(stage))
                    }
                }
                .pickerStyle(.segmented)
            }
            ToolbarItem {
                Button("Add Lead", systemImage: "plus") {
                    showingAddSheet = true
                }
                .help("Add a job posting or a person to reach out to")
            }
        }
        .sheet(isPresented: $showingAddSheet) {
            AddLeadSheet()
                .frame(width: 420)
        }
    }

    private var list: some View {
        List {
            ForEach(filteredLeads) { lead in
                LeadRow(lead: lead)
                    .contextMenu {
                        Button("Convert to Application",
                               systemImage: "briefcase") {
                            convert(lead)
                        }
                        Divider()
                        Button("Delete", systemImage: "trash", role: .destructive) {
                            delete(lead)
                        }
                    }
                    .swipeActions(edge: .trailing) {
                        Button("Delete", systemImage: "trash", role: .destructive) {
                            delete(lead)
                        }
                    }
            }
        }
        .listStyle(.inset)
    }

    private var filteredLeads: [Lead] {
        guard let stageFilter else { return leads }
        return leads.filter { $0.stage == stageFilter }
    }

    private func convert(_ lead: Lead) {
        let app = JobApplication(
            company: lead.company,
            roleTitle: lead.type == .person && lead.title.isEmpty
                ? "Outreach — \(lead.personName)"
                : lead.title,
            status: lead.type == .person ? .outreach : .applied
        )
        if !lead.url.isEmpty { app.source = lead.url }
        if !lead.notes.isEmpty { app.notes = lead.notes }
        modelContext.insert(app)
        modelContext.delete(lead)
        try? modelContext.save()
    }

    private func delete(_ lead: Lead) {
        withAnimation(.spring(duration: 0.3)) {
            modelContext.delete(lead)
        }
        try? modelContext.save()
    }
}

// MARK: - Row

private struct LeadRow: View {
    @Environment(\.modelContext) private var modelContext
    @Bindable var lead: Lead

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: lead.type.systemImage)
                .font(.title3)
                .foregroundStyle(.tint)
                .frame(width: 26)

            VStack(alignment: .leading, spacing: 2) {
                Text(primaryTitle).font(.headline).lineLimit(1)
                if !secondaryTitle.isEmpty {
                    Text(secondaryTitle)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }

            Spacer()

            if let url = lead.linkURL {
                Link(destination: url) {
                    Image(systemName: "arrow.up.forward.square")
                }
                .help("Open link")
            }

            stageMenu
        }
        .padding(.vertical, 4)
    }

    private var primaryTitle: String {
        switch lead.type {
        case .jobPosting:
            return lead.title.isEmpty ? (lead.company.isEmpty ? "Untitled Posting" : lead.company) : lead.title
        case .person:
            return lead.personName.isEmpty ? "Unnamed Contact" : lead.personName
        }
    }

    private var secondaryTitle: String {
        switch lead.type {
        case .jobPosting: return lead.company
        case .person:     return [lead.title, lead.company].filter { !$0.isEmpty }.joined(separator: " · ")
        }
    }

    private var stageMenu: some View {
        Menu {
            ForEach(LeadStage.allCases) { stage in
                Button {
                    withAnimation(.spring(duration: 0.3)) {
                        lead.stage = stage
                        lead.lastUpdated = Date()
                    }
                    try? modelContext.save()
                } label: {
                    if lead.stage == stage {
                        Label(stage.displayName, systemImage: "checkmark")
                    } else {
                        Text(stage.displayName)
                    }
                }
            }
        } label: {
            Text(lead.stage.displayName)
                .font(.caption.weight(.medium))
                .padding(.horizontal, 8).padding(.vertical, 3)
                .background(stageColor.opacity(0.15), in: Capsule())
                .foregroundStyle(stageColor)
        }
        .menuStyle(.borderlessButton)
        .fixedSize()
    }

    private var stageColor: Color {
        switch lead.stage {
        case .todo:      return .orange
        case .contacted: return .teal
        case .applied:   return .blue
        case .done:      return .green
        }
    }
}

// MARK: - Add sheet

private struct AddLeadSheet: View {
    @Environment(\.modelContext) private var modelContext
    @Environment(\.dismiss) private var dismiss

    @State private var type: LeadType = .jobPosting
    @State private var title = ""
    @State private var company = ""
    @State private var url = ""
    @State private var personName = ""
    @State private var notes = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("New Lead").font(.title3.bold())

            Picker("Type", selection: $type) {
                ForEach(LeadType.allCases) { type in
                    Label(type.displayName, systemImage: type.systemImage)
                        .tag(type)
                }
            }
            .pickerStyle(.segmented)
            .labelsHidden()

            Form {
                if type == .person {
                    TextField("Name", text: $personName)
                }
                TextField(type == .person ? "Role / Context" : "Role Title", text: $title)
                TextField("Company", text: $company)
                TextField("Link (posting or profile)", text: $url)
                TextField("Notes", text: $notes, axis: .vertical)
                    .lineLimit(2...4)
            }
            .formStyle(.columns)
            .textFieldStyle(.roundedBorder)

            HStack {
                Button("Cancel") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Spacer()
                Button("Add Lead") { add() }
                    .keyboardShortcut(.defaultAction)
                    .buttonStyle(.borderedProminent)
                    .disabled(!isValid)
            }
        }
        .padding(20)
    }

    private var isValid: Bool {
        switch type {
        case .jobPosting: return !title.isEmpty || !company.isEmpty || !url.isEmpty
        case .person:     return !personName.isEmpty
        }
    }

    private func add() {
        let lead = Lead(type: type)
        lead.title = title.trimmingCharacters(in: .whitespaces)
        lead.company = company.trimmingCharacters(in: .whitespaces)
        lead.url = url.trimmingCharacters(in: .whitespaces)
        lead.personName = personName.trimmingCharacters(in: .whitespaces)
        lead.notes = notes.trimmingCharacters(in: .whitespacesAndNewlines)
        modelContext.insert(lead)
        try? modelContext.save()
        dismiss()
    }
}
