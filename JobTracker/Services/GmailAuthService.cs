// Dependency-free Google OAuth 2.0 for a native Windows app using PKCE and
// the system browser + a loopback HTTP listener (Google's documented flow
// for installed/desktop apps — the Windows analog of macOS's
// ASWebAuthenticationSession). Tokens are persisted via Secrets (DPAPI) and
// refreshed on demand.
//
// IMPORTANT for users setting up their own OAuth client: this flow needs a
// "Desktop app" type client (not "iOS", which the macOS build uses) since
// it relies on the http://127.0.0.1:{port}/ loopback redirect.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JobTracker.Services;

public enum AuthErrorKind { NotConfigured, Cancelled, BadResponse, NoRefreshToken }

public sealed class AuthException(AuthErrorKind kind, string? detail = null)
    : Exception(Describe(kind, detail))
{
    public AuthErrorKind Kind { get; } = kind;

    private static string Describe(AuthErrorKind kind, string? detail) => kind switch
    {
        AuthErrorKind.NotConfigured => "Google client ID is not set. Add it in Settings.",
        AuthErrorKind.Cancelled => "Sign-in was cancelled.",
        AuthErrorKind.BadResponse => $"OAuth error: {detail}",
        AuthErrorKind.NoRefreshToken => "No refresh token available. Please sign in again.",
        _ => "OAuth error",
    };
}

public sealed partial class GmailAuthService : DispatcherObservableObject
{
    [ObservableProperty]
    private bool _isSignedIn = Secrets.Shared.Get(SecretKey.GmailRefreshToken) is not null;

    [ObservableProperty]
    private string? _accountEmail;

    private static readonly HttpClient Http = new();
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromMinutes(5);

    // MARK: - Sign in

    public async Task SignInAsync()
    {
        if (!AppConfig.IsGoogleConfigured) throw new AuthException(AuthErrorKind.NotConfigured);

        var verifier = RandomCodeVerifier();
        var challenge = CodeChallenge(verifier);
        var port = GetFreeLoopbackPort();
        var redirectUri = $"http://127.0.0.1:{port}/oauth2redirect/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        var authorizationUrl = BuildAuthorizationUrl(redirectUri, challenge);
        Process.Start(new ProcessStartInfo(authorizationUrl) { UseShellExecute = true });

        string code;
        try
        {
            code = await WaitForCallbackAsync(listener);
        }
        finally
        {
            listener.Stop();
        }

        await ExchangeCodeAsync(code, verifier, redirectUri);
        IsSignedIn = true;
    }

    private static int GetFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<string> WaitForCallbackAsync(HttpListener listener)
    {
        var contextTask = listener.GetContextAsync();
        var completed = await Task.WhenAny(contextTask, Task.Delay(CallbackTimeout));
        if (completed != contextTask)
        {
            throw new AuthException(AuthErrorKind.Cancelled, "Sign-in timed out.");
        }

        var context = await contextTask;
        var query = context.Request.QueryString;
        var code = query["code"];
        var error = query["error"];

        var html = error is null
            ? "<html><body>Signed in — you can close this window.</body></html>"
            : "<html><body>Sign-in failed. You can close this window.</body></html>";
        var buffer = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer);
        context.Response.OutputStream.Close();

        if (error is not null) throw new AuthException(AuthErrorKind.BadResponse, error);
        if (string.IsNullOrEmpty(code)) throw new AuthException(AuthErrorKind.BadResponse, "Missing authorization code");
        return code;
    }

    private static string BuildAuthorizationUrl(string redirectUri, string challenge)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = AppConfig.GoogleClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = AppConfig.GmailScope,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["access_type"] = "offline",
            ["prompt"] = "consent",
        };
        var qs = string.Join('&', query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{AppConfig.AuthorizationEndpoint}?{qs}";
    }

    // MARK: - Token exchange & refresh

    private async Task ExchangeCodeAsync(string code, string verifier, string redirectUri)
    {
        var token = await PostTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = AppConfig.GoogleClientId,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
        });
        Persist(token);
        if (token.IdToken is not null) AccountEmail = EmailFromIdToken(token.IdToken);
    }

    /// Returns a valid access token, refreshing if necessary.
    public async Task<string> ValidAccessTokenAsync()
    {
        var token = Secrets.Shared.Get(SecretKey.GmailAccessToken);
        var expiryString = Secrets.Shared.Get(SecretKey.GmailAccessTokenExpiry);
        if (token is not null && expiryString is not null &&
            double.TryParse(expiryString, NumberStyles.Float, CultureInfo.InvariantCulture, out var expiry) &&
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() < expiry - 60)
        {
            return token;
        }
        return await RefreshAsync();
    }

    private async Task<string> RefreshAsync()
    {
        var refreshToken = Secrets.Shared.Get(SecretKey.GmailRefreshToken)
            ?? throw new AuthException(AuthErrorKind.NoRefreshToken);
        var token = await PostTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = AppConfig.GoogleClientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        });
        Persist(token);
        return token.AccessToken ?? throw new AuthException(AuthErrorKind.BadResponse, "No access token");
    }

    private static async Task<TokenResponse> PostTokenAsync(Dictionary<string, string> parameters)
    {
        using var content = new FormUrlEncodedContent(parameters);
        using var response = await Http.PostAsync(AppConfig.TokenEndpoint, content);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new AuthException(AuthErrorKind.BadResponse, body);
        return JsonSerializer.Deserialize<TokenResponse>(body)
            ?? throw new AuthException(AuthErrorKind.BadResponse, "Empty response");
    }

    private static void Persist(TokenResponse token)
    {
        if (token.AccessToken is not null)
        {
            Secrets.Shared.Set(token.AccessToken, SecretKey.GmailAccessToken);
            var expiry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (token.ExpiresIn ?? 3600);
            Secrets.Shared.Set(expiry.ToString(CultureInfo.InvariantCulture), SecretKey.GmailAccessTokenExpiry);
        }
        if (token.RefreshToken is not null)
        {
            Secrets.Shared.Set(token.RefreshToken, SecretKey.GmailRefreshToken);
        }
    }

    public void SignOut()
    {
        Secrets.Shared.Set(null, SecretKey.GmailAccessToken);
        Secrets.Shared.Set(null, SecretKey.GmailRefreshToken);
        Secrets.Shared.Set(null, SecretKey.GmailAccessTokenExpiry);
        IsSignedIn = false;
        AccountEmail = null;
    }

    // MARK: - PKCE helpers

    private static string RandomCodeVerifier() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string CodeChallenge(string verifier) =>
        Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// Extracts the email claim from a JWT id_token without verifying it
    /// (verification isn't needed — the token came directly from Google over TLS).
    private static string? EmailFromIdToken(string idToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length < 2) return null;
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        try
        {
            var bytes = Convert.FromBase64String(payload);
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.TryGetProperty("email", out var email) ? email.GetString() : null;
        }
        catch (FormatException) { return null; }
        catch (JsonException) { return null; }
    }

    // MARK: - Token model

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int? ExpiresIn { get; set; }
        [JsonPropertyName("id_token")] public string? IdToken { get; set; }
    }
}
