import UIKit
import WebKit

final class Space {
    let id: String
    var name: String
    var color: UInt32
    var activeTab: String?
    init(id: String, name: String, color: UInt32) { self.id = id; self.name = name; self.color = color }
}

final class Tab {
    let id: String
    var space: String
    var url: String
    var title: String
    var pinned = false
    var lastUsed = Date().timeIntervalSince1970
    /// Live while in use or used recently; nil while resting.
    var view: WKWebView?
    /// Back/forward history kept while resting (WKWebView.interactionState).
    var saved: Any?
    var progress: Double = 1
    var playing = false
    /// The tab a link opened this one from; closing it returns there.
    var parent: String?
    /// The page's own colour (theme-color or background), or nil.
    var tint: UIColor?
    var icon: UIImage?
    var desktop = false
    /// The http:// address Mica is trying over HTTPS right now, so a failure can offer it.
    var upgraded: String?
    /// The cookie jar it's in, or nil for Mica's own cookies.
    var jar: String?
    var group: String?

    var isHome: Bool { url == Navigation.home }
    /// A private tab: its own website data, never saved, gone with the last private tab.
    var isPrivate: Bool { space == Browser.privateSpace }
    var host: String { Domains.hostOf(url) }

    init(id: String = UUID().uuidString, space: String, url: String, title: String = "") {
        self.id = id; self.space = space; self.url = url; self.title = title
    }
}

/// A named set of cookies and sign-ins of its own (desktop's cookie jar): a WKWebsiteDataStore with its own identifier.
final class Jar {
    let id: String
    var name: String
    var color: UInt32
    init(id: String = UUID().uuidString, name: String, color: UInt32) { self.id = id; self.name = name; self.color = color }
}

/// Tabs kept together under a name and colour in the deck.
final class TabGroup {
    let id: String
    var name: String
    var color: UInt32
    let space: String
    var collapsed: Bool
    init(id: String, name: String, color: UInt32, space: String, collapsed: Bool = false) {
        self.id = id; self.name = name; self.color = color; self.space = space; self.collapsed = collapsed
    }
}

final class HistoryEntry {
    let url: String
    var title: String
    var at: TimeInterval
    var visits: Int
    init(url: String, title: String, at: TimeInterval = Date().timeIntervalSince1970, visits: Int = 1) {
        self.url = url; self.title = title; self.at = at; self.visits = visits
    }
}

/// Mica's files: Application Support/Mica (state, read later, sessions) and Caches/Mica.
enum Files {
    static let support: URL = {
        let u = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("Mica", isDirectory: true)
        try? FileManager.default.createDirectory(at: u, withIntermediateDirectories: true)
        return u
    }()
    static let caches: URL = {
        let u = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0].appendingPathComponent("Mica", isDirectory: true)
        try? FileManager.default.createDirectory(at: u, withIntermediateDirectories: true)
        return u
    }()
    /// Downloads go where the Files app shows them (On My iPhone › Mica › Downloads).
    static let downloads: URL = {
        let u = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0].appendingPathComponent("Downloads", isDirectory: true)
        try? FileManager.default.createDirectory(at: u, withIntermediateDirectories: true)
        return u
    }()

    static func dir(_ name: String, cache: Bool = false) -> URL {
        let u = (cache ? caches : support).appendingPathComponent(name, isDirectory: true)
        try? FileManager.default.createDirectory(at: u, withIntermediateDirectories: true)
        return u
    }

    static func write(_ data: Data, to url: URL) { try? data.write(to: url, options: [.atomic]) }
}

/// Tabs, spaces, history and the library in state.json, written atomically. Same shape as Android's.
struct Store {
    struct State {
        var spaces: [Space]
        var tabs: [Tab]
        var activeSpace: String
        var history: [HistoryEntry]
        var bookmarks: [HistoryEntry]
        var readLater: [HistoryEntry]
        var closed: [HistoryEntry]
        var groups: [TabGroup]
        var jars: [Jar]?
    }

    private let file = Files.support.appendingPathComponent("state.json")

    private func entries(_ o: [String: Any], _ name: String) -> [HistoryEntry] {
        (o[name] as? [[String: Any]] ?? []).compactMap { h in
            guard let u = h["u"] as? String else { return nil }
            return HistoryEntry(url: u, title: h["t"] as? String ?? "", at: (h["at"] as? Double ?? 0) / 1000, visits: max(1, h["v"] as? Int ?? 1))
        }
    }

    private func entries(_ list: [HistoryEntry]) -> [[String: Any]] {
        list.map { e in
            var o: [String: Any] = ["u": e.url, "t": e.title, "at": Int64(e.at * 1000)]
            if e.visits > 1 { o["v"] = e.visits }
            return o
        }
    }

    func load() -> State? {
        guard let data = try? Data(contentsOf: file), let o = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        let spaces: [Space] = (o["spaces"] as? [[String: Any]] ?? []).compactMap { s in
            guard let id = s["id"] as? String else { return nil }
            let space = Space(id: id, name: s["name"] as? String ?? "", color: UInt32(truncatingIfNeeded: s["color"] as? Int ?? 0x71608C))
            space.activeTab = (s["active"] as? String).flatMap { $0.isEmpty ? nil : $0 }
            return space
        }
        guard !spaces.isEmpty else { return nil }
        func opt(_ v: Any?) -> String? { (v as? String).flatMap { $0.isEmpty ? nil : $0 } }
        let tabs: [Tab] = (o["tabs"] as? [[String: Any]] ?? []).compactMap { t in
            guard let id = t["id"] as? String, let space = t["space"] as? String, let url = t["url"] as? String else { return nil }
            let tab = Tab(id: id, space: space, url: url, title: t["title"] as? String ?? "")
            tab.pinned = t["pinned"] as? Bool ?? false
            tab.lastUsed = (t["used"] as? Double ?? 0) / 1000
            tab.desktop = t["desktop"] as? Bool ?? false
            if let tint = t["tint"] as? Int, tint != 0 { tab.tint = UIColor(rgb: UInt32(truncatingIfNeeded: tint)) }
            tab.parent = opt(t["parent"]); tab.group = opt(t["group"]); tab.jar = opt(t["jar"])
            return tab
        }
        let groups: [TabGroup] = (o["groups"] as? [[String: Any]] ?? []).compactMap { g in
            guard let id = g["id"] as? String, let space = g["space"] as? String else { return nil }
            return TabGroup(id: id, name: g["name"] as? String ?? "", color: UInt32(truncatingIfNeeded: g["color"] as? Int ?? 0), space: space, collapsed: g["collapsed"] as? Bool ?? false)
        }
        let jars: [Jar]? = (o["jars"] as? [[String: Any]])?.compactMap { j in
            guard let id = j["id"] as? String else { return nil }
            return Jar(id: id, name: j["name"] as? String ?? "", color: UInt32(truncatingIfNeeded: j["color"] as? Int ?? 0))
        }
        return State(spaces: spaces, tabs: tabs, activeSpace: o["activeSpace"] as? String ?? spaces[0].id, history: entries(o, "history"),
                     bookmarks: entries(o, "bookmarks"), readLater: entries(o, "readLater"), closed: entries(o, "closed"), groups: groups, jars: jars)
    }

    func save(_ s: State) {
        var o: [String: Any] = ["activeSpace": s.activeSpace]
        o["spaces"] = s.spaces.filter { $0.id != Browser.privateSpace }.map { ["id": $0.id, "name": $0.name, "color": Int($0.color), "active": $0.activeTab ?? ""] as [String: Any] }
        o["tabs"] = s.tabs.filter { !$0.isPrivate }.map { t -> [String: Any] in
            ["id": t.id, "space": t.space, "url": t.url, "title": t.title, "pinned": t.pinned, "used": Int64(t.lastUsed * 1000), "desktop": t.desktop,
             "tint": Int(t.tint?.rgbValue ?? 0), "parent": t.parent ?? "", "group": t.group ?? "", "jar": t.jar ?? ""]
        }
        o["history"] = entries(s.history)
        o["bookmarks"] = entries(s.bookmarks)
        o["readLater"] = entries(s.readLater)
        o["closed"] = entries(s.closed)
        if let jars = s.jars { o["jars"] = jars.map { ["id": $0.id, "name": $0.name, "color": Int($0.color)] as [String: Any] } }
        o["groups"] = s.groups.filter { $0.space != Browser.privateSpace }.map { ["id": $0.id, "name": $0.name, "color": Int($0.color), "space": $0.space, "collapsed": $0.collapsed] as [String: Any] }
        if let data = try? JSONSerialization.data(withJSONObject: o) { Files.write(data, to: file) }
    }
}

/// Settings kept in UserDefaults.
final class Prefs {
    static let shared = Prefs()
    private let d = UserDefaults.standard

    enum Theme: Int { case system, light, dark
        var style: UIUserInterfaceStyle { self == .light ? .light : self == .dark ? .dark : .unspecified }
    }

    var theme: Theme {
        get { Theme(rawValue: d.integer(forKey: "theme")) ?? .system }
        set { d.set(newValue.rawValue, forKey: "theme") }
    }
    var blockerOn: Bool {
        get { d.object(forKey: "blocker") as? Bool ?? true }
        set { d.set(newValue, forKey: "blocker") }
    }
    /// Sites (registrable domains) where ads are allowed.
    var allowedSites: Set<String> {
        get { Set(d.stringArray(forKey: "allowed") ?? []) }
        set { d.set(Array(newValue).sorted(), forKey: "allowed") }
    }
    var desktopDefault: Bool {
        get { d.bool(forKey: "desktopDefault") }
        set { d.set(newValue, forKey: "desktopDefault") }
    }
    var micaPasswords: Bool {
        get { d.object(forKey: "micaPasswords") as? Bool ?? true }
        set { d.set(newValue, forKey: "micaPasswords") }
    }
    var readerSize: Int {
        get { (d.object(forKey: "readerSize") as? Int) ?? 18 }
        set { d.set(min(30, max(14, newValue)), forKey: "readerSize") }
    }
    var readerSerif: Bool {
        get { d.object(forKey: "readerSerif") as? Bool ?? true }
        set { d.set(newValue, forKey: "readerSerif") }
    }
    /// Text size per site, as a percentage.
    func zoom(for host: String) -> Int { (d.dictionary(forKey: "siteZoom")?[host] as? Int) ?? 100 }
    func setZoom(_ host: String, _ percent: Int) {
        var all = d.dictionary(forKey: "siteZoom") ?? [:]
        if percent == 100 { all.removeValue(forKey: host) } else { all[host] = min(300, max(50, percent)) }
        d.set(all, forKey: "siteZoom")
    }
    /// Camera and microphone answers per origin: "origin\tkind" → allowed.
    var sitePermissions: [String: Bool] {
        get { d.dictionary(forKey: "sitePermissions") as? [String: Bool] ?? [:] }
        set { d.set(newValue, forKey: "sitePermissions") }
    }
}
