// Secret storage backed by the Windows Data Protection API (DPAPI): values
// are encrypted at rest to the current Windows user account and held in a
// single blob file. This mirrors the sandbox-file fallback path in the
// macOS Secrets.swift (owner-only file, no OS-credential-prompt UX) — DPAPI
// is the direct Windows analog of that approach, so there's no separate
// "keychain" concept to reproduce here.

using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace JobTracker.Services;

public enum SecretKey
{
    NvidiaApiKey,
    OpenAiKey,
    OpenRouterKey,
    GroqKey,
    CustomLlmKey,
    GmailAccessToken,
    GmailRefreshToken,
    GmailAccessTokenExpiry,
    AwsAccessKeyId,
    AwsSecretAccessKey,
}

public sealed class Secrets
{
    public static readonly Secrets Shared = new();

    private readonly Lock _lock = new();
    private Dictionary<SecretKey, string>? _cache;

    private Secrets() { }

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JobTracker", "secrets.v2");

    public string? Get(SecretKey key)
    {
        lock (_lock)
        {
            LoadIfNeededLocked();
            return _cache!.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;
        }
    }

    public void Set(string? value, SecretKey key)
    {
        lock (_lock)
        {
            LoadIfNeededLocked();
            if (!string.IsNullOrEmpty(value))
            {
                _cache![key] = value;
            }
            else
            {
                _cache!.Remove(key);
            }
            PersistLocked();
        }
    }

    /// Removes every secret this app has ever stored.
    public void WipeAll()
    {
        lock (_lock)
        {
            _cache = new Dictionary<SecretKey, string>();
            try { File.Delete(FilePath); } catch { /* best effort */ }
        }
    }

    private void LoadIfNeededLocked()
    {
        if (_cache is not null) return;

        try
        {
            if (File.Exists(FilePath))
            {
                var encrypted = File.ReadAllBytes(FilePath);
                var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                var blob = JsonSerializer.Deserialize<Dictionary<SecretKey, string>>(plain);
                _cache = blob ?? new Dictionary<SecretKey, string>();
                return;
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"[Secrets] Could not read secret store: {ex.Message}");
        }
        _cache = new Dictionary<SecretKey, string>();
    }

    private void PersistLocked()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            var plain = JsonSerializer.SerializeToUtf8Bytes(_cache);
            var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FilePath, encrypted);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"[Secrets] Secret store write FAILED: {ex.Message}");
            ActivityLog.Shared.Error($"Could not save credentials ({ex.Message}). They may be lost when the app quits.");
        }
    }
}
