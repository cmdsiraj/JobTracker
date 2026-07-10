//
//  TimelineScrubberView.swift
//  JobTracker
//
//  Interactive timeline range control for the pipeline: a per-month
//  activity histogram with a draggable selection window (two handles +
//  translatable middle). Snaps to month boundaries. Replaces date pickers.
//

import SwiftUI

struct TimelineScrubberView: View {
    /// Full data extent (month-floored lower bound).
    let domain: ClosedRange<Date>
    /// Applications per month across the domain.
    let histogram: [(month: Date, count: Int)]
    /// Currently selected window (month-snapped).
    @Binding var selection: ClosedRange<Date>

    private let calendar = Calendar.current
    private let barAreaHeight: CGFloat = 36
    private let handleWidth: CGFloat = 10

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Label("Timeline", systemImage: "slider.horizontal.below.rectangle")
                    .font(.caption.weight(.medium))
                    .foregroundStyle(.secondary)
                Spacer()
                Text("\(monthLabel(selection.lowerBound)) – \(monthLabel(endLabelDate))")
                    .font(.caption.weight(.semibold))
                    .contentTransition(.numericText())
                Button("Last 5 Months") {
                    withAnimation(.spring(duration: 0.35)) {
                        selection = Self.defaultWindow(clampedTo: domain)
                    }
                }
                .buttonStyle(.plain)
                .font(.caption)
                .foregroundStyle(Color.accentColor)
            }

            GeometryReader { geo in
                let width = geo.size.width
                ZStack(alignment: .leading) {
                    // Histogram bars.
                    histogramBars(width: width)

                    // Dimmed outside-selection regions.
                    let lowerX = x(for: selection.lowerBound, width: width)
                    let upperX = x(for: selection.upperBound, width: width)
                    Rectangle()
                        .fill(.background.opacity(0.55))
                        .frame(width: max(0, lowerX))
                    Rectangle()
                        .fill(.background.opacity(0.55))
                        .frame(width: max(0, width - upperX))
                        .offset(x: upperX)

                    // Selection window (draggable as a whole).
                    RoundedRectangle(cornerRadius: 6)
                        .stroke(Color.accentColor, lineWidth: 1.5)
                        .background(
                            RoundedRectangle(cornerRadius: 6)
                                .fill(Color.accentColor.opacity(0.08))
                        )
                        .frame(width: max(handleWidth, upperX - lowerX))
                        .offset(x: lowerX)
                        .gesture(windowDrag(width: width))

                    // Handles.
                    handle(at: lowerX, width: width, isLower: true)
                    handle(at: upperX, width: width, isLower: false)
                }
            }
            .frame(height: barAreaHeight)

            HStack {
                Text(monthLabel(domain.lowerBound))
                Spacer()
                Text(monthLabel(domain.upperBound))
            }
            .font(.caption2)
            .foregroundStyle(.tertiary)
        }
        .padding(12)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 10))
    }

    // MARK: - Pieces

    private func histogramBars(width: CGFloat) -> some View {
        let maxCount = max(1, histogram.map(\.count).max() ?? 1)
        return HStack(alignment: .bottom, spacing: 2) {
            ForEach(histogram, id: \.month) { entry in
                RoundedRectangle(cornerRadius: 1.5)
                    .fill(selection.contains(entry.month)
                          ? Color.accentColor.opacity(0.55)
                          : Color.secondary.opacity(0.3))
                    .frame(height: entry.count == 0
                           ? 2
                           : max(4, barAreaHeight * CGFloat(entry.count) / CGFloat(maxCount)))
                    .frame(maxWidth: .infinity)
            }
        }
        .frame(width: width, height: barAreaHeight, alignment: .bottom)
    }

    private func handle(at position: CGFloat, width: CGFloat, isLower: Bool) -> some View {
        Capsule()
            .fill(Color.accentColor)
            .frame(width: handleWidth, height: barAreaHeight + 6)
            .shadow(color: .black.opacity(0.25), radius: 2, y: 1)
            .offset(x: position - handleWidth / 2)
            .gesture(
                DragGesture(minimumDistance: 1)
                    .onChanged { value in
                        let date = snappedDate(forX: value.location.x, width: width)
                        if isLower {
                            let maxLower = calendar.date(byAdding: .month, value: -1,
                                                         to: selection.upperBound) ?? selection.upperBound
                            let lower = min(max(date, domain.lowerBound), maxLower)
                            selection = lower...selection.upperBound
                        } else {
                            let minUpper = calendar.date(byAdding: .month, value: 1,
                                                         to: selection.lowerBound) ?? selection.lowerBound
                            let upper = max(min(date, domain.upperBound), minUpper)
                            selection = selection.lowerBound...upper
                        }
                    }
            )
    }

    /// Dragging the window itself translates the whole selection.
    private func windowDrag(width: CGFloat) -> some Gesture {
        DragGesture(minimumDistance: 2)
            .onChanged { value in
                let monthsSpan = calendar.dateComponents(
                    [.month], from: selection.lowerBound, to: selection.upperBound).month ?? 1
                let date = snappedDate(forX: value.location.x, width: width)
                var lower = date
                var upper = calendar.date(byAdding: .month, value: monthsSpan, to: lower) ?? lower
                if upper > domain.upperBound {
                    upper = domain.upperBound
                    lower = calendar.date(byAdding: .month, value: -monthsSpan, to: upper) ?? lower
                }
                if lower < domain.lowerBound {
                    lower = domain.lowerBound
                    upper = calendar.date(byAdding: .month, value: monthsSpan, to: lower) ?? upper
                }
                selection = lower...max(upper, lower)
            }
    }

    // MARK: - Date ↔ position

    private func x(for date: Date, width: CGFloat) -> CGFloat {
        let total = domain.upperBound.timeIntervalSince(domain.lowerBound)
        guard total > 0 else { return 0 }
        let fraction = date.timeIntervalSince(domain.lowerBound) / total
        return width * CGFloat(min(max(fraction, 0), 1))
    }

    private func snappedDate(forX x: CGFloat, width: CGFloat) -> Date {
        let total = domain.upperBound.timeIntervalSince(domain.lowerBound)
        let fraction = Double(min(max(x / max(width, 1), 0), 1))
        let raw = domain.lowerBound.addingTimeInterval(fraction * total)
        // Snap to the nearest month boundary.
        let start = startOfMonth(raw)
        let next = calendar.date(byAdding: .month, value: 1, to: start) ?? start
        return raw.timeIntervalSince(start) < next.timeIntervalSince(raw) ? start : next
    }

    private func startOfMonth(_ date: Date) -> Date {
        calendar.date(from: calendar.dateComponents([.year, .month], from: date)) ?? date
    }

    private var endLabelDate: Date {
        // Show the last *included* month, not the exclusive boundary.
        calendar.date(byAdding: .month, value: -1, to: selection.upperBound)
            .map { max($0, selection.lowerBound) } ?? selection.lowerBound
    }

    private func monthLabel(_ date: Date) -> String {
        date.formatted(.dateTime.month(.abbreviated).year())
    }

    // MARK: - Shared data helpers

    /// Month-floored extent covering `dates` through now.
    static func domain(for dates: [Date]) -> ClosedRange<Date> {
        let calendar = Calendar.current
        let now = Date()
        let earliest = dates.min() ?? calendar.date(byAdding: .month, value: -5, to: now)!
        let floor = calendar.date(
            from: calendar.dateComponents([.year, .month], from: min(earliest, now))) ?? earliest
        return floor...max(now, floor.addingTimeInterval(86400))
    }

    /// Counts per month across `domain`.
    static func monthHistogram(dates: [Date], domain: ClosedRange<Date>) -> [(month: Date, count: Int)] {
        let calendar = Calendar.current
        var counts: [Date: Int] = [:]
        for date in dates {
            let month = calendar.date(
                from: calendar.dateComponents([.year, .month], from: date)) ?? date
            counts[month, default: 0] += 1
        }
        var result: [(Date, Int)] = []
        var cursor = domain.lowerBound
        while cursor <= domain.upperBound {
            result.append((cursor, counts[cursor] ?? 0))
            guard let next = calendar.date(byAdding: .month, value: 1, to: cursor) else { break }
            cursor = next
        }
        return result
    }

    // MARK: - Defaults

    /// Last 5 whole months through "now", clamped to the domain.
    static func defaultWindow(clampedTo domain: ClosedRange<Date>) -> ClosedRange<Date> {
        let calendar = Calendar.current
        let upper = domain.upperBound
        let lower = calendar.date(byAdding: .month, value: -5, to: upper) ?? upper
        let clampedLower = max(lower, domain.lowerBound)
        return clampedLower...max(upper, clampedLower)
    }
}
