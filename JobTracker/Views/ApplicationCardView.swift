//
//  ApplicationCardView.swift
//  JobTracker
//
//  A single draggable card on the Kanban board, with review and
//  outreach-origin indicators.
//

import SwiftUI

struct ApplicationCardView: View {
    let application: JobApplication

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline, spacing: 6) {
                Text(application.company.isEmpty ? "Unknown Company" : application.company)
                    .font(.headline)
                    .lineLimit(1)
                Spacer(minLength: 4)
                if application.status == .outreach {
                    Image(systemName: "arrow.up.right")
                        .font(.caption2.bold())
                        .foregroundStyle(.orange)
                        .help("Started by your own outreach")
                }
                if application.needsReview {
                    Image(systemName: "exclamationmark.triangle.fill")
                        .font(.caption)
                        .foregroundStyle(.yellow)
                        .help("Low-confidence match — needs review")
                }
            }

            if !application.roleTitle.isEmpty {
                Text(application.roleTitle)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
            }

            if let location = application.location, !location.isEmpty {
                Label(location, systemImage: "mappin.and.ellipse")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }

            HStack {
                if !application.cycle.isEmpty {
                    Text(application.cycle)
                        .font(.caption2)
                        .padding(.horizontal, 6).padding(.vertical, 2)
                        .background(.blue.opacity(0.15), in: Capsule())
                }
                if let source = application.source, !source.isEmpty {
                    Text(source)
                        .font(.caption2)
                        .padding(.horizontal, 6).padding(.vertical, 2)
                        .background(.quaternary, in: Capsule())
                }
                Spacer()
                Text(application.lastUpdated, format: .relative(presentation: .named))
                    .font(.caption2)
                    .foregroundStyle(.tertiary)
            }
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.background, in: RoundedRectangle(cornerRadius: 10))
        .overlay(
            RoundedRectangle(cornerRadius: 10)
                .stroke(.quaternary, lineWidth: 1)
        )
        .shadow(color: .black.opacity(0.05), radius: 2, y: 1)
    }
}
