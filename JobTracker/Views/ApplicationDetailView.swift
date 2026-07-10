//
//  ApplicationDetailView.swift
//  JobTracker
//
//  Detail for a single application. Every edit persists instantly — there
//  is no Save button. Includes the full communication log (emails, notes,
//  status changes), note capture, merge/split tools, and review clearing.
//

import SwiftUI
import SwiftData

struct ApplicationDetailView: View {
    @Environment(\.modelContext) private var modelContext
    @Environment(\.dismiss) private var dismiss
    @Bindable var application: JobApplication

    @State private var newNote = ""
    @State private var showingMergeSheet = false

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
            Divider()
            ScrollView {
                VStack(alignment: .leading, spacing: 24) {
                    if application.needsReview {
                        reviewBanner
                    }
                    detailsSection
                    notesSection
                    timelineSection
                }
                .padding(20)
            }
        }
        .onChange(of: application.company) { _, newValue in
            application.companyKey = JobApplication.normalizeCompany(newValue)
            persist()
        }
        .onChange(of: application.roleTitle) { persist() }
        .onChange(of: application.location) { persist() }
        .onChange(of: application.source) { persist() }
        .onChange(of: application.cycle) { persist() }
        .onChange(of: application.nextAction) { persist() }
        .onChange(of: application.notes) { persist() }
        .onChange(of: application.tags) { persist() }
        .onChange(of: application.statusRaw) { oldValue, newValue in
            logStatusChange(from: oldValue, to: newValue)
        }
        .sheet(isPresented: $showingMergeSheet) {
            MergeTargetSheet(source: application) {
                dismiss()
            }
            .frame(minWidth: 420, minHeight: 380)
        }
    }

    // MARK: Header

    private var header: some View {
        HStack(alignment: .top, spacing: 12) {
            VStack(alignment: .leading, spacing: 4) {
                TextField("Company", text: $application.company)
                    .font(.title2.bold())
                    .textFieldStyle(.plain)
                TextField("Role", text: $application.roleTitle)
                    .font(.title3)
                    .foregroundStyle(.secondary)
                    .textFieldStyle(.plain)
            }

            Spacer()

            statusPicker

            Menu {
                Button("Merge Into…", systemImage: "arrow.triangle.merge") {
                    showingMergeSheet = true
                }
                Divider()
                Button("Delete Application", systemImage: "trash", role: .destructive) {
                    modelContext.delete(application)
                    try? modelContext.save()
                    dismiss()
                }
            } label: {
                Image(systemName: "ellipsis.circle")
            }
            .menuStyle(.borderlessButton)
            .fixedSize()

            Button {
                dismiss()
            } label: {
                Image(systemName: "xmark.circle.fill")
                    .foregroundStyle(.secondary)
            }
            .buttonStyle(.plain)
            .help("Close")
        }
        .padding(20)
    }

    private var statusPicker: some View {
        Picker("Status", selection: $application.statusRaw) {
            ForEach(ApplicationStatus.allCases) { status in
                Label(status.displayName, systemImage: status.systemImage)
                    .tag(status.rawValue)
            }
        }
        .labelsHidden()
        .tint(application.status.color)
        .fixedSize()
    }

    private var reviewBanner: some View {
        HStack(spacing: 10) {
            Image(systemName: "exclamationmark.triangle.fill")
                .foregroundStyle(.yellow)
            Text("Some emails were matched to this application with low confidence.")
                .font(.callout)
            Spacer()
            Button("Mark Reviewed") {
                withAnimation(.spring(duration: 0.35)) {
                    application.needsReview = false
                }
                persist()
            }
            .controlSize(.small)
        }
        .padding(12)
        .background(.yellow.opacity(0.12), in: RoundedRectangle(cornerRadius: 10))
    }

    // MARK: Fields

    private var detailsSection: some View {
        Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 8) {
            GridRow {
                Text("Location").foregroundStyle(.secondary)
                TextField("—", text: Binding($application.location, replacingNilWith: ""))
                    .textFieldStyle(.roundedBorder)
            }
            GridRow {
                Text("Source").foregroundStyle(.secondary)
                TextField("—", text: Binding($application.source, replacingNilWith: ""))
                    .textFieldStyle(.roundedBorder)
            }
            GridRow {
                Text("Cycle").foregroundStyle(.secondary)
                TextField("e.g. Summer 2026", text: $application.cycle)
                    .textFieldStyle(.roundedBorder)
            }
            GridRow {
                Text("Next Action").foregroundStyle(.secondary)
                TextField("—", text: $application.nextAction)
                    .textFieldStyle(.roundedBorder)
            }
            GridRow {
                Text("Tags").foregroundStyle(.secondary)
                TextField("comma, separated, tags", text: tagsBinding)
                    .textFieldStyle(.roundedBorder)
            }
            if let applied = application.appliedDate {
                GridRow {
                    Text("Applied").foregroundStyle(.secondary)
                    Text(applied, format: .dateTime.month().day().year())
                }
            }
            if let contact = application.contactEmail, !contact.isEmpty {
                GridRow {
                    Text("Contact").foregroundStyle(.secondary)
                    Text(contact).textSelection(.enabled)
                }
            }
        }
    }

    private var tagsBinding: Binding<String> {
        Binding(
            get: { application.tags.joined(separator: ", ") },
            set: { newValue in
                application.tags = newValue
                    .split(separator: ",")
                    .map { $0.trimmingCharacters(in: .whitespaces) }
                    .filter { !$0.isEmpty }
            }
        )
    }

    private var notesSection: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Notes").font(.headline)
            TextEditor(text: $application.notes)
                .font(.body)
                .frame(minHeight: 72)
                .scrollContentBackground(.hidden)
                .padding(6)
                .background(.quaternary.opacity(0.3), in: RoundedRectangle(cornerRadius: 8))
        }
    }

    // MARK: Timeline

    private var timelineSection: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("Communication Log").font(.headline)

            HStack(spacing: 8) {
                TextField("Add a note — e.g. \"Process paused until March\"",
                          text: $newNote)
                    .textFieldStyle(.roundedBorder)
                    .onSubmit(addNote)
                Button("Add Note", systemImage: "plus.circle.fill", action: addNote)
                    .disabled(newNote.trimmingCharacters(in: .whitespaces).isEmpty)
            }

            if application.sortedEvents.isEmpty {
                Text("No activity yet. Notes and matched emails will appear here.")
                    .font(.callout)
                    .foregroundStyle(.secondary)
            } else {
                ForEach(application.sortedEvents) { event in
                    TimelineRow(event: event)
                        .contextMenu {
                            Button("Detach into New Application",
                                   systemImage: "arrow.triangle.branch") {
                                detach(event)
                            }
                            Button("Delete Event", systemImage: "trash",
                                   role: .destructive) {
                                modelContext.delete(event)
                                try? modelContext.save()
                            }
                        }
                }
            }
        }
    }

    // MARK: Actions

    private func addNote() {
        let text = newNote.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { return }
        let note = EmailEvent(kind: .note, text: text)
        modelContext.insert(note)
        note.application = application
        withAnimation(.spring(duration: 0.35)) { newNote = "" }
        persist()
    }

    /// Splits one event out into a brand-new application.
    private func detach(_ event: EmailEvent) {
        let copy = JobApplication(company: application.company,
                                  roleTitle: application.roleTitle,
                                  status: event.detectedStatus ?? application.status)
        copy.cycle = application.cycle
        copy.needsReview = true
        modelContext.insert(copy)
        event.application = copy
        if !event.threadId.isEmpty {
            application.threadIds.removeAll { $0 == event.threadId }
            if !copy.threadIds.contains(event.threadId) {
                copy.threadIds.append(event.threadId)
            }
        }
        persist()
    }

    private func logStatusChange(from oldRaw: String, to newRaw: String) {
        guard oldRaw != newRaw,
              let old = ApplicationStatus(rawValue: oldRaw),
              let new = ApplicationStatus(rawValue: newRaw) else { return }
        let event = EmailEvent(kind: .statusChange,
                               text: "\(old.displayName) → \(new.displayName)")
        modelContext.insert(event)
        event.application = application
        persist()
    }

    private func persist() {
        application.lastUpdated = Date()
        try? modelContext.save()
    }
}

// MARK: - Timeline row

private struct TimelineRow: View {
    let event: EmailEvent

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            iconColumn
            content
        }
    }

    private var iconColumn: some View {
        VStack(spacing: 0) {
            Image(systemName: iconName)
                .font(.caption)
                .foregroundStyle(iconColor)
                .frame(width: 22, height: 22)
                .background(iconColor.opacity(0.14), in: Circle())
            Rectangle().fill(.quaternary).frame(width: 2)
        }
    }

    @ViewBuilder
    private var content: some View {
        switch event.kind {
        case .statusChange:
            VStack(alignment: .leading, spacing: 2) {
                HStack {
                    Text(event.snippet)
                        .font(.subheadline.weight(.medium))
                    Spacer()
                    dateText
                }
            }
            .padding(.bottom, 10)
        case .note:
            VStack(alignment: .leading, spacing: 3) {
                HStack {
                    Text("Note").font(.caption.bold()).foregroundStyle(.secondary)
                    Spacer()
                    dateText
                }
                Text(event.snippet).font(.callout)
            }
            .padding(.bottom, 10)
        case .email:
            VStack(alignment: .leading, spacing: 3) {
                HStack {
                    Text(senderLine)
                        .font(.caption.bold())
                        .foregroundStyle(event.direction == .outgoing ? .orange : .secondary)
                    if let detected = event.detectedStatus {
                        Text(detected.displayName)
                            .font(.caption2.bold())
                            .padding(.horizontal, 6).padding(.vertical, 1)
                            .background(detected.color.opacity(0.15), in: Capsule())
                            .foregroundStyle(detected.color)
                    }
                    if event.matchConfidence < 0.9 {
                        Label(event.matchConfidence.formatted(.percent.precision(.fractionLength(0))),
                              systemImage: "questionmark.circle")
                            .font(.caption2)
                            .foregroundStyle(.yellow)
                            .help("Matched to this application with \(event.matchConfidence.formatted(.percent.precision(.fractionLength(0)))) confidence")
                    }
                    Spacer()
                    dateText
                }
                Text(event.subject).font(.subheadline).lineLimit(2)
                Text(event.snippet)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(3)
                if let url = event.gmailURL {
                    Link("Open in Gmail", destination: url).font(.caption)
                }
            }
            .padding(.bottom, 10)
        }
    }

    private var senderLine: String {
        if event.direction == .outgoing {
            return "You → \(event.sender.isEmpty ? "recipient" : event.sender)"
        }
        return event.sender.isEmpty ? "Unknown sender" : event.sender
    }

    private var dateText: some View {
        Text(event.receivedDate, format: .dateTime.month().day().year().hour().minute())
            .font(.caption2)
            .foregroundStyle(.tertiary)
    }

    private var iconName: String {
        switch event.kind {
        case .statusChange: return "arrow.triangle.swap"
        case .note:         return "note.text"
        case .email:
            return event.direction == .outgoing ? "arrow.up.right" : "arrow.down.left"
        }
    }

    private var iconColor: Color {
        switch event.kind {
        case .statusChange: return event.detectedStatus?.color ?? .purple
        case .note:         return .indigo
        case .email:        return event.direction == .outgoing ? .orange : .blue
        }
    }
}

// MARK: - Merge sheet

private struct MergeTargetSheet: View {
    @Environment(\.modelContext) private var modelContext
    @Environment(\.dismiss) private var dismiss
    let source: JobApplication
    let onMerged: () -> Void

    @Query(sort: \JobApplication.lastUpdated, order: .reverse)
    private var applications: [JobApplication]

    @State private var searchText = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            VStack(alignment: .leading, spacing: 4) {
                Text("Merge Into…").font(.title3.bold())
                Text("Moves every email, note, and thread from “\(source.company)” into the application you pick, then deletes this one.")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }

            TextField("Search applications", text: $searchText)
                .textFieldStyle(.roundedBorder)

            if candidates.isEmpty {
                ContentUnavailableView("No Other Applications",
                                       systemImage: "tray",
                                       description: Text("There is nothing to merge into yet."))
            } else {
                List(candidates) { target in
                    Button {
                        merge(into: target)
                    } label: {
                        HStack {
                            Image(systemName: target.status.systemImage)
                                .foregroundStyle(target.status.color)
                            VStack(alignment: .leading, spacing: 1) {
                                Text(target.company).font(.headline)
                                Text(target.roleTitle.isEmpty ? target.status.displayName : target.roleTitle)
                                    .font(.caption)
                                    .foregroundStyle(.secondary)
                            }
                            Spacer()
                            Text("\((target.events ?? []).count) events")
                                .font(.caption2)
                                .foregroundStyle(.tertiary)
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                }
                .listStyle(.inset)
            }

            HStack {
                Spacer()
                Button("Cancel") { dismiss() }
                    .keyboardShortcut(.cancelAction)
            }
        }
        .padding(20)
    }

    private var candidates: [JobApplication] {
        applications.filter { candidate in
            candidate.id != source.id
                && (searchText.isEmpty
                    || candidate.company.localizedCaseInsensitiveContains(searchText)
                    || candidate.roleTitle.localizedCaseInsensitiveContains(searchText))
        }
    }

    private func merge(into target: JobApplication) {
        for event in source.events ?? [] {
            event.application = target
        }
        for threadId in source.threadIds where !target.threadIds.contains(threadId) {
            target.threadIds.append(threadId)
        }
        if !source.notes.isEmpty {
            target.notes = target.notes.isEmpty
                ? source.notes
                : target.notes + "\n\n" + source.notes
        }
        target.lastUpdated = Date()
        modelContext.delete(source)
        try? modelContext.save()
        dismiss()
        onMerged()
    }
}

// MARK: - Optional-String binding helper

private extension Binding where Value == String {
    init(_ source: Binding<String?>, replacingNilWith fallback: String) {
        self.init(
            get: { source.wrappedValue ?? fallback },
            set: { source.wrappedValue = $0.isEmpty ? nil : $0 }
        )
    }
}
