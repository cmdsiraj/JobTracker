# JobTracker

A native macOS app that turns your Gmail into an automatically maintained job-application tracker. It reads your job-related email (confirmations, assessments, recruiter calls, interviews, offers, rejections — and your own outreach), classifies each message with an LLM, groups everything into per-application timelines, and gives you a Kanban pipeline plus an analytics dashboard.

Built with SwiftUI + SwiftData. No server, no shared database: all data lives on your Mac (or your own iCloud), and the only thing that ever leaves the machine is email text sent to the LLM provider you configure.

![Platform](https://img.shields.io/badge/platform-macOS%2015%2B-blue)
![Swift](https://img.shields.io/badge/Swift-5-orange)
![UI](https://img.shields.io/badge/UI-SwiftUI-purple)

> **Windows user?** See [windows/README.md](windows/README.md) for the WPF port (`windows-port` branch) — same
> Gmail-sync/LLM-classification engine, ported line-for-line, with a native WPF UI and DPAPI secrets in place of
> Keychain. No iCloud sync equivalent; storage is local-only there.

## Features

### Ingestion & intelligence
- **Gmail sync** via OAuth 2.0 (PKCE, read-only scope) — timestamp-based: every launch fetches only mail newer than the last successful sync (inbox *and* sent), independent of read/unread state
- **Bulk import** of Google Takeout `.mbox` archives (streaming parser, handles multi-GB files) with a scope picker (current cycle / last 12 months / everything) before any LLM calls
- **LLM classification** in batches of 8 emails per request, with a heuristic prefilter (ATS senders, application phrases) so newsletters and noise never cost an API call
- **Multi-provider**: NVIDIA NIM, OpenAI, OpenRouter, Groq, or any custom OpenAI-compatible endpoint — each provider keeps its own key in the Keychain; a built-in throttle stays under free-tier rate limits (40 req/min)
- **Smart application matching**: Gmail thread → normalized company + token-similar role → time proximity → known recruiter address. Same-day applications to different roles at one company stay separate; ambiguous matches go to a Needs Review tray instead of being guessed
- **Duplicate merging** runs after every sync (and on demand) — role-similarity aware, never collapses distinct roles
- **Outreach tracking**: emails *you* send to companies/recruiters are detected and tracked as their own pipeline stage

### Tracking & UI
- **Kanban pipeline** — Outreach → Applied → Assessment → Recruiter Call → Interview → Final Round → Offer / Rejected; drag cards between columns (every move is logged on the timeline)
- **Interactive timeline scrubber** — a dual-handle range slider with a per-month activity histogram, on both the Dashboard and the pipeline; defaults to the last 5 months
- **Analytics dashboard** (Swift Charts): totals, weekly/monthly/yearly counts, daily & weekly trends, pipeline funnel, status donut, GitHub-style activity heatmap, cycle comparison — all filtered by the scrubber and recruiting-cycle chips
- **Per-application communication log**: every email (in/out), manual note, and status change on one timeline, with Open-in-Gmail links
- **Detailed filters**: status, company, recruiting cycle (e.g. "Summer 2026" vs "New Grad 2026"), source, plus full-text search
- **Leads**: manually save job postings and people to reach out to; convert them into applications later
- **Manual entry**: add applications or log updates ("recruiter said process is paused") for anything that didn't arrive by email
- **Instant persistence** — no Save buttons anywhere
- **Live activity log** (⇧⌘L) showing exactly what ingestion/sync is doing
- Optional **menu-bar watcher** for near-real-time processing (off by default to save battery)

### Privacy
- Data stays in a local SwiftData store (or your personal iCloud, chosen at onboarding)
- Secrets live only in the macOS Keychain
- The email prefilter minimizes how much content is sent to the LLM provider

## Requirements

| Requirement | Notes |
|---|---|
| macOS 15+ | Apple Silicon or Intel |
| Xcode 16+ | to build from source |
| Google Cloud OAuth client (iOS type) | free — enables Gmail sign-in; you enter your own client ID **in the app during onboarding** (no shared API identity: create a project, enable the Gmail API, add yourself as a test user on the OAuth consent screen) |
| LLM API key | e.g. NVIDIA NIM (free tier at build.nvidia.com), OpenAI, OpenRouter, Groq, or any OpenAI-compatible endpoint |

## Getting started

### From the release DMG
1. Download `JobTracker-x.y.dmg` from [Releases](../../releases), open it, drag JobTracker to Applications
2. First launch walks you through: storage choice (iCloud / local) → Gmail connect → API key (validated live) → initial import
3. For historical data, export your mail via [Google Takeout](https://takeout.google.com) (Mail → .mbox) and use **Import Mail Archive…**

> Note: the app is signed for personal/local use. On a Mac other than the build machine, right-click → Open on first launch.

### From source
```bash
git clone https://github.com/iamsiddhu3007/JobTracker.git
open JobTracker/JobTracker.xcodeproj
```
1. Target → Signing & Capabilities → App Sandbox → enable **Outgoing Connections (Client)**
2. Build & run (⌘R) — onboarding asks for your own Google OAuth client ID and LLM key
3. Tests: ⌘U (Swift Testing, 27 unit + UI tests)

## Architecture

```
Views (SwiftUI)          Dashboard · Kanban · Detail/Timeline · Leads · Review · Onboarding
Domain services          SyncPipeline (staged, cancellable) · EmailClassifier (batched)
                         ApplicationMatcher (multi-signal) · DuplicateMerger · CycleDetector
Infrastructure           GmailAPIClient (OAuth PKCE) · NIMClient (+ RequestThrottle)
                         MboxParser (streaming MIME) · Secrets (Keychain) · StoreManager (SwiftData)
```

- **Sync** is launch-triggered with a timestamp watermark — no background daemon, minimal battery
- **Matching** uses in-memory session indexes (O(1) per email) and flags anything below 90% confidence for human review; the user can always merge, split, or detach
- Models are CloudKit-compatible for the iCloud storage option and portable to iOS

## License

MIT — see [LICENSE](LICENSE).
