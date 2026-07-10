//
//  NIMClient.swift
//  JobTracker
//
//  Client for NVIDIA NIM's OpenAI-compatible chat completions endpoint.
//  The API key is read from the Keychain at call time.
//

import Foundation

enum NIMError: LocalizedError {
    case missingAPIKey
    case http(Int, String)
    case rateLimited
    case emptyResponse

    var errorDescription: String? {
        switch self {
        case .missingAPIKey:     return "No NVIDIA API key set. Add it in Settings."
        case .http(let c, let b): return "NVIDIA API HTTP \(c): \(b)"
        case .rateLimited:       return "NVIDIA API rate limit hit; will retry."
        case .emptyResponse:     return "NVIDIA API returned no content."
        }
    }
}

/// Paces all LLM requests to stay under the provider's rate limit
/// (NVIDIA free tier: 40 requests/minute → one request per 1.5 s; a small
/// margin keeps bursts safely below the cap). Every request in the app
/// funnels through NIMClient, so this is the single enforcement point.
@MainActor
final class RequestThrottle {
    static let shared = RequestThrottle()
    private var lastRequestStart: Date = .distantPast
    private let minInterval: TimeInterval = 1.6

    private init() {}

    /// Waits until it's safe to start the next request.
    func waitTurn() async {
        let elapsed = Date().timeIntervalSince(lastRequestStart)
        if elapsed < minInterval {
            try? await Task.sleep(for: .seconds(minInterval - elapsed))
        }
        lastRequestStart = Date()
    }
}

struct NIMClient {
    var model: String

    struct Message: Encodable {
        let role: String
        let content: String
    }

    /// Sends a chat completion to the active provider (Settings → Account)
    /// and returns the assistant's message content. The API key comes from
    /// the in-memory secrets cache (one Keychain read per launch).
    func complete(messages: [Message], maxTokens: Int = 800, temperature: Double = 0.1) async throws -> String {
        let provider = Preferences.shared.provider
        guard let apiKey = Secrets.shared.get(provider.secretKey), !apiKey.isEmpty else {
            throw NIMError.missingAPIKey
        }
        guard let endpoint = Preferences.shared.effectiveBaseURL else {
            throw NIMError.http(-1, "No endpoint URL configured for the custom provider")
        }

        await RequestThrottle.shared.waitTurn()

        var request = URLRequest(url: endpoint)
        request.httpMethod = "POST"
        request.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")

        let payload = RequestBody(
            model: model,
            messages: messages,
            temperature: temperature,
            max_tokens: maxTokens,
            // NVIDIA-only knob (disables reasoning traces for clean JSON);
            // other providers can reject unknown fields, so omit it there.
            chat_template_kwargs: provider == .nvidia ? .init(enable_thinking: false) : nil
        )
        request.httpBody = try JSONEncoder().encode(payload)

        let (data, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse else { throw NIMError.emptyResponse }
        if http.statusCode == 429 { throw NIMError.rateLimited }
        guard (200..<300).contains(http.statusCode) else {
            throw NIMError.http(http.statusCode, String(data: data, encoding: .utf8) ?? "")
        }

        let decoded = try JSONDecoder().decode(ResponseBody.self, from: data)
        guard let content = decoded.choices.first?.message.content, !content.isEmpty else {
            throw NIMError.emptyResponse
        }
        return content
    }

    // MARK: - Wire types

    private struct RequestBody: Encodable {
        struct ChatTemplateKwargs: Encodable { let enable_thinking: Bool }
        let model: String
        let messages: [Message]
        let temperature: Double
        let max_tokens: Int
        let chat_template_kwargs: ChatTemplateKwargs?
    }

    private struct ResponseBody: Decodable {
        struct Choice: Decodable {
            struct Msg: Decodable { let content: String? }
            let message: Msg
        }
        let choices: [Choice]
    }
}
