//
//  DashboardView.swift
//  JobTracker
//
//  Analytics overview with scope controls: pick a time window (month, year,
//  custom dates) and recruiting cycles, and every stat and chart follows.
//
//  Performance: all statistics are computed in ONE pass over the data and
//  cached in `DashboardData` (a plain value in @State). They recompute only
//  when the store or the scope changes — never per render. The old version
//  recomputed every chart on every body evaluation, which caused visible
//  lag during syncs on large datasets.
//

import SwiftUI
import SwiftData
import Charts

// MARK: - Precomputed stats

/// Everything the dashboard renders, computed in one pass.
private struct DashboardData {
    var total = 0
    var thisWeek = 0
    var thisMonth = 0
    var thisYear = 0
    var active = 0
    var statusCounts: [(status: ApplicationStatus, count: Int)] = []
    var daily: [(date: Date, count: Int)] = []
    var weekly: [(date: Date, count: Int)] = []
    var funnel: [(status: ApplicationStatus, count: Int)] = []
    var cycles: [(cycle: String, count: Int)] = []
    var heat: [HeatCell] = []
    var isEmpty: Bool { total == 0 }

    struct HeatCell: Identifiable {
        let id: String
        let weekLabel: String
        let dayLabel: String
        let count: Int
    }
}

// MARK: - View

struct DashboardView: View {
    @Query(sort: \JobApplication.lastUpdated, order: .reverse)
    private var applications: [JobApplication]

    /// Timeline window driven by the interactive scrubber (nil = not yet
    /// initialized; defaults to the last 5 months on first appearance).
    @State private var timeline: ClosedRange<Date>?
    @State private var selectedCycles: Set<String> = []
    @State private var data = DashboardData()

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                scopeBar
                if data.isEmpty {
                    ContentUnavailableView(
                        "No Data in This Scope",
                        systemImage: "chart.bar.xaxis",
                        description: Text("Adjust the time range or cycle filters, run a sync, or import a mail archive.")
                    )
                    .frame(minHeight: 320)
                } else {
                    headlineStats
                    statusBreakdown
                    chartGrid
                }
            }
            .padding(20)
        }
        .task {
            if timeline == nil {
                timeline = TimelineScrubberView.defaultWindow(clampedTo: scrubberDomain)
            }
            recompute()
        }
        .onChange(of: applications.count) { recompute() }
        .onChange(of: timeline) { recompute() }
        .onChange(of: selectedCycles) { recompute() }
    }

    // MARK: Scope controls

    private var scrubberDomain: ClosedRange<Date> {
        TimelineScrubberView.domain(for: applications.map { $0.appliedDate ?? $0.lastUpdated })
    }

    private var scopeBar: some View {
        let domain = scrubberDomain
        return VStack(alignment: .leading, spacing: 10) {
            TimelineScrubberView(
                domain: domain,
                histogram: TimelineScrubberView.monthHistogram(
                    dates: applications.map { $0.appliedDate ?? $0.lastUpdated },
                    domain: domain),
                selection: Binding(
                    get: { timeline ?? TimelineScrubberView.defaultWindow(clampedTo: domain) },
                    set: { timeline = $0 }
                )
            )

            if !availableCycles.isEmpty {
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        cycleChip("All Cycles", isOn: selectedCycles.isEmpty) {
                            selectedCycles = []
                        }
                        ForEach(availableCycles, id: \.self) { cycle in
                            cycleChip(cycle, isOn: selectedCycles.contains(cycle)) {
                                if selectedCycles.contains(cycle) {
                                    selectedCycles.remove(cycle)
                                } else {
                                    selectedCycles.insert(cycle)
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private func cycleChip(_ title: String, isOn: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(title)
                .font(.callout.weight(isOn ? .semibold : .regular))
                .padding(.horizontal, 12).padding(.vertical, 5)
                .background(isOn ? AnyShapeStyle(Color.accentColor.opacity(0.18))
                                 : AnyShapeStyle(.quaternary.opacity(0.5)),
                            in: Capsule())
                .overlay(Capsule().stroke(isOn ? Color.accentColor : .clear, lineWidth: 1))
        }
        .buttonStyle(.plain)
    }

    // MARK: Headline stats

    private var headlineStats: some View {
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 160), spacing: 12)], spacing: 12) {
            statCard("Total", value: data.total, systemImage: "tray.full.fill", tint: .blue)
            statCard("This Week", value: data.thisWeek, systemImage: "calendar.badge.clock", tint: .purple)
            statCard("This Month", value: data.thisMonth, systemImage: "calendar", tint: .teal)
            statCard("This Year", value: data.thisYear, systemImage: "sparkles", tint: .orange)
            statCard("Active", value: data.active, systemImage: "bolt.fill", tint: .green)
        }
    }

    private func statCard(_ title: String, value: Int,
                          systemImage: String, tint: Color) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Label(title, systemImage: systemImage)
                .font(.caption.weight(.medium))
                .foregroundStyle(tint)
            Text("\(value)")
                .font(.system(.title, design: .rounded, weight: .bold))
                .contentTransition(.numericText())
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12))
    }

    // MARK: Status breakdown

    private var statusBreakdown: some View {
        card("Pipeline at a Glance", systemImage: "square.stack.3d.up") {
            HStack(spacing: 0) {
                ForEach(ApplicationStatus.allCases) { status in
                    let count = data.statusCounts.first { $0.status == status }?.count ?? 0
                    VStack(spacing: 4) {
                        Image(systemName: status.systemImage)
                            .foregroundStyle(status.color)
                        Text("\(count)")
                            .font(.headline.monospacedDigit())
                        Text(status.displayName)
                            .font(.caption2)
                            .foregroundStyle(.secondary)
                            .lineLimit(1)
                            .minimumScaleFactor(0.7)
                    }
                    .frame(maxWidth: .infinity)
                }
            }
        }
    }

    // MARK: Charts

    private var chartGrid: some View {
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 380), spacing: 16)], spacing: 16) {
            dailyActivityCard
            weeklyActivityCard
            funnelCard
            donutCard
            heatmapCard
            cycleCard
        }
    }

    private var dailyActivityCard: some View {
        card("Daily Activity", systemImage: "chart.xyaxis.line") {
            Chart(data.daily, id: \.date) { point in
                AreaMark(x: .value("Day", point.date, unit: .day),
                         y: .value("Applications", point.count))
                    .foregroundStyle(.linearGradient(colors: [.blue.opacity(0.35), .blue.opacity(0.02)],
                                                     startPoint: .top, endPoint: .bottom))
                    .interpolationMethod(.monotone)
                LineMark(x: .value("Day", point.date, unit: .day),
                         y: .value("Applications", point.count))
                    .foregroundStyle(.blue)
                    .interpolationMethod(.monotone)
            }
            .chartYAxis { AxisMarks(position: .trailing) }
            .frame(height: 180)
        }
    }

    private var weeklyActivityCard: some View {
        card("Applications per Week", systemImage: "chart.bar.fill") {
            Chart(data.weekly, id: \.date) { point in
                BarMark(x: .value("Week", point.date, unit: .weekOfYear),
                        y: .value("Applications", point.count))
                    .foregroundStyle(.teal.gradient)
                    .cornerRadius(3)
            }
            .chartYAxis { AxisMarks(position: .trailing) }
            .frame(height: 180)
        }
    }

    private var funnelCard: some View {
        card("Pipeline Funnel", systemImage: "triangle.fill") {
            Chart(data.funnel, id: \.status) { point in
                BarMark(x: .value("Count", point.count),
                        y: .value("Stage", point.status.displayName))
                    .foregroundStyle(point.status.color.gradient)
                    .cornerRadius(4)
                    .annotation(position: .trailing) {
                        Text("\(point.count)")
                            .font(.caption.monospacedDigit())
                            .foregroundStyle(.secondary)
                    }
            }
            .chartYScale(domain: ApplicationStatus.funnelStages.map(\.displayName))
            .chartXAxis(.hidden)
            .frame(height: 200)
        }
    }

    private var donutCard: some View {
        card("Current Status Mix", systemImage: "chart.pie.fill") {
            HStack(spacing: 16) {
                Chart(data.statusCounts, id: \.status) { point in
                    SectorMark(angle: .value("Count", point.count),
                               innerRadius: .ratio(0.62),
                               angularInset: 1.5)
                        .foregroundStyle(point.status.color.gradient)
                        .cornerRadius(3)
                }
                .frame(height: 190)
                .chartBackground { _ in
                    VStack(spacing: 0) {
                        Text("\(data.total)")
                            .font(.system(.title2, design: .rounded, weight: .bold))
                        Text("total")
                            .font(.caption2)
                            .foregroundStyle(.secondary)
                    }
                }

                VStack(alignment: .leading, spacing: 5) {
                    ForEach(data.statusCounts, id: \.status) { point in
                        HStack(spacing: 6) {
                            Circle().fill(point.status.color).frame(width: 8, height: 8)
                            Text(point.status.displayName).font(.caption)
                            Spacer(minLength: 4)
                            Text("\(point.count)")
                                .font(.caption.monospacedDigit())
                                .foregroundStyle(.secondary)
                        }
                    }
                }
                .frame(width: 130)
            }
        }
    }

    private var heatmapCard: some View {
        card("Activity Heatmap — Last 16 Weeks", systemImage: "square.grid.3x3.fill") {
            let maxCount = max(1, data.heat.map(\.count).max() ?? 1)
            let weekDomain = data.heat.map(\.weekLabel).reduce(into: [String]()) {
                if $0.last != $1 { $0.append($1) }
            }
            Chart(data.heat) { cell in
                RectangleMark(x: .value("Week", cell.weekLabel),
                              y: .value("Day", cell.dayLabel),
                              width: .ratio(0.9), height: .ratio(0.9))
                    .foregroundStyle(Color.green.opacity(
                        cell.count == 0 ? 0.08 : 0.25 + 0.75 * Double(cell.count) / Double(maxCount)))
                    .cornerRadius(2)
            }
            .chartXScale(domain: weekDomain)
            .chartYScale(domain: Self.weekdayLabels.reversed())
            .chartXAxis {
                AxisMarks { value in
                    if let label = value.as(String.self), !label.isEmpty {
                        AxisValueLabel { Text(label).font(.caption2) }
                    }
                }
            }
            .chartYAxis {
                AxisMarks { value in
                    AxisValueLabel {
                        if let label = value.as(String.self) { Text(label).font(.caption2) }
                    }
                }
            }
            .frame(height: 180)
        }
    }

    private var cycleCard: some View {
        card("Cycle Comparison", systemImage: "arrow.triangle.2.circlepath.circle") {
            if data.cycles.isEmpty {
                Text("No recruiting cycles detected yet.")
                    .font(.callout).foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, minHeight: 120)
            } else {
                Chart(data.cycles, id: \.cycle) { point in
                    BarMark(x: .value("Cycle", point.cycle),
                            y: .value("Applications", point.count))
                        .foregroundStyle(.indigo.gradient)
                        .cornerRadius(4)
                        .annotation(position: .top) {
                            Text("\(point.count)")
                                .font(.caption.monospacedDigit())
                                .foregroundStyle(.secondary)
                        }
                }
                .chartYAxis(.hidden)
                .frame(height: 180)
            }
        }
    }

    private func card<Content: View>(_ title: String, systemImage: String,
                                     @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            Label(title, systemImage: systemImage).font(.headline)
            content()
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12))
    }

    // MARK: - Single-pass computation

    private var availableCycles: [String] {
        // Cheap: set-build over one field; only used by the chip bar.
        Set(applications.map(\.cycle).filter { !$0.isEmpty })
            .sorted { CycleDetector.sortKey($0) > CycleDetector.sortKey($1) }
    }

    private func recompute() {
        let calendar = Calendar.current
        let now = Date()
        let range = timeline

        let weekCutoff = calendar.date(byAdding: .day, value: -7, to: now)!
        let monthCutoff = calendar.date(byAdding: .day, value: -30, to: now)!
        let yearStart = calendar.date(from: calendar.dateComponents([.year], from: now))!

        // Chart windows: within the scope, daily covers the last 30 days of
        // the range, weekly the last 12 weeks, the heatmap the last 16 weeks.
        let rangeEnd = range?.upperBound ?? now
        let dailyStart = calendar.startOfDay(for: calendar.date(byAdding: .day, value: -29, to: rangeEnd)!)
        let weeklyStart = calendar.date(byAdding: .weekOfYear, value: -11,
                                        to: calendar.startOfWeek(for: rangeEnd))!
        let heatStart = calendar.date(byAdding: .weekOfYear, value: -15,
                                      to: calendar.startOfWeek(for: rangeEnd))!

        var result = DashboardData()
        var statusTally: [ApplicationStatus: Int] = [:]
        var dayBuckets: [Date: Int] = [:]
        var weekBuckets: [Date: Int] = [:]
        var heatCounts: [Date: Int] = [:]
        var cycleTally: [String: Int] = [:]
        var reachedRanks: [Int] = []

        // ONE pass over applications (events touched once, only for funnel).
        for app in applications {
            let date = app.appliedDate ?? app.lastUpdated
            if let range, !range.contains(date) { continue }
            if !selectedCycles.isEmpty, !selectedCycles.contains(app.cycle) { continue }

            result.total += 1
            if app.status.isActive { result.active += 1 }
            if date >= weekCutoff { result.thisWeek += 1 }
            if date >= monthCutoff { result.thisMonth += 1 }
            if date >= yearStart { result.thisYear += 1 }
            statusTally[app.status, default: 0] += 1
            if !app.cycle.isEmpty { cycleTally[app.cycle, default: 0] += 1 }

            let day = calendar.startOfDay(for: date)
            if day >= dailyStart { dayBuckets[day, default: 0] += 1 }
            let week = calendar.startOfWeek(for: date)
            if week >= weeklyStart { weekBuckets[week, default: 0] += 1 }
            if day >= heatStart { heatCounts[day, default: 0] += 1 }

            var best = app.status == .rejected ? 0 : app.status.rank
            for event in app.events ?? [] {
                if let detected = event.detectedStatus, detected != .rejected {
                    best = max(best, detected.rank)
                }
            }
            reachedRanks.append(best)
        }

        result.statusCounts = ApplicationStatus.allCases
            .compactMap { status in
                let count = statusTally[status] ?? 0
                return count > 0 ? (status, count) : nil
            }
        result.funnel = ApplicationStatus.funnelStages.map { stage in
            (stage, reachedRanks.filter { $0 >= stage.rank }.count)
        }
        result.cycles = cycleTally.map { ($0.key, $0.value) }
            .sorted { CycleDetector.sortKey($0.0) < CycleDetector.sortKey($1.0) }

        // Zero-filled chart series.
        result.daily = (0..<30).map { offset in
            let day = calendar.date(byAdding: .day, value: offset, to: dailyStart)!
            return (day, dayBuckets[day] ?? 0)
        }
        result.weekly = (0..<12).map { offset in
            let week = calendar.date(byAdding: .weekOfYear, value: offset, to: weeklyStart)!
            return (week, weekBuckets[week] ?? 0)
        }

        // Heatmap cells.
        let monthFormat = Date.FormatStyle().month(.abbreviated)
        var cells: [DashboardData.HeatCell] = []
        var lastMonth = ""
        for weekIndex in 0..<16 {
            let weekStart = calendar.date(byAdding: .weekOfYear, value: weekIndex, to: heatStart)!
            let month = weekStart.formatted(monthFormat)
            let weekLabel = month == lastMonth
                ? String(repeating: " ", count: weekIndex + 1) : month
            lastMonth = month
            for dayIndex in 0..<7 {
                let day = calendar.date(byAdding: .day, value: dayIndex, to: weekStart)!
                cells.append(.init(id: "\(weekIndex)-\(dayIndex)",
                                   weekLabel: weekLabel,
                                   dayLabel: Self.weekdayLabels[dayIndex],
                                   count: heatCounts[calendar.startOfDay(for: day)] ?? 0))
            }
        }
        result.heat = cells

        data = result
    }

    private static let weekdayLabels = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"]
}

// MARK: - Calendar helper

extension Calendar {
    /// Monday-based start of the week containing `date`.
    func startOfWeek(for date: Date) -> Date {
        var calendar = self
        calendar.firstWeekday = 2
        let components = calendar.dateComponents([.yearForWeekOfYear, .weekOfYear], from: date)
        return calendar.date(from: components) ?? startOfDay(for: date)
    }
}
