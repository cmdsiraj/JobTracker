// App-level (not user-level) configuration: OAuth client identity, API
// endpoints, scopes, defaults. Nothing personal lives here — user secrets
// are in the DPAPI-protected store (Secrets.cs) and user data is in the
// local SQLite store (StoreManager.cs).

namespace JobTracker.Services;

public static class AppConfig
{
    // MARK: - Google OAuth (identifies the app, not the user)

    /// OAuth 2.0 Client ID (type: Desktop app) from the *user's own* Google
    /// Cloud project, entered during onboarding (Settings → Account to
    /// change). Deliberately NOT hardcoded: every user of this open-source
    /// app brings their own client so nobody shares anyone else's Google API
    /// quota.
    public static string GoogleClientId => Preferences.Shared.GoogleClientId;

    public const string ClientIdSuffix = ".apps.googleusercontent.com";

    public static bool IsGoogleConfigured =>
        GoogleClientId.EndsWith(ClientIdSuffix, StringComparison.Ordinal) &&
        GoogleClientId.Length > ClientIdSuffix.Length;

    public static readonly Uri AuthorizationEndpoint = new("https://accounts.google.com/o/oauth2/v2/auth");
    public static readonly Uri TokenEndpoint = new("https://oauth2.googleapis.com/token");
    public const string GmailScope = "https://www.googleapis.com/auth/gmail.readonly";

    // MARK: - Gmail API

    public static readonly Uri GmailBaseUrl = new("https://gmail.googleapis.com/gmail/v1/");

    // MARK: - Default LLM model (NVIDIA NIM)

    public const string DefaultModel = "nvidia/nemotron-3-ultra-550b-a55b";

    /// Emails classified per LLM request. Batching cuts request count and
    /// free-tier credit burn roughly by this factor.
    public const int ClassificationBatchSize = 8;

    // MARK: - Sync

    /// Safety overlap subtracted from the last-sync watermark so boundary
    /// emails are never missed (duplicates are filtered by message id).
    public static readonly TimeSpan SyncOverlap = TimeSpan.FromHours(1);

    /// Optional tray watcher poll interval.
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(120);

    // MARK: - Reset

    /// Bump to force a one-time wipe of data written by older architectures
    /// (store, preferences, AND secrets — a bump means a true fresh start).
    public const int ArchitectureVersion = 1;
}
