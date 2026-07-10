//
//  ContentView.swift
//  JobTracker
//
//  RootView routes between onboarding and the main shell. MainShell is the
//  primary NavigationSplitView: sidebar sections (Dashboard, Pipeline,
//  Outreach, Leads, Needs Review), detailed filtering, mbox import, and the
//  live sync overlay.
//

import SwiftUI
import SwiftData
import UniformTypeIdentifiers

// MARK: - Root

struct RootView: View {
    @Environment(AppState.self) private var appState

    var body: some View {
        if appState.needsOnboarding {
            OnboardingFlow()
                .frame(minWidth: 640, minHeight: 600)
        } else {
            MainShell()
                .frame(minWidth: 960, minHeight: 620)
                .task { appState.pipeline.syncOnLaunchIfNeeded() }
        }
    }
}

// MARK: - Filters

/// Every dimension the user can slice applications by. Filters combine
/// (AND across dimensions, OR within one).
@Observable
final class FilterState {
    var searchText = ""
    var statuses: Set<ApplicationStatus> = []
    var companies: Set<String> = []
    var cycles: Set<String> = []
    var sources: Set<String> = []

    /// Pipeline timeline window, driven by the interactive scrubber.
    /// Defaults to the last 5 months; nil means "not yet initialized".
    var timeline: ClosedRange<Date>?

    var hasActiveFilters: Bool {
        !statuses.isEmpty || !companies.isEmpty || !cycles.isEmpty || !sources.isEmpty
    }

    var activeFilterCount: Int {
        statuses.count + companies.count + cycles.count + sources.count
    }

    func clear() {
        statuses = []
        companies = []
        cycles = []
        sources = []
    }

    func matches(_ app: JobApplication) -> Bool {
        if !statuses.isEmpty, !statuses.contains(app.status) { return false }
        if !companies.isEmpty, !companies.contains(app.company) { return false }
        if !cycles.isEmpty, !cycles.contains(app.cycle) { return false }
        if !sources.isEmpty, !sources.contains(app.source ?? "") { return false }
        if let timeline, !timeline.contains(app.appliedDate ?? app.lastUpdated) { return false }
        if !searchText.isEmpty {
            let matchesSearch = app.company.localizedCaseInsensitiveContains(searchText)
                || app.roleTitle.localizedCaseInsensitiveContains(searchText)
                || app.notes.localizedCaseInsensitiveContains(searchText)
                || app.tags.contains { $0.localizedCaseInsensitiveContains(searchText) }
            if !matchesSearch { return false }
        }
        return true
    }
}

// MARK: - Sidebar sections

enum SidebarItem: Hashable {
    case dashboard
    case allApplications
    case status(ApplicationStatus)
    case leads
    case review

    var title: String {
        switch self {
        case .dashboard:        return "Dashboard"
        case .allApplications:  return "All Applications"
        case .status(let s):    return s.displayName
        case .leads:            return "Leads"
        case .review:           return "Needs Review"
        }
    }
}

// MARK: - Main shell

struct MainShell: View {
    @Environment(AppState.self) private var appState
    @Environment(\.openWindow) private var openWindow
    @Query(sort: \JobApplication.lastUpdated, order: .reverse)
    private var applications: [JobApplication]

    @State private var selection: SidebarItem? = .dashboard
    @State private var filters = FilterState()
    @State private var selectedApp: JobApplication?
    @State private var showingImporter = false
    @State private var showingNewApplication = false
    @State private var overlayVisible = false

    var body: some View {
        // Compute filter results and per-status counts ONCE per render —
        // the previous version recomputed the full filter pass for every
        // sidebar badge (≈10× per render), which lagged on large datasets.
        let filtered = applications.filter { filters.matches($0) }
        let statusTally = Dictionary(grouping: filtered, by: \.status).mapValues(\.count)
        let reviewCount = applications.lazy.filter(\.needsReview).count

        NavigationSplitView {
            sidebar(filteredCount: filtered.count,
                    statusTally: statusTally,
                    reviewCount: reviewCount)
        } detail: {
            detailContent(filtered: filtered)
                .navigationTitle(selection?.title ?? "JobTracker")
                .toolbar { toolbarContent }
        }
        .searchable(text: Bindable(filters).searchText,
                    placement: .toolbar,
                    prompt: "Search company, role, tags…")
        .sheet(item: $selectedApp) { app in
            ApplicationDetailView(application: app)
                .frame(minWidth: 560, minHeight: 580)
        }
        .sheet(isPresented: $showingNewApplication) {
            NewApplicationSheet()
        }
        .fileImporter(isPresented: $showingImporter,
                      allowedContentTypes: importTypes) { result in
            if case .success(let url) = result {
                appState.pipeline.importMbox(url: url)
            }
        }
        .sheet(isPresented: Binding(
            get: { appState.pipeline.pendingImport != nil },
            set: { if !$0 { appState.pipeline.discardPendingImport() } }
        )) {
            if let pending = appState.pipeline.pendingImport {
                ImportScopeSheet(pending: pending) { scope in
                    appState.pipeline.confirmImport(scope: scope)
                }
            }
        }
        .overlay(alignment: .bottomTrailing) {
            if overlayVisible {
                SyncOverlayView()
                    .padding(20)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
            }
        }
        .onChange(of: appState.pipeline.stage) { _, newStage in
            updateOverlay(for: newStage)
        }
        .onAppear { updateOverlay(for: appState.pipeline.stage) }
    }

    // MARK: Overlay visibility

    private func updateOverlay(for stage: SyncPipeline.Stage) {
        switch stage {
        case .idle:
            withAnimation(.spring(duration: 0.4)) { overlayVisible = false }
        case .finished:
            withAnimation(.spring(duration: 0.4)) { overlayVisible = true }
            Task {
                try? await Task.sleep(for: .seconds(2.2))
                if case .finished = appState.pipeline.stage {
                    withAnimation(.spring(duration: 0.4)) { overlayVisible = false }
                }
            }
        case .failed:
            withAnimation(.spring(duration: 0.4)) { overlayVisible = true }
            Task {
                try? await Task.sleep(for: .seconds(6))
                if case .failed = appState.pipeline.stage {
                    withAnimation(.spring(duration: 0.4)) { overlayVisible = false }
                }
            }
        default:
            withAnimation(.spring(duration: 0.4)) { overlayVisible = true }
        }
    }

    // MARK: Detail routing

    @ViewBuilder
    private func detailContent(filtered: [JobApplication]) -> some View {
        switch selection ?? .dashboard {
        case .dashboard:
            DashboardView()
        case .allApplications:
            boardWithTimeline(filtered: filtered,
                              columns: ApplicationStatus.boardColumns)
        case .status(let status):
            boardWithTimeline(filtered: filtered.filter { $0.status == status },
                              columns: [status])
        case .leads:
            LeadsView()
        case .review:
            ReviewView()
        }
    }

    /// Pipeline boards get the interactive timeline scrubber on top. The
    /// window defaults to the last 5 months; drag the handles (or the window
    /// itself) to reach older applications.
    private func boardWithTimeline(filtered: [JobApplication],
                                   columns: [ApplicationStatus]) -> some View {
        let domain = timelineDomain
        return VStack(spacing: 0) {
            TimelineScrubberView(
                domain: domain,
                histogram: monthHistogram(domain: domain),
                selection: Binding(
                    get: { filters.timeline ?? TimelineScrubberView.defaultWindow(clampedTo: domain) },
                    set: { filters.timeline = $0 }
                )
            )
            .padding([.horizontal, .top], 12)

            KanbanBoardView(applications: filtered,
                            columns: columns,
                            selection: $selectedApp)
        }
        .onAppear {
            if filters.timeline == nil {
                filters.timeline = TimelineScrubberView.defaultWindow(clampedTo: domain)
            }
        }
    }

    /// Month-floored extent of all application dates through now.
    private var timelineDomain: ClosedRange<Date> {
        TimelineScrubberView.domain(for: applications.map { $0.appliedDate ?? $0.lastUpdated })
    }

    private func monthHistogram(domain: ClosedRange<Date>) -> [(month: Date, count: Int)] {
        TimelineScrubberView.monthHistogram(
            dates: applications.map { $0.appliedDate ?? $0.lastUpdated },
            domain: domain)
    }

    // MARK: Sidebar

    private func sidebar(filteredCount: Int,
                         statusTally: [ApplicationStatus: Int],
                         reviewCount: Int) -> some View {
        List(selection: $selection) {
            Section("Overview") {
                Label("Dashboard", systemImage: "chart.bar.xaxis")
                    .tag(SidebarItem.dashboard)
            }

            Section("Pipeline") {
                Label("All Applications", systemImage: "tray.full")
                    .badge(filteredCount)
                    .tag(SidebarItem.allApplications)
                ForEach(ApplicationStatus.boardColumns.filter { $0 != .outreach }) { status in
                    Label(status.displayName, systemImage: status.systemImage)
                        .badge(statusTally[status] ?? 0)
                        .tag(SidebarItem.status(status))
                }
            }

            Section("Outreach") {
                Label(ApplicationStatus.outreach.displayName,
                      systemImage: ApplicationStatus.outreach.systemImage)
                    .badge(statusTally[.outreach] ?? 0)
                    .tag(SidebarItem.status(.outreach))
            }

            Section("Leads") {
                Label("Leads", systemImage: "sparkles")
                    .tag(SidebarItem.leads)
            }

            Section("Triage") {
                Label("Needs Review", systemImage: "exclamationmark.triangle")
                    .badge(reviewCount)
                    .tag(SidebarItem.review)
            }
        }
        .navigationSplitViewColumnWidth(min: 210, ideal: 240)
        .listStyle(.sidebar)
    }

    // MARK: Toolbar

    @ToolbarContentBuilder
    private var toolbarContent: some ToolbarContent {
        ToolbarItem {
            Button {
                showingNewApplication = true
            } label: {
                Label("Add Manually", systemImage: "plus")
            }
            .help("Manually add an application or update")
        }

        ToolbarItem {
            filterMenu
        }

        ToolbarItem {
            Button {
                openWindow(id: "activity-log")
            } label: {
                Label("Activity Log", systemImage: "text.alignleft")
            }
            .help("Show live sync logs (⇧⌘L)")
        }

        ToolbarItem {
            Button {
                showingImporter = true
            } label: {
                Label("Import Mail Archive…", systemImage: "square.and.arrow.down")
            }
            .disabled(appState.pipeline.isRunning)
            .help("Import a Google Takeout .mbox archive")
        }

        ToolbarItem {
            if appState.pipeline.isRunning {
                HStack(spacing: 6) {
                    ProgressView().controlSize(.small)
                    Text(appState.pipeline.stage.shortDescription)
                        .font(.caption).foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            } else {
                Button {
                    Task { await appState.pipeline.syncNow() }
                } label: {
                    Label("Sync Now", systemImage: "arrow.clockwise")
                }
                .help("Fetch and classify new email now")
            }
        }
    }

    private var filterMenu: some View {
        Menu {
            Section("Status") {
                ForEach(ApplicationStatus.allCases) { status in
                    filterToggle(status.displayName,
                                 isOn: filters.statuses.contains(status)) {
                        toggle(status, in: &filters.statuses)
                    }
                }
            }
            if !availableCompanies.isEmpty {
                Menu("Company") {
                    ForEach(availableCompanies, id: \.self) { company in
                        filterToggle(company,
                                     isOn: filters.companies.contains(company)) {
                            toggle(company, in: &filters.companies)
                        }
                    }
                }
            }
            if !availableCycles.isEmpty {
                Menu("Cycle") {
                    ForEach(availableCycles, id: \.self) { cycle in
                        filterToggle(cycle, isOn: filters.cycles.contains(cycle)) {
                            toggle(cycle, in: &filters.cycles)
                        }
                    }
                }
            }
            if !availableSources.isEmpty {
                Menu("Source") {
                    ForEach(availableSources, id: \.self) { source in
                        filterToggle(source, isOn: filters.sources.contains(source)) {
                            toggle(source, in: &filters.sources)
                        }
                    }
                }
            }
            if filters.hasActiveFilters {
                Divider()
                Button("Clear Filters", systemImage: "xmark.circle") {
                    withAnimation { filters.clear() }
                }
            }
        } label: {
            Label(
                filters.hasActiveFilters
                    ? "Filters (\(filters.activeFilterCount))"
                    : "Filters",
                systemImage: filters.hasActiveFilters
                    ? "line.3.horizontal.decrease.circle.fill"
                    : "line.3.horizontal.decrease.circle"
            )
        }
        .help("Filter by status, company, cycle, source, or date")
    }

    private func filterToggle(_ title: String, isOn: Bool,
                              action: @escaping () -> Void) -> some View {
        Button(action: action) {
            if isOn {
                Label(title, systemImage: "checkmark")
            } else {
                Text(title)
            }
        }
    }

    private func toggle<T: Hashable>(_ value: T, in set: inout Set<T>) {
        if set.contains(value) { set.remove(value) } else { set.insert(value) }
    }

    // MARK: Data helpers

    private var importTypes: [UTType] {
        var types: [UTType] = [.data]
        if let mbox = UTType(filenameExtension: "mbox") { types.insert(mbox, at: 0) }
        return types
    }

    private var availableCompanies: [String] {
        Set(applications.map(\.company).filter { !$0.isEmpty })
            .sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
    }

    private var availableCycles: [String] {
        Set(applications.map(\.cycle).filter { !$0.isEmpty })
            .sorted { CycleDetector.sortKey($0) > CycleDetector.sortKey($1) }
    }

    private var availableSources: [String] {
        Set(applications.compactMap(\.source).filter { !$0.isEmpty })
            .sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
    }

}
