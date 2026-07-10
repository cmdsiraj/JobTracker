//
//  CycleDetector.swift
//  JobTracker
//
//  Fallback heuristic that extracts a recruiting cycle ("Summer 2026",
//  "Fall 2025", "New Grad 2026") from free text when the LLM doesn't
//  return one explicitly.
//

import Foundation

enum CycleDetector {

    private static let seasonPattern = "(spring|summer|fall|autumn|winter)"
    private static let yearPattern = "(20\\d{2})"

    /// Returns a normalized cycle like "Summer 2026", or nil if none found.
    static func detect(in text: String) -> String? {
        let lower = text.lowercased()

        // "Summer 2026", "summer intern 2026", "summer-2026"
        if let match = firstMatch("\(seasonPattern)[\\s,‐–-]{0,3}\(yearPattern)", in: lower) {
            return normalize(season: match.0, year: match.1)
        }
        // "2026 Summer"
        if let match = firstMatch("\(yearPattern)[\\s,‐–-]{0,3}\(seasonPattern)", in: lower) {
            return normalize(season: match.1, year: match.0)
        }
        // "New Grad 2026" / "University Grad 2026"
        if let match = firstMatch("(new\\s*grad|university\\s*grad|graduate)[\\s,‐–-]{0,3}\(yearPattern)", in: lower) {
            return "New Grad \(match.1)"
        }
        return nil
    }

    /// Sort key: newer cycles first, then by season within a year.
    static func sortKey(_ cycle: String) -> (Int, Int) {
        let year = Int(cycle.components(separatedBy: CharacterSet.decimalDigits.inverted)
            .first { $0.count == 4 } ?? "") ?? 0
        let lower = cycle.lowercased()
        let season: Int
        if lower.contains("spring") { season = 0 }
        else if lower.contains("summer") { season = 1 }
        else if lower.contains("fall") || lower.contains("autumn") { season = 2 }
        else if lower.contains("winter") { season = 3 }
        else { season = 4 }
        return (year, season)
    }

    private static func normalize(season: String, year: String) -> String {
        let name = season == "autumn" ? "Fall" : season.capitalized
        return "\(name) \(year)"
    }

    /// Returns (group1, group2) of the first match, or nil.
    private static func firstMatch(_ pattern: String, in text: String) -> (String, String)? {
        guard let regex = try? NSRegularExpression(pattern: pattern),
              let match = regex.firstMatch(in: text, range: NSRange(text.startIndex..., in: text)),
              match.numberOfRanges >= 3,
              let r1 = Range(match.range(at: 1), in: text),
              let r2 = Range(match.range(at: 2), in: text) else { return nil }
        return (String(text[r1]), String(text[r2]))
    }
}
