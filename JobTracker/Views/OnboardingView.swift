//
//  OnboardingView.swift
//  JobTracker
//
//  Multi-step first-run wizard: welcome, storage choice, Gmail connection,
//  NVIDIA API key, and the initial-import guide. Finishing marks
//  onboardingDone, which flips RootView over to the main shell.
//

import SwiftUI
import UniformTypeIdentifiers

struct OnboardingFlow: View {
    @Environment(AppState.self) private var appState

    private enum Step: Int, CaseIterable {
        case welcome, storage, gmail, apiKey, importGuide
    }

    @State private var step: Step = .welcome
    @State private var goingForward = true

    // Gmail step
    @State private var signingIn = false
    @State private var signInError: String?

    // API key step
    @State private var nvidiaKey = ""
    @State private var keyState: KeyState = .idle
    private enum KeyState: Equatable { case idle, validating, valid, invalid }

    // Import step
    @State private var showingImporter = false

    var body: some View {
        VStack(spacing: 0) {
            ZStack {
                stepContent
                    .id(step)
                    .transition(.asymmetric(
                        insertion: .move(edge: goingForward ? .trailing : .leading)
                            .combined(with: .opacity),
                        removal: .move(edge: goingForward ? .leading : .trailing)
                            .combined(with: .opacity)
                    ))
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .padding(.horizontal, 48)

            controls
                .padding(24)
        }
        .background(.background)
        .fileImporter(isPresented: $showingImporter,
                      allowedContentTypes: importTypes) { result in
            if case .success(let url) = result {
                finish()
                appState.pipeline.importMbox(url: url)
            }
        }
    }

    // MARK: Step routing

    @ViewBuilder
    private var stepContent: some View {
        switch step {
        case .welcome:     welcomeStep
        case .storage:     storageStep
        case .gmail:       gmailStep
        case .apiKey:      apiKeyStep
        case .importGuide: importStep
        }
    }

    // MARK: Steps

    private var welcomeStep: some View {
        VStack(spacing: 20) {
            Image(systemName: "briefcase.fill")
                .font(.system(size: 64))
                .foregroundStyle(.tint)
                .symbolEffect(.bounce, options: .nonRepeating)
                .padding(.bottom, 4)
            Text("Welcome to JobTracker")
                .font(.largeTitle.bold())
            Text("Your job search, on autopilot. JobTracker reads your job-related email, classifies it with AI, and keeps every application's status, timeline, and stats up to date — automatically.")
                .font(.title3)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .frame(maxWidth: 460)
            HStack(spacing: 20) {
                featureBadge("envelope.badge", "Gmail-powered")
                featureBadge("brain", "AI classification")
                featureBadge("chart.bar.xaxis", "Live analytics")
                featureBadge("lock.shield", "Private by design")
            }
            .padding(.top, 8)
        }
    }

    private func featureBadge(_ icon: String, _ title: String) -> some View {
        VStack(spacing: 6) {
            Image(systemName: icon)
                .font(.title2)
                .foregroundStyle(.tint)
            Text(title).font(.caption).foregroundStyle(.secondary)
        }
        .frame(width: 100)
    }

    private var storageStep: some View {
        VStack(spacing: 20) {
            stepHeader("Where should your data live?",
                       subtitle: "Both options keep your data completely private. You can start over anytime from Settings.")

            HStack(spacing: 16) {
                storageCard(
                    choice: .iCloud,
                    icon: "icloud.fill",
                    title: "iCloud",
                    tagline: "Recommended",
                    points: ["Synced across your devices",
                             "Private to your Apple Account",
                             "Automatic backup"]
                )
                storageCard(
                    choice: .local,
                    icon: "desktopcomputer",
                    title: "This Mac Only",
                    tagline: "Most private",
                    points: ["Never leaves this machine",
                             "No iCloud account needed",
                             "You manage backups"]
                )
            }
            .frame(maxWidth: 620)
        }
    }

    private func storageCard(choice: StorageChoice, icon: String, title: String,
                             tagline: String, points: [String]) -> some View {
        let isSelected = appState.prefs.storageChoice == choice
        return Button {
            withAnimation(.spring(duration: 0.3)) {
                appState.prefs.storageChoice = choice
            }
        } label: {
            VStack(alignment: .leading, spacing: 12) {
                HStack {
                    Image(systemName: icon)
                        .font(.title)
                        .foregroundStyle(.tint)
                    Spacer()
                    Image(systemName: isSelected ? "checkmark.circle.fill" : "circle")
                        .font(.title3)
                        .foregroundStyle(isSelected ? Color.accentColor : Color.secondary)
                        .contentTransition(.symbolEffect(.replace))
                }
                VStack(alignment: .leading, spacing: 2) {
                    Text(title).font(.headline)
                    Text(tagline)
                        .font(.caption.weight(.medium))
                        .foregroundStyle(.tint)
                }
                VStack(alignment: .leading, spacing: 5) {
                    ForEach(points, id: \.self) { point in
                        Label(point, systemImage: "checkmark")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14))
            .overlay(
                RoundedRectangle(cornerRadius: 14)
                    .strokeBorder(isSelected ? Color.accentColor : Color.clear,
                                  lineWidth: 2)
            )
        }
        .buttonStyle(.plain)
    }

    private var gmailStep: some View {
        VStack(spacing: 20) {
            stepHeader("Connect Gmail",
                       subtitle: "JobTracker asks for read-only access and only ever looks for job-related mail. Your credentials stay between you and Google.")

            if appState.auth.isSignedIn {
                VStack(spacing: 10) {
                    Image(systemName: "checkmark.circle.fill")
                        .font(.system(size: 44))
                        .foregroundStyle(.green)
                        .symbolEffect(.bounce, options: .nonRepeating)
                    Text("Connected")
                        .font(.headline)
                    if let email = appState.auth.accountEmail {
                        Text(email)
                            .font(.callout)
                            .foregroundStyle(.secondary)
                    }
                }
                .padding(24)
                .frame(maxWidth: 380)
                .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14))
            } else {
                // Bring-your-own OAuth client: this open-source app never
                // ships a shared Google API identity — each user creates a
                // free client in their own Google Cloud project.
                clientIDBox

                Button {
                    Task { await connectGmail() }
                } label: {
                    Label(signingIn ? "Connecting…" : "Connect Gmail",
                          systemImage: "envelope.badge")
                        .frame(minWidth: 180)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .disabled(signingIn || !appState.isGoogleConfigured)

                if signingIn { ProgressView().controlSize(.small) }
            }

            if let signInError {
                Label(signInError, systemImage: "exclamationmark.triangle.fill")
                    .font(.callout)
                    .foregroundStyle(.orange)
                    .frame(maxWidth: 420)
            }
        }
    }

    /// One-time Google OAuth client setup: instructions + client-ID field.
    private var clientIDBox: some View {
        @Bindable var prefs = appState.prefs
        return VStack(alignment: .leading, spacing: 10) {
            Label("Your Google OAuth Client ID", systemImage: "key.horizontal")
                .font(.headline)

            Text("""
            JobTracker uses your own (free) Google API access — nothing is shared:
            1. Go to console.cloud.google.com and create a project
            2. Enable the **Gmail API**
            3. OAuth consent screen → External → add your email as a *Test user*
            4. Credentials → Create OAuth client ID → type **iOS** → any bundle id
            5. Paste the Client ID below
            """)
            .font(.caption)
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)

            TextField("", text: $prefs.googleClientID,
                      prompt: Text("1234567890-abc….apps.googleusercontent.com"))
                .textFieldStyle(.roundedBorder)
                .font(.callout.monospaced())
                .autocorrectionDisabled()

            if !prefs.googleClientID.isEmpty && !appState.isGoogleConfigured {
                Label("A client ID ends in .apps.googleusercontent.com",
                      systemImage: "exclamationmark.circle")
                    .font(.caption)
                    .foregroundStyle(.orange)
            }

            Link("Open Google Cloud Console…",
                 destination: URL(string: "https://console.cloud.google.com/apis/credentials")!)
                .font(.caption)
        }
        .padding(16)
        .frame(maxWidth: 460)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14))
    }

    private var apiKeyStep: some View {
        VStack(spacing: 20) {
            stepHeader("Add your NVIDIA API key",
                       subtitle: "The AI that reads and classifies your email runs on NVIDIA's free API. Grab a key at build.nvidia.com — it takes about a minute.")

            VStack(spacing: 12) {
                HStack(spacing: 8) {
                    SecureField("nvapi-…", text: $nvidiaKey)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 280)
                        .onSubmit { Task { await validateKey() } }

                    Button {
                        Task { await validateKey() }
                    } label: {
                        switch keyState {
                        case .validating:
                            ProgressView().controlSize(.small)
                        default:
                            Text("Validate & Save")
                        }
                    }
                    .disabled(nvidiaKey.trimmingCharacters(in: .whitespaces).isEmpty
                              || keyState == .validating)
                }

                switch keyState {
                case .valid:
                    Label("Key works — you're all set.", systemImage: "checkmark.circle.fill")
                        .font(.callout)
                        .foregroundStyle(.green)
                        .transition(.scale.combined(with: .opacity))
                case .invalid:
                    Label("That key didn't work. Double-check it and try again.",
                          systemImage: "xmark.circle.fill")
                        .font(.callout)
                        .foregroundStyle(.red)
                        .transition(.scale.combined(with: .opacity))
                case .idle where appState.hasNVIDIAKey:
                    Label("A key is already saved. You can replace it above.",
                          systemImage: "key.fill")
                        .font(.callout)
                        .foregroundStyle(.secondary)
                default:
                    EmptyView()
                }
            }
            .padding(24)
            .frame(maxWidth: 440)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14))

            Link(destination: URL(string: "https://build.nvidia.com")!) {
                Label("Get a free key at build.nvidia.com", systemImage: "arrow.up.forward.square")
                    .font(.callout)
            }

            Text("Your key is stored only in the macOS Keychain.")
                .font(.footnote)
                .foregroundStyle(.tertiary)
        }
        .animation(.spring(duration: 0.3), value: keyState)
    }

    private var importStep: some View {
        VStack(spacing: 20) {
            stepHeader("One last thing: your history",
                       subtitle: "Choose how much of your existing email to bring in.")

            VStack(alignment: .leading, spacing: 14) {
                importPoint("square.and.arrow.down",
                            "Import a Google Takeout .mbox archive to capture your full application history — parsing takes about a minute per gigabyte.")
                importPoint("brain",
                            "AI classification runs at roughly 8 emails per second's worth of requests; a typical archive finishes in a few minutes with live progress.")
                importPoint("clock.arrow.circlepath",
                            "After that, every launch only processes email that arrived since your last sync — quick and quiet.")
            }
            .padding(20)
            .frame(maxWidth: 520)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14))

            HStack(spacing: 12) {
                Button {
                    showingImporter = true
                } label: {
                    Label("Import Archive…", systemImage: "square.and.arrow.down")
                        .frame(minWidth: 160)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)

                Button {
                    finish()
                } label: {
                    Text("Skip — sync from now on")
                        .frame(minWidth: 160)
                }
                .controlSize(.large)
            }
        }
    }

    private func importPoint(_ icon: String, _ text: String) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: icon)
                .font(.title3)
                .foregroundStyle(.tint)
                .frame(width: 26)
            Text(text)
                .font(.callout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    private func stepHeader(_ title: String, subtitle: String) -> some View {
        VStack(spacing: 8) {
            Text(title).font(.largeTitle.bold())
            Text(subtitle)
                .font(.callout)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .frame(maxWidth: 480)
        }
    }

    // MARK: Controls

    private var controls: some View {
        HStack {
            if step != .welcome {
                Button("Back") { go(to: previousStep) }
                    .controlSize(.large)
            }

            Spacer()

            // Step dots
            HStack(spacing: 7) {
                ForEach(Step.allCases, id: \.rawValue) { s in
                    Circle()
                        .fill(s == step ? Color.accentColor : Color.secondary.opacity(0.3))
                        .frame(width: 7, height: 7)
                        .scaleEffect(s == step ? 1.25 : 1)
                }
            }
            .animation(.spring(duration: 0.3), value: step)

            Spacer()

            if step != .importGuide {
                Button(continueTitle) { go(to: nextStep) }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.large)
                    .disabled(!canContinue)
                    .keyboardShortcut(.defaultAction)
            }
        }
    }

    private var continueTitle: String {
        switch step {
        case .gmail where !appState.auth.isSignedIn:  return "Skip for Now"
        case .apiKey where keyState != .valid && !appState.hasNVIDIAKey:
            return "Skip for Now"
        default: return "Continue"
        }
    }

    private var canContinue: Bool {
        switch step {
        case .storage: return appState.prefs.storageChoice != nil
        default:       return true
        }
    }

    private var nextStep: Step {
        Step(rawValue: step.rawValue + 1) ?? .importGuide
    }

    private var previousStep: Step {
        Step(rawValue: step.rawValue - 1) ?? .welcome
    }

    private func go(to newStep: Step) {
        goingForward = newStep.rawValue > step.rawValue
        withAnimation(.spring(duration: 0.45)) { step = newStep }
    }

    // MARK: Actions

    private func connectGmail() async {
        signingIn = true
        signInError = nil
        defer { signingIn = false }
        do {
            try await appState.auth.signIn()
        } catch {
            signInError = error.localizedDescription
        }
    }

    private func validateKey() async {
        let trimmed = nvidiaKey.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return }
        keyState = .validating
        appState.saveNVIDIAKey(trimmed)
        let ok = await appState.validateNVIDIAKey()
        withAnimation(.spring(duration: 0.3)) {
            keyState = ok ? .valid : .invalid
        }
        if ok { nvidiaKey = "" }
    }

    private func finish() {
        appState.prefs.onboardingDone = true
    }

    private var importTypes: [UTType] {
        var types: [UTType] = [.data]
        if let mbox = UTType(filenameExtension: "mbox") { types.insert(mbox, at: 0) }
        return types
    }
}
