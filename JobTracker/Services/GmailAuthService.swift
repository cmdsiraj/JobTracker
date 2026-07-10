//
//  GmailAuthService.swift
//  JobTracker
//
//  Dependency-free Google OAuth 2.0 for a native macOS app using PKCE and
//  ASWebAuthenticationSession. Tokens are persisted in the Keychain and
//  refreshed on demand.
//

import Foundation
import AuthenticationServices
import CryptoKit
import AppKit

enum AuthError: LocalizedError {
    case notConfigured
    case cancelled
    case badResponse(String)
    case noRefreshToken

    var errorDescription: String? {
        switch self {
        case .notConfigured:   return "Google client ID is not set. Add it in Settings or AppConfig.swift."
        case .cancelled:       return "Sign-in was cancelled."
        case .badResponse(let m): return "OAuth error: \(m)"
        case .noRefreshToken:  return "No refresh token available. Please sign in again."
        }
    }
}

@MainActor
@Observable
final class GmailAuthService: NSObject {

    var isSignedIn: Bool = Secrets.shared.get(.gmailRefreshToken) != nil
    var accountEmail: String?

    private var currentSession: ASWebAuthenticationSession?

    // MARK: - Sign in

    func signIn() async throws {
        guard AppConfig.isGoogleConfigured else { throw AuthError.notConfigured }

        let verifier = Self.randomCodeVerifier()
        let challenge = Self.codeChallenge(for: verifier)

        var components = URLComponents(url: AppConfig.authorizationEndpoint, resolvingAgainstBaseURL: false)!
        components.queryItems = [
            .init(name: "client_id", value: AppConfig.googleClientID),
            .init(name: "redirect_uri", value: AppConfig.redirectURI),
            .init(name: "response_type", value: "code"),
            .init(name: "scope", value: AppConfig.gmailScope),
            .init(name: "code_challenge", value: challenge),
            .init(name: "code_challenge_method", value: "S256"),
            .init(name: "access_type", value: "offline"),
            .init(name: "prompt", value: "consent")
        ]

        let callbackURL = try await presentAuthSession(url: components.url!)

        guard let code = URLComponents(url: callbackURL, resolvingAgainstBaseURL: false)?
            .queryItems?.first(where: { $0.name == "code" })?.value else {
            throw AuthError.badResponse("Missing authorization code")
        }

        try await exchangeCode(code, verifier: verifier)
        isSignedIn = true
    }

    private func presentAuthSession(url: URL) async throws -> URL {
        try await withCheckedThrowingContinuation { continuation in
            let session = ASWebAuthenticationSession(
                url: url,
                callbackURLScheme: AppConfig.callbackURLScheme
            ) { callbackURL, error in
                if let callbackURL {
                    continuation.resume(returning: callbackURL)
                } else if let error = error as? ASWebAuthenticationSessionError,
                          error.code == .canceledLogin {
                    continuation.resume(throwing: AuthError.cancelled)
                } else {
                    continuation.resume(throwing: error ?? AuthError.cancelled)
                }
            }
            session.presentationContextProvider = self
            session.prefersEphemeralWebBrowserSession = false
            self.currentSession = session
            session.start()
        }
    }

    // MARK: - Token exchange & refresh

    private func exchangeCode(_ code: String, verifier: String) async throws {
        let params = [
            "client_id": AppConfig.googleClientID,
            "code": code,
            "code_verifier": verifier,
            "grant_type": "authorization_code",
            "redirect_uri": AppConfig.redirectURI
        ]
        let token = try await postToken(params)
        persist(token)
        if let idToken = token.idToken { accountEmail = Self.email(fromIDToken: idToken) }
    }

    /// Returns a valid access token, refreshing if necessary.
    func validAccessToken() async throws -> String {
        if let token = Secrets.shared.get(.gmailAccessToken),
           let expiryString = Secrets.shared.get(.gmailAccessTokenExpiry),
           let expiry = TimeInterval(expiryString),
           Date().timeIntervalSince1970 < expiry - 60 {
            return token
        }
        return try await refresh()
    }

    private func refresh() async throws -> String {
        guard let refreshToken = Secrets.shared.get(.gmailRefreshToken) else {
            throw AuthError.noRefreshToken
        }
        let params = [
            "client_id": AppConfig.googleClientID,
            "refresh_token": refreshToken,
            "grant_type": "refresh_token"
        ]
        let token = try await postToken(params)
        persist(token)
        guard let access = token.accessToken else { throw AuthError.badResponse("No access token") }
        return access
    }

    private func postToken(_ params: [String: String]) async throws -> TokenResponse {
        var request = URLRequest(url: AppConfig.tokenEndpoint)
        request.httpMethod = "POST"
        request.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        request.httpBody = params
            .map { "\($0.key)=\($0.value.addingPercentEncoding(withAllowedCharacters: .urlQueryValueAllowed) ?? $0.value)" }
            .joined(separator: "&")
            .data(using: .utf8)

        let (data, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
            throw AuthError.badResponse(String(data: data, encoding: .utf8) ?? "Unknown")
        }
        return try JSONDecoder().decode(TokenResponse.self, from: data)
    }

    private func persist(_ token: TokenResponse) {
        if let access = token.accessToken {
            Secrets.shared.set(access, for: .gmailAccessToken)
            let expiry = Date().timeIntervalSince1970 + Double(token.expiresIn ?? 3600)
            Secrets.shared.set(String(expiry), for: .gmailAccessTokenExpiry)
        }
        if let refresh = token.refreshToken {
            Secrets.shared.set(refresh, for: .gmailRefreshToken)
        }
    }

    func signOut() {
        Secrets.shared.set(nil, for: .gmailAccessToken)
        Secrets.shared.set(nil, for: .gmailRefreshToken)
        Secrets.shared.set(nil, for: .gmailAccessTokenExpiry)
        isSignedIn = false
        accountEmail = nil
    }

    // MARK: - PKCE helpers

    private static func randomCodeVerifier() -> String {
        var bytes = [UInt8](repeating: 0, count: 32)
        _ = SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes)
        return Data(bytes).base64URLEncodedString()
    }

    private static func codeChallenge(for verifier: String) -> String {
        let hash = SHA256.hash(data: Data(verifier.utf8))
        return Data(hash).base64URLEncodedString()
    }

    /// Extracts the email claim from a JWT id_token without verifying it
    /// (verification isn't needed — the token came directly from Google over TLS).
    private static func email(fromIDToken idToken: String) -> String? {
        let parts = idToken.split(separator: ".")
        guard parts.count >= 2 else { return nil }
        var payload = String(parts[1])
        payload = payload.replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        while payload.count % 4 != 0 { payload += "=" }
        guard let data = Data(base64Encoded: payload),
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        return json["email"] as? String
    }
}

// MARK: - Presentation anchor

extension GmailAuthService: ASWebAuthenticationPresentationContextProviding {
    func presentationAnchor(for session: ASWebAuthenticationSession) -> ASPresentationAnchor {
        NSApplication.shared.keyWindow ?? ASPresentationAnchor()
    }
}

// MARK: - Token model

private struct TokenResponse: Decodable {
    let accessToken: String?
    let refreshToken: String?
    let expiresIn: Int?
    let idToken: String?

    enum CodingKeys: String, CodingKey {
        case accessToken = "access_token"
        case refreshToken = "refresh_token"
        case expiresIn = "expires_in"
        case idToken = "id_token"
    }
}

extension Data {
    func base64URLEncodedString() -> String {
        base64EncodedString()
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }
}

extension CharacterSet {
    static let urlQueryValueAllowed: CharacterSet = {
        var set = CharacterSet.alphanumerics
        set.insert(charactersIn: "-._~")
        return set
    }()
}
