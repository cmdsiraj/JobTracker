//
//  GmailAPIClient.swift
//  JobTracker
//
//  Thin async wrapper over the Gmail REST API v1. Handles the profile call,
//  timestamp-watermark message listing (inbox + sent), and fetching +
//  decoding individual messages.
//

import Foundation

/// A parsed Gmail message reduced to what the classifier needs.
/// `nonisolated`: constructed inside the detached mbox-parse task.
nonisolated struct FetchedMessage {
    let id: String
    let threadId: String
    let sender: String
    let subject: String
    let snippet: String
    let body: String
    /// True when the user sent this message (outreach, replies, follow-ups).
    var isOutgoing: Bool = false
    let date: Date
}

enum GmailAPIError: LocalizedError {
    case http(Int, String)
    case decoding(String)

    var errorDescription: String? {
        switch self {
        case .http(let code, let body): return "Gmail API HTTP \(code): \(body)"
        case .decoding(let m):          return "Gmail decode error: \(m)"
        }
    }
}

struct GmailAPIClient {
    let auth: GmailAuthService

    private func authorizedRequest(path: String, queryItems: [URLQueryItem] = []) async throws -> URLRequest {
        let token = try await auth.validAccessToken()
        var components = URLComponents(url: AppConfig.gmailBaseURL.appendingPathComponent(path),
                                       resolvingAgainstBaseURL: false)!
        if !queryItems.isEmpty { components.queryItems = queryItems }
        var request = URLRequest(url: components.url!)
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        return request
    }

    private func perform(_ request: URLRequest) async throws -> Data {
        let (data, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse else {
            throw GmailAPIError.http(-1, "No HTTP response")
        }
        guard (200..<300).contains(http.statusCode) else {
            throw GmailAPIError.http(http.statusCode, String(data: data, encoding: .utf8) ?? "")
        }
        return data
    }

    // MARK: - Profile

    struct Profile: Decodable {
        let emailAddress: String
        let historyId: String
    }

    func profile() async throws -> Profile {
        let request = try await authorizedRequest(path: "users/me/profile")
        let data = try await perform(request)
        return try JSONDecoder().decode(Profile.self, from: data)
    }

    // MARK: - Timestamp-based listing

    /// Returns ids of inbox + sent messages after `date` (the sync
    /// watermark). Sent mail is included so the user's own outreach emails
    /// are captured. Deliberately independent of read/unread state — the
    /// user may read mail on other devices.
    func messageIDs(after date: Date, maxResults: Int = 500) async throws -> [String] {
        let epoch = Int(date.timeIntervalSince1970)
        return try await listMessageIDs(query: "(in:inbox OR in:sent) after:\(epoch)", maxResults: maxResults)
    }

    /// Returns recent inbox + sent message ids from the last `days` days
    /// (first sync when no archive was imported).
    func recentMessageIDs(days: Int, maxResults: Int = 200) async throws -> [String] {
        try await listMessageIDs(query: "(in:inbox OR in:sent) newer_than:\(days)d", maxResults: maxResults)
    }

    private func listMessageIDs(query: String, maxResults: Int) async throws -> [String] {
        var ids: [String] = []
        var pageToken: String?

        repeat {
            var items: [URLQueryItem] = [
                .init(name: "q", value: query),
                .init(name: "maxResults", value: "100")
            ]
            if let pageToken { items.append(.init(name: "pageToken", value: pageToken)) }
            let request = try await authorizedRequest(path: "users/me/messages", queryItems: items)
            let data = try await perform(request)
            let page = try JSONDecoder().decode(MessageListResponse.self, from: data)
            ids.append(contentsOf: (page.messages ?? []).map(\.id))
            pageToken = page.nextPageToken
        } while pageToken != nil && ids.count < maxResults

        return Array(ids.prefix(maxResults))
    }

    private struct MessageListResponse: Decodable {
        struct Ref: Decodable { let id: String }
        let messages: [Ref]?
        let nextPageToken: String?
    }

    // MARK: - Fetch a single message

    func message(id: String) async throws -> FetchedMessage {
        let request = try await authorizedRequest(
            path: "users/me/messages/\(id)",
            queryItems: [.init(name: "format", value: "full")]
        )
        let data = try await perform(request)
        let raw = try JSONDecoder().decode(RawMessage.self, from: data)

        let headers = raw.payload?.headers ?? []
        func header(_ name: String) -> String {
            headers.first { $0.name.caseInsensitiveCompare(name) == .orderedSame }?.value ?? ""
        }

        let dateMillis = Double(raw.internalDate ?? "") ?? 0
        return FetchedMessage(
            id: raw.id,
            threadId: raw.threadId,
            sender: header("From"),
            subject: header("Subject"),
            snippet: raw.snippet ?? "",
            body: Self.extractBody(from: raw.payload),
            isOutgoing: (raw.labelIds ?? []).contains("SENT"),
            date: Date(timeIntervalSince1970: dateMillis / 1000)
        )
    }

    // MARK: - Body decoding

    private struct RawMessage: Decodable {
        let id: String
        let threadId: String
        let snippet: String?
        let internalDate: String?
        let labelIds: [String]?
        let payload: Part?
    }

    private struct Part: Decodable {
        let mimeType: String?
        let headers: [Header]?
        let body: Body?
        let parts: [Part]?
    }
    private struct Header: Decodable { let name: String; let value: String }
    private struct Body: Decodable { let data: String? }

    /// Depth-first search for the first text/plain part; falls back to text/html
    /// (stripped) or any decodable data.
    private static func extractBody(from part: Part?) -> String {
        guard let part else { return "" }

        if part.mimeType == "text/plain", let text = decode(part.body?.data) {
            return text
        }
        if let parts = part.parts {
            for child in parts {
                let text = extractBody(from: child)
                if !text.isEmpty { return text }
            }
        }
        if part.mimeType == "text/html", let html = decode(part.body?.data) {
            return stripHTML(html)
        }
        return decode(part.body?.data) ?? ""
    }

    private static func decode(_ base64URL: String?) -> String? {
        guard var value = base64URL, !value.isEmpty else { return nil }
        value = value.replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        while value.count % 4 != 0 { value += "=" }
        guard let data = Data(base64Encoded: value) else { return nil }
        return String(data: data, encoding: .utf8)
    }

    private static func stripHTML(_ html: String) -> String {
        html.replacingOccurrences(of: "<[^>]+>", with: " ", options: .regularExpression)
            .replacingOccurrences(of: "&nbsp;", with: " ")
            .replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression)
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
