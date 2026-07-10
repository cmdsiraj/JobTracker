//
//  JobTrackerTests.swift
//  JobTrackerTests
//
//  Created by Mohana Siddhartha Chivukula on 7/1/26.
//

import Testing
import Foundation
@testable import JobTracker

struct HeuristicPrefilterTests {

    @Test func detectsATSSender() {
        #expect(HeuristicPrefilter.isLikelyJobRelated(
            sender: "no-reply@greenhouse.io",
            subject: "Update",
            body: "Hello"))
    }

    @Test func detectsKeywordSubject() {
        #expect(HeuristicPrefilter.isLikelyJobRelated(
            sender: "careers@acme.com",
            subject: "Thank you for applying to Acme",
            body: ""))
    }

    @Test func ignoresUnrelatedMail() {
        #expect(!HeuristicPrefilter.isLikelyJobRelated(
            sender: "friend@example.com",
            subject: "Dinner tonight?",
            body: "Want to grab food later?"))
    }
}

struct ClassifierParsingTests {

    @Test func parsesBatchArray() throws {
        let json = """
        [{"index": 0, "isJobRelated": true, "company": "Acme", "role": "iOS Engineer",
          "status": "interview", "location": "Remote", "source": "Greenhouse",
          "nextAction": "Reply with availability", "cycle": "Summer 2026", "confidence": 0.9},
         {"index": 1, "isJobRelated": false, "company": "", "role": "",
          "status": "unknown", "confidence": 0.8}]
        """
        let results = try EmailClassifier.parseArray(json)
        #expect(results.count == 2)
        #expect(results[0].isJobRelated)
        #expect(results[0].company == "Acme")
        #expect(results[0].applicationStatus == .interview)
        #expect(results[0].cycle == "Summer 2026")
        #expect(!results[1].isJobRelated)
        #expect(results[1].applicationStatus == nil)
    }

    @Test func parsesSingleObjectFallbackWithSurroundingText() throws {
        let messy = """
        Here is the result:
        {"index": 0, "isJobRelated": false, "company": "", "role": "", "status": "unknown", "confidence": 0.8}
        Hope that helps!
        """
        let results = try EmailClassifier.parseArray(messy)
        #expect(results.count == 1)
        #expect(!results[0].isJobRelated)
        #expect(results[0].applicationStatus == nil)
    }

    @Test func mapsLLMStatusStrings() {
        #expect(ApplicationStatus.from(llmString: "recruiter call") == .recruiterCall)
        #expect(ApplicationStatus.from(llmString: "Online Assessment") == .assessment)
        #expect(ApplicationStatus.from(llmString: "outreach") == .outreach)
        #expect(ApplicationStatus.from(llmString: "final round") == .finalRound)
        #expect(ApplicationStatus.from(llmString: "gibberish") == nil)
    }
}

struct RoleSimilarityTests {

    @Test func roleVariantsMatch() {
        // The exact failure mode observed in real ingestion.
        let a = ApplicationMatcher.roleTokens("Software Engineer Intern")
        let b = ApplicationMatcher.roleTokens("Software Engineering Intern - Summer 2026")
        #expect(ApplicationMatcher.similarity(a, b) >= 0.6)
    }

    @Test func differentRolesDoNotMatch() {
        let a = ApplicationMatcher.roleTokens("Data Scientist Intern")
        let b = ApplicationMatcher.roleTokens("Software Engineer Intern")
        #expect(ApplicationMatcher.similarity(a, b) < 0.6)
    }

    @Test func internVsFullTimeStayDistinct() {
        let a = ApplicationMatcher.roleTokens("Software Engineer Intern")
        let b = ApplicationMatcher.roleTokens("Software Engineer New Grad")
        #expect(ApplicationMatcher.similarity(a, b) < 0.6)
    }

    @Test func derivesCompanyFromSender() {
        #expect(ApplicationMatcher.companyFromSender("TikTok <noreply@careers.tiktok.com>") == "Tiktok")
        #expect(ApplicationMatcher.companyFromSender("no-reply@greenhouse.io") == nil)
        #expect(ApplicationMatcher.companyFromSender("friend@gmail.com") == nil)
    }
}

@MainActor
struct SameCompanyMultiRoleTests {

    /// Applying to multiple roles at one company on the SAME DAY must yield
    /// separate applications — and a role-less follow-up must not be force-
    /// merged when the target is ambiguous.
    @Test func sameDayMultipleRolesStaySeparate() {
        let now = Date()
        let swe = JobApplication(company: "Amazon", roleTitle: "Software Engineer Intern", status: .applied)
        swe.lastEmailDate = now
        swe.lastUpdated = now
        let ds = JobApplication(company: "Amazon", roleTitle: "Data Scientist Intern", status: .applied)
        ds.lastEmailDate = now
        ds.lastUpdated = now
        let matcher = ApplicationMatcher(existing: [swe, ds])

        // A distinct third role creates a NEW application (no match).
        var newRole = ClassificationResult.notJobRelated(index: 0)
        newRole.company = "Amazon"
        newRole.role = "Product Manager Intern"
        let message = FetchedMessage(id: "1", threadId: "t-new", sender: "no-reply@amazon.jobs",
                                     subject: "Thanks for applying", snippet: "", body: "",
                                     isOutgoing: false, date: now)
        #expect(matcher.match(message: message, result: newRole) == nil)

        // A role-less update with TWO candidates attaches only as a
        // low-confidence review match — never silently.
        var roleless = ClassificationResult.notJobRelated(index: 1)
        roleless.company = "Amazon"
        roleless.role = ""
        let update = FetchedMessage(id: "2", threadId: "t-upd", sender: "no-reply@amazon.jobs",
                                    subject: "Update on your application", snippet: "", body: "",
                                    isOutgoing: false, date: now)
        let match = matcher.match(message: update, result: roleless)
        #expect(match != nil)
        #expect(match!.needsReview)
    }

    @Test func sameThreadAlwaysWinsEvenAfterAssociation() {
        let app = JobApplication(company: "Acme", roleTitle: "iOS Engineer", status: .applied)
        let matcher = ApplicationMatcher(existing: [app])
        matcher.associate(threadId: "brand-new-thread", with: app)

        var result = ClassificationResult.notJobRelated(index: 0)
        result.company = "Totally Different Co"
        let message = FetchedMessage(id: "9", threadId: "brand-new-thread", sender: "x@y.com",
                                     subject: "Re: interview", snippet: "", body: "",
                                     isOutgoing: false, date: Date())
        #expect(matcher.match(message: message, result: result)?.confidence == 1.0)
    }
}

struct MatcherTests {

    @MainActor @Test func normalizesCompanies() {
        #expect(JobApplication.normalizeCompany("Google LLC") == "google")
        #expect(JobApplication.normalizeCompany("Acme, Inc.") == "acme")
        #expect(JobApplication.normalizeCompany("TikTok") == JobApplication.normalizeCompany("tiktok"))
    }

    @MainActor @Test func matchesByThreadThenCompanyRole() {
        let app = JobApplication(company: "Acme Inc", roleTitle: "iOS Engineer", status: .applied)
        app.threadIds = ["deadbeef"]
        app.lastUpdated = Date()
        let matcher = ApplicationMatcher(existing: [app])

        let sameThread = FetchedMessage(id: "1", threadId: "deadbeef", sender: "a@acme.com",
                                        subject: "Update", snippet: "", body: "", isOutgoing: false, date: Date())
        let byThread = matcher.match(message: sameThread,
                                     result: .notJobRelated(index: 0))
        #expect(byThread?.confidence == 1.0)

        let sameCompanyRole = FetchedMessage(id: "2", threadId: "other", sender: "b@acme.com",
                                             subject: "Interview", snippet: "", body: "", isOutgoing: false, date: Date())
        var result = ClassificationResult.notJobRelated(index: 1)
        result.company = "Acme"
        result.role = "iOS Engineer"
        let byCompany = matcher.match(message: sameCompanyRole, result: result)
        #expect(byCompany?.confidence == 0.9)
        #expect(byCompany?.needsReview == false)
    }
}

struct CycleDetectorTests {

    @Test func detectsSeasonYear() {
        #expect(CycleDetector.detect(in: "Software Engineering Intern - Summer 2026") == "Summer 2026")
        #expect(CycleDetector.detect(in: "FALL 2025 internship program") == "Fall 2025")
        #expect(CycleDetector.detect(in: "2026 summer analyst") == "Summer 2026")
    }

    @Test func detectsNewGrad() {
        #expect(CycleDetector.detect(in: "New Grad 2026 Software Engineer") == "New Grad 2026")
    }

    @Test func returnsNilWithoutCycle() {
        #expect(CycleDetector.detect(in: "Your application to Acme") == nil)
    }

    @Test func sortKeyOrdersNewestFirst() {
        let cycles = ["Summer 2025", "Fall 2025", "Summer 2026"]
        let sorted = cycles.sorted { CycleDetector.sortKey($0) > CycleDetector.sortKey($1) }
        #expect(sorted == ["Summer 2026", "Fall 2025", "Summer 2025"])
    }
}

struct MboxParserTests {

    private func makeTempMbox(_ content: String) throws -> URL {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("test-\(UUID().uuidString).mbox")
        try content.write(to: url, atomically: true, encoding: .utf8)
        return url
    }

    private let sampleMbox = """
    From 1869546606810630443@xxx Wed Jul 01 20:43:02 +0000 2026
    X-GM-THRID: 1869546606810630443
    X-Gmail-Labels: Inbox,Opened
    Message-ID: <abc123@mail.acme.com>
    From: Acme Recruiting <no-reply@greenhouse.io>
    Subject: Thank you for applying to Acme - Summer 2026 Intern
    Date: Wed, 1 Jul 2026 13:43:02 -0700 (PDT)
    Content-Type: text/plain; charset=UTF-8

    We received your application for Software Engineering Intern.

    From 1869546606810630444@xxx Wed Jul 01 21:43:02 +0000 2026
    X-GM-THRID: 1869546606810630444
    X-Gmail-Labels: Spam
    Message-ID: <spam1@spam.com>
    From: spammer <x@spam.com>
    Subject: You won an interview prize job offer
    Date: Wed, 1 Jul 2026 14:43:02 -0700
    Content-Type: text/plain

    Claim your job offer interview prize now!

    From 1869546606810630445@xxx Wed Jul 01 22:43:02 +0000 2026
    X-GM-THRID: 1869546606810630445
    X-Gmail-Labels: Inbox
    Message-ID: <def456@newsletter.com>
    From: News Digest <digest@news.com>
    Subject: Your weekly cooking newsletter
    Date: Wed, 1 Jul 2026 15:43:02 -0700
    Content-Type: text/plain

    This week in recipes: pasta.

    """

    @Test func parsesAndFiltersMessages() throws {
        let url = try makeTempMbox(sampleMbox)
        defer { try? FileManager.default.removeItem(at: url) }

        let (messages, summary) = try MboxParser.collectJobCandidates(url: url)

        #expect(summary.totalMessages == 3)
        #expect(summary.skippedSpamTrash == 1)   // spam skipped despite keywords
        #expect(summary.candidates == 1)          // newsletter filtered out
        #expect(messages.count == 1)

        let message = try #require(messages.first)
        #expect(message.id == "abc123@mail.acme.com")
        #expect(message.threadId == String(UInt64(1869546606810630443), radix: 16))
        #expect(message.subject.contains("Summer 2026"))
        #expect(message.sender.contains("greenhouse.io"))
        #expect(message.body.contains("We received your application"))
    }

    @Test func decodesRFC2047Subject() {
        let decoded = MboxParser.decodeRFC2047("=?UTF-8?B?SGVsbG8gV29ybGQ=?=")
        #expect(decoded == "Hello World")

        let qEncoded = MboxParser.decodeRFC2047("=?utf-8?Q?Interview_Confirmed?=")
        #expect(qEncoded == "Interview Confirmed")
    }

    @Test func decodesQuotedPrintable() {
        let data = MboxParser.decodeQuotedPrintable("Caf=C3=A9 time")
        #expect(String(data: data ?? Data(), encoding: .utf8) == "Café time")
    }

    @Test func parsesRFC822Dates() {
        let date = MboxParser.parseDate("Wed, 1 Jul 2026 13:43:02 -0700 (PDT)")
        #expect(date != nil)
    }
}

struct ApplicationStatusTests {

    @Test func statusRoundTrips() {
        let app = JobApplication(company: "Acme", roleTitle: "Engineer", status: .offer)
        #expect(app.status == .offer)
        app.status = .rejected
        #expect(app.statusRaw == "rejected")
    }

    @Test func rankOrdersPipeline() {
        #expect(ApplicationStatus.applied.rank < ApplicationStatus.interview.rank)
        #expect(ApplicationStatus.interview.rank < ApplicationStatus.offer.rank)
    }
}
