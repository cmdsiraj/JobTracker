//
//  NewApplicationSheet.swift
//  JobTracker
//
//  Manual entry: create a brand-new application, or log an update (note +
//  optional status change) on an existing one — for anything that didn't
//  arrive by email: phone calls, LinkedIn messages, career-fair chats,
//  "process paused", and so on.
//

import SwiftUI
import SwiftData

struct NewApplicationSheet: View {
    @Environment(\.modelContext) private var modelContext
    @Environment(\.dismiss) private var dismiss
    @Query(sort: \JobApplication.lastUpdated, order: .reverse)
    private var applications: [JobApplication]

    private enum Mode: String, CaseIterable, Identifiable {
        case newApplication = "New Application"
        case update = "Update Existing"
        var id: String { rawValue }
    }

    @State private var mode: Mode = .newApplication

    // New application fields
    @State private var company = ""
    @State private var role = ""
    @State private var status: ApplicationStatus = .applied
    @State private var cycle = ""
    @State private var location = ""
    @State private var source = ""
    @State private var appliedDate = Date()
    @State private var notes = ""

    // Update fields
    @State private var targetApp: JobApplication?
    @State private var updateText = ""
    @State private var newStatus: ApplicationStatus?

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("Add Manually")
                .font(.title2.bold())

            Picker("Mode", selection: $mode) {
                ForEach(Mode.allCases) { mode in
                    Text(mode.rawValue).tag(mode)
                }
            }
            .pickerStyle(.segmented)
            .labelsHidden()

            switch mode {
            case .newApplication: newApplicationForm
            case .update:         updateForm
            }

            HStack {
                Button("Cancel") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Spacer()
                Button(mode == .newApplication ? "Add Application" : "Log Update") {
                    save()
                }
                .keyboardShortcut(.defaultAction)
                .buttonStyle(.borderedProminent)
                .disabled(!canSave)
            }
        }
        .padding(24)
        .frame(width: 460)
    }

    // MARK: - Forms

    private var newApplicationForm: some View {
        Form {
            TextField("Company", text: $company)
            TextField("Role", text: $role)
            Picker("Status", selection: $status) {
                ForEach(ApplicationStatus.allCases) { status in
                    Label(status.displayName, systemImage: status.systemImage)
                        .tag(status)
                }
            }
            TextField("Cycle (e.g. Summer 2026)", text: $cycle)
            TextField("Location", text: $location)
            TextField("Source (e.g. LinkedIn, referral)", text: $source)
            DatePicker("Date", selection: $appliedDate, displayedComponents: .date)
            TextField("Notes", text: $notes, axis: .vertical)
                .lineLimit(2...4)
        }
        .formStyle(.columns)
    }

    private var updateForm: some View {
        Form {
            Picker("Application", selection: $targetApp) {
                Text("Choose…").tag(JobApplication?.none)
                ForEach(applications) { app in
                    Text("\(app.company) — \(app.roleTitle)")
                        .tag(Optional(app))
                }
            }
            TextField("What happened? (e.g. recruiter said process is paused)",
                      text: $updateText, axis: .vertical)
                .lineLimit(2...5)
            Picker("Move status to", selection: $newStatus) {
                Text("Keep current status").tag(ApplicationStatus?.none)
                ForEach(ApplicationStatus.allCases) { status in
                    Label(status.displayName, systemImage: status.systemImage)
                        .tag(Optional(status))
                }
            }
        }
        .formStyle(.columns)
    }

    // MARK: - Saving

    private var canSave: Bool {
        switch mode {
        case .newApplication:
            return !company.trimmingCharacters(in: .whitespaces).isEmpty
        case .update:
            return targetApp != nil &&
                (!updateText.trimmingCharacters(in: .whitespaces).isEmpty || newStatus != nil)
        }
    }

    private func save() {
        switch mode {
        case .newApplication:
            let app = JobApplication(company: company.trimmingCharacters(in: .whitespaces),
                                     roleTitle: role.trimmingCharacters(in: .whitespaces),
                                     status: status)
            app.cycle = cycle.trimmingCharacters(in: .whitespaces)
            app.location = location.isEmpty ? nil : location
            app.source = source.isEmpty ? nil : source
            app.appliedDate = appliedDate
            app.lastUpdated = appliedDate
            app.notes = notes
            modelContext.insert(app)

            let created = EmailEvent(kind: .note, text: "Added manually", date: appliedDate)
            created.application = app
            modelContext.insert(created)

        case .update:
            guard let app = targetApp else { return }
            if !updateText.trimmingCharacters(in: .whitespaces).isEmpty {
                let note = EmailEvent(kind: .note, text: updateText)
                note.application = app
                modelContext.insert(note)
            }
            if let newStatus, newStatus != app.status {
                let change = EmailEvent(kind: .statusChange,
                                        text: "\(app.status.displayName) → \(newStatus.displayName)")
                change.application = app
                modelContext.insert(change)
                app.status = newStatus
            }
            app.lastUpdated = Date()
        }
        try? modelContext.save()
        dismiss()
    }
}
