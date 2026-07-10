//
//  EmailClassifier.swift
//  JobTracker
//
//  Turns Gmail messages into structured classifications using the NVIDIA
//  NIM LLM. Emails are classified in batches (AppConfig.classificationBatchSize
//  per request) which divides request count — and free-tier credit burn —
//  by the batch size. The heuristic prefilter runs first so obvious noise
//  never reaches the network.
//

import Foundation

struct ClassificationResult: Codable {
    var index: Int?
    var isJobRelated: Bool
    var company: String
    var role: String
    var status: String
    var location: String?
    var source: String?
    var nextAction: String?
    var cycle: String?
    var confidence: Double

    var applicationStatus: ApplicationStatus? {
        ApplicationStatus.from(llmString: status)
    }

    static func notJobRelated(index: Int? = nil) -> ClassificationResult {
        ClassificationResult(index: index, isJobRelated: false, company: "", role: "",
                             status: "unknown", location: nil, source: nil,
                             nextAction: nil, cycle: nil, confidence: 1.0)
    }
}

struct EmailClassifier {
    var nim: NIMClient

    private static let batchSystemPrompt = """
    You are an assistant that reads emails and decides, for each one, whether it \
    relates to the recipient's own job application(s), and if so extracts structured data.

    You will receive several emails, each preceded by "=== EMAIL <index> ===".

    Respond with ONLY a JSON array (no prose, no markdown fences) containing one \
    object per email:
    {
      "index": number,            // the email's index, exactly as given
      "isJobRelated": bool,       // true only if about the recipient's own application/hiring process
      "company": string,          // hiring company name, "" if unknown
      "role": string,             // job title, "" if unknown
      "status": string,           // one of: outreach, applied, assessment, recruiter call, interview, final round, offer, rejected, unknown
      "location": string|null,
      "source": string|null,      // e.g. LinkedIn, Greenhouse, company site
      "nextAction": string|null,  // any action the recipient must take
      "cycle": string|null,       // recruiting cycle, e.g. "Summer 2026", "Fall 2025", "New Grad 2026"
      "confidence": number        // 0.0 - 1.0
    }

    Status guidance:
    - outreach: an email the recipient themselves sent to a company/recruiter (cold email, referral request, follow-up)
    - applied: confirmation an application was received/submitted
    - assessment: online assessment, take-home, coding challenge invitation
    - recruiter call: recruiter reaching out, scheduling an intro/phone screen
    - interview: technical/behavioral interview rounds
    - final round: onsite or final interview loop
    - offer: an offer is extended
    - rejected: declined / not moving forward
    Newsletters, job alerts, marketing, and generic "jobs you may like" emails are NOT job-related.
    """

    /// Classifies up to `AppConfig.classificationBatchSize` messages in one
    /// LLM request. Returns results keyed by position in `messages`.
    /// Messages rejected by the prefilter never reach the network.
    func classify(batch messages: [FetchedMessage]) async throws -> [Int: ClassificationResult] {
        var results: [Int: ClassificationResult] = [:]
        var toSend: [(position: Int, message: FetchedMessage)] = []

        for (position, message) in messages.enumerated() {
            if HeuristicPrefilter.isLikelyJobRelated(
                sender: message.sender, subject: message.subject,
                body: message.body, isOutgoing: message.isOutgoing
            ) {
                toSend.append((position, message))
            } else {
                results[position] = .notJobRelated(index: position)
            }
        }
        guard !toSend.isEmpty else { return results }

        let userPrompt = toSend.map { position, message in
            """
            === EMAIL \(position) ===
            Direction: \(message.isOutgoing ? "SENT BY RECIPIENT (outreach)" : "RECEIVED")
            From: \(message.sender)
            Subject: \(message.subject)
            Date: \(message.date.ISO8601Format())
            Body:
            \(String(message.body.prefix(2500)))
            """
        }.joined(separator: "\n\n")

        let content = try await nim.complete(
            messages: [
                .init(role: "system", content: Self.batchSystemPrompt),
                .init(role: "user", content: userPrompt)
            ],
            maxTokens: 350 * toSend.count
        )

        for parsed in try Self.parseArray(content) {
            guard let index = parsed.index,
                  toSend.contains(where: { $0.position == index }) else { continue }
            results[index] = parsed
        }

        // Anything the model failed to answer counts as unclassified (skipped).
        for (position, _) in toSend where results[position] == nil {
            results[position] = .notJobRelated(index: position)
        }
        return results
    }

    // MARK: - Parsing

    /// Extracts the JSON array from model output (tolerant of stray text or
    /// fences) and decodes it.
    static func parseArray(_ content: String) throws -> [ClassificationResult] {
        var text = content.trimmingCharacters(in: .whitespacesAndNewlines)
        if let start = text.firstIndex(of: "["), let end = text.lastIndex(of: "]") {
            text = String(text[start...end])
        } else if let start = text.firstIndex(of: "{"), let end = text.lastIndex(of: "}") {
            // Model answered with a single object; accept it.
            text = "[\(String(text[start...end]))]"
        }
        guard let data = text.data(using: .utf8) else { throw NIMError.emptyResponse }
        return try JSONDecoder().decode([ClassificationResult].self, from: data)
    }
}
