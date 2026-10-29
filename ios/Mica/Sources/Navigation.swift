import Foundation

/// What an address means and what Spotlight shows: ported from Navigation in src\Core.cs (and android/Navigation.kt).
/// On iOS Mica's own pages are served on the mica:// scheme itself (SchemeHandler), so the address you see is the
/// address loaded; the https://*.mica.invalid hosts the shared game pages link to are mapped back to mica://.
enum Navigation {
    static let home = "mica://home"
    static let tetris = "mica://tetris"
    static let tetrisHost = "tetris.mica.invalid"
    static let gamesHost = "games.mica.invalid"

    struct Game { let address: String; let title: String; let path: String; let page: String }

    static let games: [Game] = [
        Game(address: "mica://games", title: "Games", path: "", page: "GameHub"),
        Game(address: "mica://snake", title: "Snake", path: "snake", page: "Snake"),
        Game(address: "mica://2048", title: "2048", path: "2048", page: "Game2048"),
        Game(address: "mica://minesweeper", title: "Minesweeper", path: "minesweeper", page: "Minesweeper"),
        Game(address: "mica://breakout", title: "Breakout", path: "breakout", page: "Breakout"),
        Game(address: "mica://sudoku", title: "Sudoku", path: "sudoku", page: "Sudoku"),
        Game(address: "mica://memory", title: "Memory", path: "memory", page: "Memory"),
        Game(address: "mica://flappy", title: "Flappy", path: "flappy", page: "Flappy"),
        Game(address: "mica://pong", title: "Pong", path: "pong", page: "Pong"),
        Game(address: "mica://invaders", title: "Invaders", path: "invaders", page: "Invaders"),
        Game(address: "mica://connect4", title: "Connect Four", path: "connect4", page: "ConnectFour"),
    ]

    static func game(_ address: String) -> Game? { games.first { $0.address == address } }

    static func isInternal(_ value: String) -> Bool { value == home || value == tetris || game(value) != nil || Reader.isReader(value) }
    static func isGame(_ value: String) -> Bool { value == tetris || game(value) != nil }

    /// The mica:// address for one of the https://*.mica.invalid addresses the shared pages use, else the address.
    static func fromEngine(_ value: String) -> String {
        guard value.hasPrefix("https://") else { return value }
        let host = hostOf(value)
        if host == tetrisHost { return tetris }
        if host == Reader.legacyHost { return Reader.prefix + String(value.dropFirst(("https://" + Reader.legacyHost + "/").count)) }
        if host == gamesHost {
            var path = String(value.dropFirst(("https://" + gamesHost).count))
            path = String(path.split(separator: "?").first ?? "").split(separator: "#").first.map(String.init) ?? ""
            path = path.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
            if let g = games.first(where: { $0.path == path }) { return g.address }
        }
        return value
    }

    static func isMicaHost(_ host: String) -> Bool { host.hasSuffix(".mica.invalid") }

    static func hostOf(_ url: String) -> String { Domains.hostOf(url) }

    /// What the island shows: the host without www., or the mica:// address.
    static func label(_ url: String) -> String {
        let shown = fromEngine(url)
        if Reader.isReader(shown) { return Reader.site(shown) }
        if shown.hasPrefix("mica://") { return shown }
        let host = hostOf(url)
        if host.isEmpty { return url }
        return host.hasPrefix("www.") ? String(host.dropFirst(4)) : host
    }

    static func isWeb(_ url: String) -> Bool {
        let l = url.lowercased()
        return l.hasPrefix("http://") || l.hasPrefix("https://")
    }

    private static let localhost = try! NSRegularExpression(pattern: "^(localhost|127\\.0\\.0\\.1|\\[::1\\])(:\\d+)?([/?#].*)?$", options: [.caseInsensitive])
    private static let domainLike = try! NSRegularExpression(pattern: "^([\\p{L}\\d-]+\\.)+[\\p{L}\\d-]+(:\\d+)?([/?#].*)?$")
    private static let schemeLike = try! NSRegularExpression(pattern: "^[a-z][a-z0-9+.-]*:", options: [.caseInsensitive])

    private static func matches(_ re: NSRegularExpression, _ s: String) -> Bool {
        re.firstMatch(in: s, range: NSRange(s.startIndex..., in: s)) != nil
    }

    static func encode(_ q: String) -> String {
        var allowed = CharacterSet.alphanumerics
        allowed.insert(charactersIn: "-._*")
        return (q.addingPercentEncoding(withAllowedCharacters: allowed) ?? q).replacingOccurrences(of: "%20", with: "+")
    }

    static func searchUrl(_ q: String) -> String { "https://www.google.com/search?q=" + encode(q) }
    static func isSearch(_ url: String) -> Bool { url.hasPrefix("https://www.google.com/search?q=") }

    /// What typing [input] opens: a mica:// address, a web address, or a search. Nil for other schemes.
    static func resolve(_ input: String) -> String? {
        let q = input.trimmingCharacters(in: .whitespacesAndNewlines)
        if q.isEmpty { return home }
        var lower = q.lowercased()
        while lower.hasSuffix("/") { lower.removeLast() }
        if isInternal(lower) { return lower }
        if matches(localhost, q) { return "http://" + q }
        if !q.contains(where: { $0.isWhitespace }) && matches(domainLike, q) { return "https://" + q }
        if matches(schemeLike, q) { return isWeb(q) ? q : nil }
        return searchUrl(q)
    }

    static let keywords: [String: String] = [
        "yt": "https://www.youtube.com/results?search_query=%s", "gh": "https://github.com/search?q=%s",
        "w": "https://en.wikipedia.org/w/index.php?search=%s", "maps": "https://www.google.com/maps/search/%s",
        "g": "https://www.google.com/search?q=%s", "so": "https://stackoverflow.com/search?q=%s",
        "r": "https://www.reddit.com/search/?q=%s", "amz": "https://www.amazon.com/s?k=%s",
    ]

    /// "yt cats" → YouTube's search for cats.
    static func expandKeyword(_ q: String) -> String? {
        let t = q.trimmingCharacters(in: .whitespaces)
        guard let space = t.firstIndex(of: " "), space > t.startIndex else { return nil }
        guard let template = keywords[t[..<space].lowercased()] else { return nil }
        let rest = t[t.index(after: space)...].trimmingCharacters(in: .whitespaces)
        if rest.isEmpty { return nil }
        return template.replacingOccurrences(of: "%s", with: encode(rest))
    }

    struct Entry { let url: String; let title: String; var tabId: String? = nil; var detail = "" }
    enum Kind { case tab, bookmark, history, search, open, shortcut }
    struct Hit { let kind: Kind; let title: String; let detail: String; let url: String; var tabId: String? = nil }

    /// Spotlight's results: tabs from every space, bookmarks, then history, every word typed appearing in the title
    /// or address; the typed address or search goes first, as desktop does.
    static func search(_ query: String, tabs: [Entry], history: [Entry], bookmarks: [Entry] = []) -> [Hit] {
        let q = query.trimmingCharacters(in: .whitespaces)
        let words = q.lowercased().split(separator: " ").map(String.init)
        var hits: [Hit] = []
        var seen = Set<String>()
        func add(_ list: [Entry], _ kind: Kind) {
            for e in list {
                if hits.count >= 7 { return }
                if e.url == home || e.url.isEmpty { continue }
                let text = (e.title + " " + fromEngine(e.url)).lowercased()
                if !words.isEmpty && !words.allSatisfy({ text.contains($0) }) { continue }
                if !seen.insert(e.url).inserted { continue }
                hits.append(Hit(kind: kind, title: e.title.trimmingCharacters(in: .whitespaces).isEmpty ? label(e.url) : e.title,
                                detail: e.detail.isEmpty ? label(e.url) : e.detail, url: e.url, tabId: e.tabId))
            }
        }
        add(tabs, .tab)
        add(bookmarks, .bookmark)
        add(history, .history)
        if q.isEmpty { return hits }
        let keyword = expandKeyword(q)
        if let keyword {
            hits.removeAll { $0.url == keyword }
            let rest = q[q.index(after: q.firstIndex(of: " ")!)...].trimmingCharacters(in: .whitespaces)
            var kw = hostOf(keyword)
            if kw.hasPrefix("www.") { kw = String(kw.dropFirst(4)) }
            hits.insert(Hit(kind: .shortcut, title: L("search_web_for", rest), detail: kw, url: keyword), at: 0)
        }
        guard let url = resolve(q) else { return hits }
        let typed: Hit
        if let i = hits.firstIndex(where: { $0.url == url }) { typed = hits.remove(at: i) }
        else if isSearch(url) { typed = Hit(kind: .search, title: L("search_web_for", q), detail: "Google", url: url) }
        else { typed = Hit(kind: .open, title: L("go_to", q), detail: label(url), url: url) }
        let keepTop = keyword != nil || (!hits.isEmpty && typed.kind == .search && startsSiteAddress(hits[0].url, q))
        hits.insert(typed, at: keepTop ? min(1, hits.count) : 0)
        return hits
    }

    // Only the start of a site's address ("git" for github.com) keeps that site above the search.
    private static func startsSiteAddress(_ url: String, _ q: String) -> Bool {
        if q.contains(where: { $0.isWhitespace }) { return false }
        var host = hostOf(url)
        if host.isEmpty { return false }
        var typed = q.lowercased()
        for p in ["https://", "http://"] where typed.hasPrefix(p) { typed = String(typed.dropFirst(p.count)) }
        if host.hasPrefix("www.") && !typed.hasPrefix("www.") { host = String(host.dropFirst(4)) }
        let afterScheme = url.components(separatedBy: "://").dropFirst().joined(separator: "://")
        let rest = afterScheme.firstIndex(of: "/").map { String(afterScheme[afterScheme.index(after: $0)...]) } ?? ""
        return (host + "/" + rest).hasPrefix(typed) || host.hasPrefix(typed)
    }
}

/// Hosts, registrable domains (by the public suffix list) and the like: android/blocker/Domains.kt.
enum Domains {
    private final class Psl { var rules = Set<String>(); var wild = Set<String>(); var except = Set<String>() }

    private static let psl: Psl? = {
        guard let url = Bundle.main.url(forResource: "psl", withExtension: "dat", subdirectory: "Shared"),
              let text = try? String(contentsOf: url, encoding: .utf8) else { return nil }
        let p = Psl()
        text.enumerateLines { line, _ in
            if line.isEmpty || line.hasPrefix("//") { return }
            var r = line.trimmingCharacters(in: .whitespaces).lowercased()
            if r.unicodeScalars.contains(where: { $0.value > 127 }), let a = ascii(r) { r = a }
            if r.hasPrefix("!") { p.except.insert(String(r.dropFirst())) }
            else if r.hasPrefix("*.") { p.wild.insert(String(r.dropFirst(2))) }
            else { p.rules.insert(r) }
        }
        return p
    }()

    /// Punycode for an internationalised name, through URLComponents.
    private static func ascii(_ host: String) -> String? {
        URL(string: "https://" + host + "/")?.host?.lowercased()
    }

    /// The registrable domain (eTLD+1) of a lower-case host, or the host itself for IPs and bare names.
    static func registrable(_ host: String) -> String {
        guard let first = host.first, first != "[", let last = host.last, !last.isNumber else { return host }
        guard let p = psl else {
            let parts = host.split(separator: ".")
            return parts.count <= 2 ? host : parts.suffix(2).joined(separator: ".")
        }
        var starts = [host.startIndex]
        var i = host.startIndex
        while i < host.endIndex { if host[i] == "." { starts.append(host.index(after: i)) }; i = host.index(after: i) }
        for k in starts.indices {
            let s = String(host[starts[k]...])
            if p.except.contains(s) { return s }
            if p.rules.contains(s) || (k + 1 < starts.count && p.wild.contains(String(host[starts[k + 1]...]))) {
                return k == 0 ? host : String(host[starts[k - 1]...])
            }
        }
        return starts.count >= 2 ? String(host[starts[starts.count - 2]...]) : host
    }

    static func sameSite(_ a: String, _ b: String) -> Bool { a == b || registrable(a) == registrable(b) }

    /// The lower-case host of a URL with "//", or "" if there isn't one.
    static func hostOf(_ url: String) -> String {
        guard let scheme = url.range(of: "://") else { return "" }
        var rest = url[scheme.upperBound...]
        if let end = rest.firstIndex(where: { $0 == "/" || $0 == "?" || $0 == "#" }) { rest = rest[..<end] }
        if let at = rest.lastIndex(of: "@") { rest = rest[rest.index(after: at)...] }
        if rest.hasPrefix("[") {
            guard let close = rest.firstIndex(of: "]") else { return "" }
            return String(rest[...close]).lowercased()
        }
        if let colon = rest.firstIndex(of: ":") { rest = rest[..<colon] }
        var h = rest.lowercased()
        while h.hasSuffix(".") { h.removeLast() }
        return h
    }

    /// scheme://host[:port] of a URL, lower-cased.
    static func origin(_ url: String) -> String {
        guard let u = URLComponents(string: url), let scheme = u.scheme, let host = u.host else { return url.lowercased() }
        return (scheme + "://" + host + (u.port.map { ":\($0)" } ?? "")).lowercased()
    }
}

/// HTTPS first (desktop's BrowserProtection): an http:// address goes to its https:// twin, unless it's on your own
/// network or a site you chose to open without HTTPS this session.
enum Https {
    private static var allowed = Set<String>()
    /// The error page's "Continue without HTTPS" goes here; Mica catches it before it leaves the device.
    static let continueHost = "continue-http.mica.invalid"
    private static let localSuffixes = [".localhost", ".local", ".test", ".internal", ".lan", ".home.arpa"]

    static func allow(_ host: String) { allowed.insert(Domains.registrable(host.lowercased())) }

    /// The https:// address to load instead of [url], or nil to load it as it is.
    static func upgrade(_ url: String) -> String? {
        guard url.lowercased().hasPrefix("http://"), let u = URLComponents(string: url), let host = u.host?.lowercased() else { return nil }
        if keepsHttp(port: u.port, host: host) || allowed.contains(Domains.registrable(host)) { return nil }
        return "https" + url.dropFirst(4)
    }

    private static func keepsHttp(port: Int?, host: String) -> Bool {
        if let port, port != 80 { return true }
        if !host.contains(".") || host == "localhost" || localSuffixes.contains(where: { host.hasSuffix($0) }) { return true }
        if host.hasPrefix("[") { return true }
        let p = host.split(separator: ".").map { Int($0) }
        if p.count == 4, p.allSatisfy({ $0 != nil && (0...255).contains($0!) }) {
            let a = p[0]!, b = p[1]!
            return a == 10 || a == 127 || (a == 172 && (16...31).contains(b)) || (a == 192 && b == 168) || (a == 169 && b == 254) || (a == 100 && (64...127).contains(b))
        }
        return false
    }
}
