# JobTracker for Windows

A WPF port of the macOS JobTracker app: turns your Gmail into an automatically
maintained job-application tracker. Reads job-related email, classifies it
with an LLM, groups it into per-application timelines, and gives you a
Kanban pipeline plus an analytics dashboard.

Built with WPF (.NET, C#) + EF Core/SQLite. No server, no shared database:
all data lives locally on this PC, and the only thing that ever leaves the
machine is email text sent to the LLM provider you configure.

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![.NET](https://img.shields.io/badge/.NET-10-purple)
![UI](https://img.shields.io/badge/UI-WPF-orange)

## Differences from the macOS version

This is a from-scratch port, not a shared codebase — the underlying logic
(Gmail matching, LLM classification, duplicate merging, mbox parsing) is
ported line-for-line, but the platform integration is native Windows:

| | macOS | Windows |
|---|---|---|
| UI | SwiftUI | WPF |
| Storage | SwiftData (local or iCloud) | EF Core + SQLite (local only — no CloudKit equivalent) |
| Secrets | Keychain | DPAPI-encrypted file (`ProtectedData`, current-user scope) |
| Gmail OAuth | `ASWebAuthenticationSession` | System browser + loopback `http://127.0.0.1:{port}/` redirect |
| Background sync | Menu-bar `MenuBarExtra` | System tray icon |

**There is no iCloud/multi-device sync option** — Windows has no CloudKit
equivalent, so storage is always the local SQLite file under
`%LOCALAPPDATA%\JobTracker\`.

## Requirements

| Requirement | Notes |
|---|---|
| Windows 10 or 11 | |
| .NET 10 SDK | to build from source — `dotnet --version` |
| Google Cloud OAuth client (**Desktop app** type) | free — enables Gmail sign-in; different client *type* than macOS's "iOS" client, since this uses a loopback redirect instead of a custom URL scheme |
| LLM API key or AWS credentials | e.g. NVIDIA NIM (free tier at build.nvidia.com), OpenAI, OpenRouter, Groq, any OpenAI-compatible endpoint, or AWS Bedrock |

## Getting started

```bash
git clone -b windows-port https://github.com/cmdsiraj/JobTracker.git
cd JobTracker
dotnet build
dotnet run --project JobTracker
```

On first launch, onboarding walks you through: Gmail connect → LLM API key
→ initial import. For historical data, export your mail via
[Google Takeout](https://takeout.google.com) (Mail → .mbox) and use
**Import Mail Archive…**.

### Setting up your Google OAuth client

1. Go to [console.cloud.google.com](https://console.cloud.google.com) and
   create a project
2. Enable the **Gmail API**
3. OAuth consent screen → External → add your email as a *Test user*
4. Credentials → Create OAuth client ID → type **Desktop app**
5. Paste the Client ID into onboarding (or Settings → Account)

### Using AWS Bedrock instead of a hosted LLM API

Pick **AWS Bedrock** as the provider in Settings → Account, set the Region
and a Bedrock model ID (e.g. `anthropic.claude-3-5-sonnet-20241022-v2:0`).
Credentials come from either source, checked in this order:

1. **An Access Key ID + Secret Access Key** pasted into Settings — stored
   DPAPI-encrypted like the other providers' keys.
2. **A local AWS profile** — if both fields above are left blank, JobTracker
   falls back to whatever `AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY`
   environment variables or `%USERPROFILE%\.aws\credentials`/`\config`
   (AWS CLI or SSO login) already provide on this machine.

Your IAM principal needs `bedrock:InvokeModel` (the Converse API) on the
model you pick, and the model needs to be enabled for your account in that
region (Bedrock console → Model access).

### Running as a standalone app

`dotnet run` needs the .NET SDK. To get a normal double-clickable `.exe`
that doesn't:

```powershell
.\publish.ps1          # builds publish\JobTracker.exe (self-contained, ~85 MB)
.\make-shortcut.ps1     # adds a JobTracker shortcut to your Desktop
```

The published exe carries its own .NET runtime, so it runs on a PC without
the SDK (or any .NET runtime) installed. Config/data still live in
`%LOCALAPPDATA%\JobTracker\`, so a rebuild or republish never asks you to
re-enter your Gmail/LLM setup — see "Config persistence" below.

### Config persistence

Everything you enter in onboarding or Settings — the Google OAuth Client
ID, LLM/AWS keys, Gmail tokens, and all tracked applications — is stored
outside the build output, in `%LOCALAPPDATA%\JobTracker\`:

| File | Contents |
|---|---|
| `secrets.v2` | DPAPI-encrypted: API keys, AWS keys, Gmail tokens |
| `preferences.json` | Provider/model/region choice, onboarding state |
| `JobTrackerV2.db` | SQLite: applications, events, leads |

`dotnet build`, `dotnet run`, and re-publishing never touch this folder, so
rebuilding is always safe — you're never asked to reconnect Gmail or
re-enter a key. Only Settings → **Erase All Data** (or manually deleting
that folder) resets it.

### Running tests

```bash
dotnet test
```

60+ unit tests cover the pure-logic services (application matching,
duplicate merging, mbox/MIME parsing, cycle detection, heuristic
prefiltering) and the sync pipeline's upsert logic — the pieces that don't
require live Gmail/LLM credentials to verify.

## Architecture

```
Views (WPF)               MainShell · Kanban · Detail · Dashboard · Leads ·
                           Review · Onboarding · Settings · Tray popup
ViewModels                 One per screen (CommunityToolkit.Mvvm)
Domain services            SyncPipeline (staged, cancellable) · EmailClassifier (batched)
                           ApplicationMatcher (multi-signal) · DuplicateMerger · CycleDetector
Infrastructure              GmailAuthService (OAuth PKCE, loopback) · NimClient (+ RequestThrottle)
                           BedrockClient (AWS SigV4/Converse) · MboxParser (streaming MIME)
                           Secrets (DPAPI) · StoreManager (EF Core/SQLite)
```

- **Sync** is launch-triggered with a timestamp watermark — no background
  service, minimal resource use. An optional tray-icon watcher polls
  periodically if enabled in Settings.
- **Matching** flags anything below 90% confidence for human review in the
  Needs Review tray; the user can merge, split, or detach at any point.
- The algorithmic constants (0.55 classification confidence floor, 8-email
  batch size, 120-day match proximity window, Jaccard role similarity) are
  ported unchanged from the tuned macOS values.

## License

MIT — see [LICENSE](LICENSE).
