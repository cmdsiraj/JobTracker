//
//  MboxParser.swift
//  JobTracker
//
//  Streaming parser for Google Takeout mbox archives. Reads the file in
//  chunks (never loads the whole archive into memory), splits on mbox
//  "From " separators, decodes MIME (RFC 2047 headers, quoted-printable /
//  base64 bodies, multipart), and yields FetchedMessage values compatible
//  with the rest of the pipeline.
//
//  Takeout specifics used here:
//  - Each message starts with "From <decimal-gm-thrid>@xxx <date>"
//  - "X-GM-THRID" (decimal) converts to the hex threadId the Gmail API uses
//  - "X-Gmail-Labels" lets us skip Spam / Trash outright
//

import Foundation

enum MboxParserError: LocalizedError {
    case cannotOpen(String)

    var errorDescription: String? {
        switch self {
        case .cannotOpen(let path): return "Cannot open mbox file: \(path)"
        }
    }
}

nonisolated struct MboxParseSummary {
    var totalMessages = 0
    var skippedSpamTrash = 0
    var candidates = 0
}

/// `nonisolated`: parsing runs in a detached task off the main actor.
nonisolated enum MboxParser {

    /// Streams the mbox at `url`, returning only messages that pass the
    /// job-related prefilter (and are not Spam/Trash). `progress` receives
    /// a 0...1 fraction of file bytes consumed.
    static func collectJobCandidates(
        url: URL,
        progress: ((Double) -> Void)? = nil
    ) throws -> (messages: [FetchedMessage], summary: MboxParseSummary) {

        guard let handle = try? FileHandle(forReadingFrom: url) else {
            throw MboxParserError.cannotOpen(url.path)
        }
        defer { try? handle.close() }

        let fileSize = (try? FileManager.default.attributesOfItem(atPath: url.path)[.size] as? Int) ?? 0
        let chunkSize = 4 * 1024 * 1024
        let separator = Data("\nFrom ".utf8)

        var buffer = Data()
        var bytesConsumed = 0
        var summary = MboxParseSummary()
        var results: [FetchedMessage] = []

        func handleMessage(_ raw: Data) {
            summary.totalMessages += 1
            guard let message = parseMessage(raw) else { return }
            if message.labels.contains("Spam") || message.labels.contains("Trash") {
                summary.skippedSpamTrash += 1
                return
            }
            guard HeuristicPrefilter.isLikelyJobRelated(
                sender: message.fetched.sender,
                subject: message.fetched.subject,
                body: message.fetched.body,
                isOutgoing: message.fetched.isOutgoing
            ) else { return }
            summary.candidates += 1
            results.append(message.fetched)
        }

        while true {
            guard let chunk = try? handle.read(upToCount: chunkSize), !chunk.isEmpty else { break }
            buffer.append(chunk)
            bytesConsumed += chunk.count
            if fileSize > 0 { progress?(Double(bytesConsumed) / Double(fileSize)) }

            // Extract complete messages; keep the trailing partial one buffered.
            var searchStart = buffer.startIndex
            while let range = buffer.range(of: separator, in: searchStart..<buffer.endIndex) {
                // Validate this is a real mbox boundary: "From <digits>@xxx"
                guard isTakeoutBoundary(buffer, atSeparator: range) else {
                    searchStart = range.upperBound
                    continue
                }
                let messageData = buffer.subdata(in: buffer.startIndex..<range.lowerBound)
                if !messageData.isEmpty { handleMessage(messageData) }
                // +1 keeps the "From " (drops only the "\n").
                buffer.removeSubrange(buffer.startIndex..<(range.lowerBound + 1))
                searchStart = buffer.startIndex
            }
        }

        // Final message.
        if !buffer.isEmpty { handleMessage(buffer) }

        return (results, summary)
    }

    /// Checks that the bytes after "\nFrom " look like "<digits>@xxx ".
    private static func isTakeoutBoundary(_ data: Data, atSeparator range: Range<Data.Index>) -> Bool {
        var index = range.upperBound
        var sawDigit = false
        while index < data.endIndex, data[index] >= UInt8(ascii: "0"), data[index] <= UInt8(ascii: "9") {
            sawDigit = true
            index += 1
        }
        guard sawDigit, index < data.endIndex, data[index] == UInt8(ascii: "@") else { return false }
        return true
    }

    // MARK: - Single message

    private struct ParsedMessage {
        let fetched: FetchedMessage
        let labels: Set<String>
    }

    private static func parseMessage(_ raw: Data) -> ParsedMessage? {
        // Split headers / body at the first blank line.
        let crlfSep = Data("\r\n\r\n".utf8)
        let lfSep = Data("\n\n".utf8)
        var headerData = raw
        var bodyData = Data()
        if let r = raw.range(of: crlfSep) ?? raw.range(of: lfSep) {
            headerData = raw.subdata(in: raw.startIndex..<r.lowerBound)
            bodyData = raw.subdata(in: r.upperBound..<raw.endIndex)
        }

        guard let headerText = String(data: headerData, encoding: .utf8)
            ?? String(data: headerData, encoding: .isoLatin1) else { return nil }

        let headers = unfoldHeaders(headerText)
        func header(_ name: String) -> String { headers[name.lowercased()] ?? "" }

        let labels = Set(header("X-Gmail-Labels").components(separatedBy: ",")
            .map { $0.trimmingCharacters(in: .whitespaces) })

        let threadIdHex: String = {
            if let decimal = UInt64(header("X-GM-THRID")) { return String(decimal, radix: 16) }
            return ""
        }()

        let messageId = header("Message-ID")
            .trimmingCharacters(in: CharacterSet(charactersIn: "<> \t"))

        let subject = decodeRFC2047(header("Subject"))
        let sender = decodeRFC2047(header("From"))
        let date = parseDate(header("Date")) ?? Date.distantPast

        let body = extractBody(
            bodyData,
            contentType: header("Content-Type"),
            transferEncoding: header("Content-Transfer-Encoding"),
            depth: 0
        )
        let cleanBody = String(body.prefix(8000))

        let fetched = FetchedMessage(
            id: messageId.isEmpty ? "mbox-\(threadIdHex)-\(raw.count)" : messageId,
            threadId: threadIdHex,
            sender: sender,
            subject: subject,
            snippet: String(cleanBody.replacingOccurrences(of: "\n", with: " ").prefix(160)),
            body: cleanBody,
            isOutgoing: labels.contains("Sent"),
            date: date
        )
        return ParsedMessage(fetched: fetched, labels: labels)
    }

    // MARK: - Headers

    /// Unfolds continuation lines and returns a [lowercased-name: value] map
    /// (first occurrence wins).
    private static func unfoldHeaders(_ text: String) -> [String: String] {
        var result: [String: String] = [:]
        var currentName: String?
        var currentValue = ""

        func commit() {
            if let name = currentName, result[name] == nil {
                result[name] = currentValue.trimmingCharacters(in: .whitespaces)
            }
        }

        for line in text.replacingOccurrences(of: "\r\n", with: "\n").components(separatedBy: "\n") {
            if line.hasPrefix(" ") || line.hasPrefix("\t") {
                currentValue += " " + line.trimmingCharacters(in: .whitespaces)
            } else if let colon = line.firstIndex(of: ":") {
                commit()
                currentName = String(line[..<colon]).lowercased()
                currentValue = String(line[line.index(after: colon)...])
            }
        }
        commit()
        return result
    }

    /// Decodes RFC 2047 encoded words: =?charset?B|Q?data?=
    static func decodeRFC2047(_ value: String) -> String {
        guard value.contains("=?") else { return value }
        let pattern = "=\\?([^?]+)\\?([BbQq])\\?([^?]*)\\?="
        guard let regex = try? NSRegularExpression(pattern: pattern) else { return value }

        var result = value
        // Iterate until no encoded words remain (handles adjacent words).
        var iterations = 0
        while iterations < 20,
              let match = regex.firstMatch(in: result, range: NSRange(result.startIndex..., in: result)),
              let whole = Range(match.range, in: result),
              let charsetRange = Range(match.range(at: 1), in: result),
              let encRange = Range(match.range(at: 2), in: result),
              let dataRange = Range(match.range(at: 3), in: result) {
            iterations += 1
            let charset = String(result[charsetRange]).lowercased()
            let encoding = String(result[encRange]).uppercased()
            let payload = String(result[dataRange])

            let decodedData: Data?
            if encoding == "B" {
                decodedData = Data(base64Encoded: payload)
            } else {
                let qp = payload.replacingOccurrences(of: "_", with: " ")
                decodedData = decodeQuotedPrintable(qp)
            }

            let text: String
            if let data = decodedData {
                let enc: String.Encoding = charset.contains("8859") ? .isoLatin1 : .utf8
                text = String(data: data, encoding: enc)
                    ?? String(data: data, encoding: .isoLatin1) ?? ""
            } else {
                text = ""
            }
            result.replaceSubrange(whole, with: text)
        }
        return result
    }

    // MARK: - Date

    private static let dateFormats = [
        "EEE, d MMM yyyy HH:mm:ss Z",
        "d MMM yyyy HH:mm:ss Z",
        "EEE, d MMM yyyy HH:mm Z"
    ]

    static func parseDate(_ value: String) -> Date? {
        // Strip trailing comments like "(PDT)".
        var text = value
        if let paren = text.firstIndex(of: "(") {
            text = String(text[..<paren])
        }
        text = text.trimmingCharacters(in: .whitespaces)

        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        for format in dateFormats {
            formatter.dateFormat = format
            if let date = formatter.date(from: text) { return date }
        }
        return nil
    }

    // MARK: - Body / MIME

    private static func extractBody(_ data: Data,
                                    contentType: String,
                                    transferEncoding: String,
                                    depth: Int) -> String {
        guard depth < 5 else { return "" }
        let typeLower = contentType.lowercased()

        if typeLower.contains("multipart/"), let boundary = boundary(from: contentType) {
            let parts = splitMultipart(data, boundary: boundary)
            var htmlFallback = ""
            for part in parts {
                let (headers, partBody) = splitPart(part)
                let partType = headers["content-type"] ?? "text/plain"
                let partEnc = headers["content-transfer-encoding"] ?? ""
                let text = extractBody(partBody, contentType: partType,
                                       transferEncoding: partEnc, depth: depth + 1)
                if partType.lowercased().contains("text/plain"), !text.isEmpty {
                    return text
                }
                if htmlFallback.isEmpty, !text.isEmpty { htmlFallback = text }
            }
            return htmlFallback
        }

        // Leaf part: decode transfer encoding, then charset.
        let decoded: Data
        switch transferEncoding.lowercased().trimmingCharacters(in: .whitespaces) {
        case "base64":
            let compact = String(data: data, encoding: .ascii)?
                .components(separatedBy: .whitespacesAndNewlines).joined() ?? ""
            decoded = Data(base64Encoded: compact) ?? Data()
        case "quoted-printable":
            let text = String(data: data, encoding: .ascii) ?? ""
            decoded = decodeQuotedPrintable(text) ?? Data()
        default:
            decoded = data
        }

        var text = String(data: decoded, encoding: .utf8)
            ?? String(data: decoded, encoding: .isoLatin1) ?? ""

        if typeLower.contains("text/html") {
            text = stripHTML(text)
        }
        return text.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private static func boundary(from contentType: String) -> String? {
        // boundary="xyz" or boundary=xyz
        let pattern = "boundary=\"?([^\";]+)\"?"
        guard let regex = try? NSRegularExpression(pattern: pattern, options: .caseInsensitive),
              let match = regex.firstMatch(in: contentType, range: NSRange(contentType.startIndex..., in: contentType)),
              let range = Range(match.range(at: 1), in: contentType) else { return nil }
        return String(contentType[range])
    }

    private static func splitMultipart(_ data: Data, boundary: String) -> [Data] {
        let marker = Data("--\(boundary)".utf8)
        var parts: [Data] = []
        var cursor = data.startIndex
        var partStart: Data.Index?

        while let range = data.range(of: marker, in: cursor..<data.endIndex) {
            if let start = partStart {
                parts.append(data.subdata(in: start..<range.lowerBound))
            }
            partStart = range.upperBound
            cursor = range.upperBound
            if cursor >= data.endIndex { break }
        }
        return parts
    }

    private static func splitPart(_ data: Data) -> ([String: String], Data) {
        let crlfSep = Data("\r\n\r\n".utf8)
        let lfSep = Data("\n\n".utf8)
        guard let r = data.range(of: crlfSep) ?? data.range(of: lfSep) else {
            return ([:], data)
        }
        let headerText = String(data: data.subdata(in: data.startIndex..<r.lowerBound), encoding: .utf8)
            ?? String(data: data.subdata(in: data.startIndex..<r.lowerBound), encoding: .isoLatin1) ?? ""
        let body = data.subdata(in: r.upperBound..<data.endIndex)
        return (unfoldHeaders(headerText), body)
    }

    static func decodeQuotedPrintable(_ text: String) -> Data? {
        var output = Data()
        var index = text.startIndex

        while index < text.endIndex {
            let char = text[index]
            if char == "=" {
                let next = text.index(after: index)
                // Soft line break: "=\n" or "=\r\n"
                if next < text.endIndex, text[next] == "\n" {
                    index = text.index(after: next); continue
                }
                if next < text.endIndex, text[next] == "\r" {
                    let afterCR = text.index(after: next)
                    if afterCR < text.endIndex, text[afterCR] == "\n" {
                        index = text.index(after: afterCR); continue
                    }
                }
                // Hex byte: "=XX"
                if let end = text.index(index, offsetBy: 3, limitedBy: text.endIndex) {
                    let hex = String(text[text.index(after: index)..<end])
                    if let byte = UInt8(hex, radix: 16) {
                        output.append(byte)
                        index = end
                        continue
                    }
                }
                output.append(UInt8(ascii: "="))
                index = text.index(after: index)
            } else {
                output.append(contentsOf: Array(String(char).utf8))
                index = text.index(after: index)
            }
        }
        return output
    }

    private static func stripHTML(_ html: String) -> String {
        html
            .replacingOccurrences(of: "<style[^>]*>[\\s\\S]*?</style>", with: " ", options: .regularExpression)
            .replacingOccurrences(of: "<script[^>]*>[\\s\\S]*?</script>", with: " ", options: .regularExpression)
            .replacingOccurrences(of: "<[^>]+>", with: " ", options: .regularExpression)
            .replacingOccurrences(of: "&nbsp;", with: " ")
            .replacingOccurrences(of: "&amp;", with: "&")
            .replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression)
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
