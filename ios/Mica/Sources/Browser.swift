import UIKit
import WebKit

enum DialogKind { case alert, confirm, prompt }

/// What the browser needs from the screen showing it (MainViewController).
protocol BrowserHost: AnyObject {
    var webHost: UIView { get }
    func activeChanged()
    func tabsChanged()
    func progressChanged(_ tab: Tab)
    func pageChanged(_ tab: Tab)
    func scrolled(_ dy: CGFloat, atTop: Bool)
    func announce(_ text: String)
    func openExternal(_ url: URL)
    func pageDialog(_ tab: Tab, kind: DialogKind, origin: String, message: String, defaultText: String?, answer: @escaping (Bool, String?) -> Void)
    func httpAuth(_ tab: Tab, host: String, answer: @escaping (URLCredential?) -> Void)
    func sitePermission(_ tab: Tab, origin: String, kind: String, answer: @escaping (Bool) -> Void)
    func linkMenu(_ tab: Tab, link: URL) -> UIContextMenuConfiguration?
    func readerMessage(_ tab: Tab, _ message: String)
    func loginMessage(_ tab: Tab, origin: String, json: String)
}

/// Tabs, spaces and their web views (android/Browser.kt).
///
/// Memory and battery: only the active tab and the two used most recently keep a web view; older tabs rest (their
/// view is dropped, back/forward history kept as WKWebView.interactionState). A tab playing media never rests.
final class Browser: NSObject {
    static let shared = Browser()
    static let privateSpace = "private"

    private let store = Store()
    private(set) var spaces: [Space]
    var tabs: [Tab]
    var history: [HistoryEntry]
    /// Newest first, as on desktop.
    var bookmarks: [HistoryEntry]
    var readLater: [HistoryEntry]
    var recentlyClosed: [HistoryEntry]
    private(set) var groups: [TabGroup]
    private(set) var jars: [Jar]
    private var lastSpaceId = "personal"
    private(set) var activeSpaceId: String
    weak var host: BrowserHost?
    private var saveTimer: Timer?
    private var inBackground = false
    private var delegates: [String: PageDelegate] = [:]
    /// The website data of this private session: a new one each session, gone with the last private tab.
    private var privateStore: WKWebsiteDataStore?

    private override init() {
        if let s = store.load() {
            spaces = s.spaces; tabs = s.tabs; history = s.history; activeSpaceId = s.activeSpace
            bookmarks = s.bookmarks; readLater = s.readLater; recentlyClosed = s.closed; groups = s.groups
            jars = s.jars ?? []
        } else {
            spaces = [Space(id: "personal", name: "", color: 0x71608C), Space(id: "work", name: "", color: 0x7C977D)]
            tabs = []; history = []; activeSpaceId = "personal"
            bookmarks = []; readLater = []; recentlyClosed = []; groups = []; jars = []
        }
        super.init()
        if jars.isEmpty && store.load()?.jars == nil { jars = defaultJars() }
        if !spaces.contains(where: { $0.id == activeSpaceId }) { activeSpaceId = spaces[0].id }
        tabs.removeAll { t in !spaces.contains { $0.id == t.space } }
        if !tabs.contains(where: { $0.space == activeSpaceId }) { _ = open(Navigation.home, activate: true) }
        normalizeGroups()
        lastSpaceId = activeSpaceId
        for t in tabs where t.jar != nil && !jars.contains(where: { $0.id == t.jar }) { t.jar = nil }
        // Jars deleted while in use leave their data until the next start.
        for id in UserDefaults.standard.stringArray(forKey: "deletedJars") ?? [] {
            if let uuid = UUID(uuidString: id) { WKWebsiteDataStore.remove(forIdentifier: uuid) { _ in } }
        }
        UserDefaults.standard.removeObject(forKey: "deletedJars")
    }

    var activeSpace: Space { spaces.first { $0.id == activeSpaceId } ?? spaces[0] }
    var active: Tab? { activeSpace.activeTab.flatMap { id in tabs.first { $0.id == id && $0.space == activeSpaceId } } }
    func tabsIn(_ spaceId: String? = nil) -> [Tab] { let s = spaceId ?? activeSpaceId; return tabs.filter { $0.space == s } }
    func tab(_ id: String?) -> Tab? { id.flatMap { id in tabs.first { $0.id == id } } }
    func tab(of view: WKWebView?) -> Tab? { view.flatMap { v in tabs.first { $0.view === v } } }
    func space(_ id: String) -> Space? { spaces.first { $0.id == id } }

    func spaceName(_ space: Space) -> String {
        if !space.name.isEmpty { return space.name }
        switch space.id {
        case "personal": return L("personal")
        case "work": return L("work")
        case Browser.privateSpace: return L("private_tabs")
        default: return space.id
        }
    }

    func attach(_ h: BrowserHost) {
        host = h
        show(active)
    }

    // MARK: Tabs

    @discardableResult
    func open(_ url: String, activate: Bool = true, parent: Tab? = nil, spaceId: String? = nil) -> Tab {
        let space = spaceId ?? activeSpaceId
        let tab = Tab(space: space, url: url)
        tab.parent = parent?.id
        tab.desktop = Prefs.shared.desktopDefault
        if let parent, parent.space == space { tab.jar = parent.jar }
        var at = tabs.count
        if let p = parent, let last = tabs.lastIndex(where: { $0.space == space && ($0.parent == p.id || $0.id == p.id) }) { at = last + 1 }
        tabs.insert(tab, at: min(max(0, at), tabs.count))
        if activate { self.activate(tab) } else { host?.tabsChanged(); saveLater() }
        return tab
    }

    func activate(_ tab: Tab) {
        let old = active
        // In a split, another website takes the place of the pane you were on; anything else ends the split.
        if case let (a, b)? = split, tab.id != a && tab.id != b {
            let other = old?.id == a ? b : a
            if tab.space == activeSpaceId, Navigation.isWeb(tab.url), let old, old.id == a || old.id == b {
                split = old.id == a ? (tab.id, other) : (other, tab.id)
            } else {
                split = nil
                self.tab(other)?.view?.removeFromSuperview()
                dropSplitView()
            }
        }
        if tab.space != activeSpaceId { activeSpaceId = tab.space }
        if !tab.isPrivate { lastSpaceId = tab.space }
        activeSpace.activeTab = tab.id
        tab.lastUsed = Date().timeIntervalSince1970
        if let old, old !== tab, !inSplit(old) { old.view?.removeFromSuperview() }
        show(tab)
        enforceBudget()
        host?.activeChanged()
        saveLater()
    }

    /// The tab's web view, created (or restored, if it was resting) when needed.
    private func viewOf(_ tab: Tab) -> WKWebView {
        if let v = tab.view { return v }
        let v = createView(tab)
        if let saved = tab.saved { v.interactionState = saved } else { load(tab, tab.url) }
        tab.saved = nil
        return v
    }

    /// Puts the tab's web view into the host, beside its split partner if any.
    func show(_ tab: Tab?) {
        guard let h = host else { return }
        let container = h.webHost
        if let tab, case let (a, b)? = split, let first = self.tab(a), let second = self.tab(b), tab === first || tab === second {
            let v1 = viewOf(first), v2 = viewOf(second)
            let fresh = splitView == nil
            let sv = splitView ?? SplitView { [weak self] i in
                guard let self, case let (a, b)? = self.split, let t = self.tab(i == 0 ? a : b) else { return }
                self.focusPane(t)
            }
            splitView = sv
            sv.put(v1, v2)
            sv.activeIndex = tab === first ? 0 : 1
            if sv.superview !== container {
                container.subviews.forEach { $0.removeFromSuperview() }
                container.addSubview(sv)
                sv.pin(to: container)
                container.layoutIfNeeded()
            }
            if fresh && !inBackground { sv.enter() }
            return
        }
        container.subviews.forEach { $0.removeFromSuperview() }
        guard let tab, !tab.isHome else { return }
        let v = viewOf(tab)
        container.addSubview(v)
        v.pin(to: container)
    }

    func navigate(_ tab: Tab, _ url: String) {
        tab.url = url
        if url == Navigation.home {
            if inSplit(tab) { endSplit() }
            destroyView(tab); tab.title = ""; tab.tint = nil
            if tab === active { show(tab) }
            host?.pageChanged(tab); saveLater(); return
        }
        if let v = tab.view { _ = v; load(tab, url) } else if tab === active { show(tab) } else { tab.saved = nil }
        host?.pageChanged(tab)
        saveLater()
    }

    private func load(_ tab: Tab, _ url: String) {
        guard let v = tab.view else { return }
        let secure = Https.upgrade(url)
        tab.upgraded = secure != nil ? url : nil
        guard let u = URL(string: secure ?? url) else { return }
        v.load(URLRequest(url: u))
    }

    func close(_ tab: Tab) {
        let wasActive = tab === active
        let siblings = tabsIn(tab.space)
        let index = siblings.firstIndex { $0 === tab } ?? 0
        let partner = splitPartner(tab)
        if partner != nil { split = nil; dropSplitView() }
        destroyView(tab)
        tabs.removeAll { $0 === tab }
        if Navigation.isWeb(tab.url) && !tab.isPrivate {
            recentlyClosed.removeAll { $0.url == tab.url }
            recentlyClosed.insert(HistoryEntry(url: tab.url, title: tab.title), at: 0)
            if recentlyClosed.count > 25 { recentlyClosed.removeLast(recentlyClosed.count - 25) }
        }
        if tab.isPrivate && !tabs.contains(where: { $0.isPrivate }) { endPrivate(); return }
        normalizeGroups()
        if wasActive {
            let rest = tabsIn(tab.space)
            let byParent = self.tab(tab.parent).flatMap { $0.space == tab.space ? $0 : nil }
            let next = partner ?? byParent ?? (rest.isEmpty ? nil : rest[min(index, rest.count - 1)])
            if let next { activate(next) } else { open(Navigation.home, activate: true, spaceId: tab.space) }
        } else {
            if partner != nil { show(active) }
            host?.tabsChanged()
        }
        saveLater()
    }

    func step(_ dir: Int) {
        let list = tabsIn()
        guard list.count > 1, let a = active, let i = list.firstIndex(where: { $0 === a }) else { return }
        activate(list[(i + dir + list.count) % list.count])
    }

    // MARK: Library

    func isBookmarked(_ url: String) -> Bool { bookmarks.contains { $0.url == url } }
    func isSavedForLater(_ url: String) -> Bool { readLater.contains { $0.url == url } }

    /// Adds the page, or takes it out if it's there already. Returns whether it's in the list now.
    func toggleBookmark(_ tab: Tab) -> Bool { toggle(&bookmarks, tab) }
    func toggleReadLater(_ tab: Tab) -> Bool { toggle(&readLater, tab) }

    private func toggle(_ list: inout [HistoryEntry], _ tab: Tab) -> Bool {
        let url = tab.url
        if list.contains(where: { $0.url == url }) { list.removeAll { $0.url == url }; saveLater(); return false }
        list.insert(HistoryEntry(url: url, title: tab.title.isEmpty ? Navigation.label(url) : tab.title), at: 0)
        saveLater()
        return true
    }

    func forgetBookmark(_ e: HistoryEntry) { bookmarks.removeAll { $0 === e }; saveLater() }
    func forgetReadLater(_ e: HistoryEntry) { readLater.removeAll { $0 === e }; saveLater() }
    func forgetHistory(_ e: HistoryEntry) { history.removeAll { $0 === e }; saveLater() }
    func clearHistory() { history.removeAll(); recentlyClosed.removeAll(); saveLater() }

    /// The address read later and highlights use for a tab: the article's own address in reading mode.
    func pageKey(_ tab: Tab) -> String {
        if Reader.isReader(tab.url), let a = Reader.load(Reader.id(tab.url)), let u = a["url"] as? String, !u.isEmpty { return u }
        return tab.url
    }

    /// Finds the article on the page; [done] gets it, or nil if the page isn't one.
    func readArticle(_ tab: Tab, done: @escaping ([String: Any]?) -> Void) {
        guard let v = tab.view, Navigation.isWeb(tab.url) else { return done(nil) }
        v.evaluateJavaScript(Reader.script) { raw, _ in done(Reader.parse(raw)) }
    }

    // MARK: Private tabs

    @discardableResult
    func openPrivate(_ url: String = Navigation.home) -> Tab {
        if !spaces.contains(where: { $0.id == Browser.privateSpace }) { spaces.append(Space(id: Browser.privateSpace, name: "", color: 0x4A4453)) }
        if privateStore == nil { privateStore = .nonPersistent() }
        return open(url, activate: true, spaceId: Browser.privateSpace)
    }

    /// Closes every private tab; their cookies, storage and cache go with them.
    func closePrivate() {
        guard tabs.contains(where: { $0.isPrivate }) else { return }
        tabs.filter { $0.isPrivate }.forEach { destroyView($0) }
        tabs.removeAll { $0.isPrivate }
        endPrivate()
    }

    private func endPrivate() {
        spaces.removeAll { $0.id == Browser.privateSpace }
        groups.removeAll { $0.space == Browser.privateSpace }
        privateStore = nil
        if activeSpaceId == Browser.privateSpace {
            let back = spaces.first { $0.id == lastSpaceId } ?? spaces[0]
            activeSpaceId = back.id
            let next = tab(back.activeTab).flatMap { $0.space == back.id ? $0 : nil } ?? tabsIn(back.id).max { $0.lastUsed < $1.lastUsed }
            if let next { activate(next) } else { open(Navigation.home, activate: true, spaceId: back.id) }
        } else { host?.tabsChanged() }
        saveLater()
    }

    /// The website data for [tab]'s pages: the private session's, its jar's, or Mica's own.
    func dataStore(for tab: Tab) -> WKWebsiteDataStore {
        if tab.isPrivate {
            if privateStore == nil { privateStore = .nonPersistent() }
            return privateStore!
        }
        if let id = tab.jar, let uuid = UUID(uuidString: id) { return WKWebsiteDataStore(forIdentifier: uuid) }
        return .default()
    }

    // MARK: Cookie jars

    private func defaultJars() -> [Jar] {
        [Jar(name: L("jar_work"), color: 0x4A7FD9), Jar(name: L("jar_shopping"), color: 0xE0A526),
         Jar(name: L("jar_banking"), color: 0x3E9B5F), Jar(name: L("jar_social"), color: 0xD45C9B)]
    }

    func jar(_ id: String?) -> Jar? { id.flatMap { id in jars.first { $0.id == id } } }

    @discardableResult
    func newJar(_ name: String) -> Jar {
        let used = Set(jars.map { $0.color })
        let jar = Jar(name: String(name.prefix(24)), color: Mica.palette.first { !used.contains($0) } ?? Mica.palette[jars.count % Mica.palette.count])
        jars.append(jar)
        saveLater()
        return jar
    }

    func renameJar(_ jar: Jar, _ name: String) { jar.name = String(name.prefix(24)); saveLater() }

    /// Puts [tab] in [jar] (nil: Mica's own cookies). Its page starts again there, signed in as that jar is.
    func moveToJar(_ tab: Tab, _ jar: Jar?) {
        if tab.isPrivate || tab.jar == jar?.id { return }
        tab.jar = jar?.id
        destroyView(tab)
        tab.saved = nil
        if tab === active || inSplit(tab) { show(active) }
        host?.tabsChanged()
        saveLater()
    }

    /// The jar goes, with its cookies; its tabs go back to Mica's own.
    func deleteJar(_ jar: Jar) {
        tabs.filter { $0.jar == jar.id }.forEach { moveToJar($0, nil) }
        jars.removeAll { $0 === jar }
        if let uuid = UUID(uuidString: jar.id) {
            WKWebsiteDataStore.remove(forIdentifier: uuid) { error in
                if error != nil {
                    var later = UserDefaults.standard.stringArray(forKey: "deletedJars") ?? []
                    later.append(jar.id)
                    UserDefaults.standard.set(later, forKey: "deletedJars")
                }
            }
        }
        saveLater()
    }

    // MARK: Tab groups

    func group(_ id: String?) -> TabGroup? { id.flatMap { id in groups.first { $0.id == id } } }
    func groupsIn(_ spaceId: String? = nil) -> [TabGroup] { let s = spaceId ?? activeSpaceId; return groups.filter { $0.space == s } }
    func members(_ g: TabGroup) -> [Tab] { tabs.filter { $0.group == g.id } }

    @discardableResult
    func newGroup(_ tab: Tab, name: String, color: UInt32) -> TabGroup {
        let g = TabGroup(id: "g" + String(Int(Date().timeIntervalSince1970 * 1000), radix: 36), name: name, color: color, space: tab.space)
        groups.append(g)
        addToGroup(tab, g)
        return g
    }

    /// Joins the group, placed after its last tab so a group's tabs stay together.
    func addToGroup(_ tab: Tab, _ g: TabGroup) {
        guard tab.space == g.space else { return }
        tab.group = g.id
        tab.pinned = false
        if tabs.contains(where: { $0.group == g.id && $0 !== tab }) {
            tabs.removeAll { $0 === tab }
            let last = tabs.lastIndex { $0.group == g.id } ?? (tabs.count - 1)
            tabs.insert(tab, at: last + 1)
        }
        normalizeGroups(); host?.tabsChanged(); saveLater()
    }

    func leaveGroup(_ tab: Tab) {
        guard let g = group(tab.group) else { return }
        tab.group = nil
        tabs.removeAll { $0 === tab }
        let last = tabs.lastIndex { $0.group == g.id } ?? (tabs.count - 1)
        tabs.insert(tab, at: min(tabs.count, last + 1))
        normalizeGroups(); host?.tabsChanged(); saveLater()
    }

    func renameGroup(_ g: TabGroup, name: String, color: UInt32) { g.name = name; g.color = color; host?.tabsChanged(); saveLater() }
    func ungroup(_ g: TabGroup) { members(g).forEach { $0.group = nil }; groups.removeAll { $0 === g }; host?.tabsChanged(); saveLater() }
    func closeGroup(_ g: TabGroup) { members(g).forEach { close($0) }; groups.removeAll { $0 === g }; host?.tabsChanged(); saveLater() }
    func setCollapsed(_ g: TabGroup, _ collapsed: Bool) { g.collapsed = collapsed; host?.tabsChanged(); saveLater() }

    /// Empty groups go; tabs that point at a missing group or another space's leave it; members sit together.
    func normalizeGroups() {
        for t in tabs where t.group != nil && group(t.group)?.space != t.space { t.group = nil }
        groups.removeAll { g in !tabs.contains { $0.group == g.id } }
        for g in groups {
            guard let first = tabs.firstIndex(where: { $0.group == g.id }) else { continue }
            let ms = tabs.filter { $0.group == g.id }
            tabs.removeAll { $0.group == g.id }
            tabs.insert(contentsOf: ms, at: min(first, tabs.count))
        }
    }

    // MARK: Spaces

    func switchSpace(_ id: String) {
        guard id != activeSpaceId, let space = space(id) else { return }
        let target = tab(space.activeTab).flatMap { $0.space == id ? $0 : nil } ?? tabsIn(id).max { $0.lastUsed < $1.lastUsed }
        if let target { activate(target) } else { activeSpaceId = id; open(Navigation.home, activate: true, spaceId: id) }
    }

    @discardableResult
    func addSpace(name: String, color: UInt32) -> Space {
        let space = Space(id: "s" + String(Int(Date().timeIntervalSince1970 * 1000), radix: 36), name: name, color: color)
        let at = spaces.firstIndex { $0.id == Browser.privateSpace } ?? spaces.count
        spaces.insert(space, at: at)
        switchSpace(space.id)
        return space
    }

    func editSpace(_ space: Space, name: String, color: UInt32) { space.name = name; space.color = color; host?.tabsChanged(); saveLater() }

    func deleteSpace(_ space: Space) {
        let normal = spaces.filter { $0.id != Browser.privateSpace }
        guard normal.count > 1, space.id != Browser.privateSpace, let into = normal.first(where: { $0 !== space }) else { return }
        tabs.filter { $0.space == space.id }.forEach { $0.space = into.id; $0.group = nil }
        spaces.removeAll { $0 === space }
        if activeSpaceId == space.id {
            activeSpaceId = into.id
            if let t = active ?? tabsIn(into.id).first { activate(t) }
        }
        normalizeGroups(); host?.tabsChanged(); saveLater()
    }

    func moveTo(_ tab: Tab, spaceId: String) {
        // A private tab's page lives in other website data; it can't cross over either way.
        if tab.space == spaceId || tab.isPrivate || spaceId == Browser.privateSpace { return }
        tab.group = nil
        if tab === active {
            let rest = tabsIn(tab.space).filter { $0 !== tab }
            tab.space = spaceId
            if let next = rest.max(by: { $0.lastUsed < $1.lastUsed }) { activate(next) } else { open(Navigation.home, spaceId: activeSpaceId) }
        } else { tab.space = spaceId }
        host?.tabsChanged(); saveLater()
    }

    func setPinned(_ tab: Tab, _ pinned: Bool) { tab.pinned = pinned; host?.tabsChanged(); saveLater() }

    func setDesktop(_ tab: Tab, _ desktop: Bool) {
        tab.desktop = desktop
        if let v = tab.view { v.customUserAgent = nil; v.reload() }
        saveLater()
    }

    // MARK: Split view

    /// The two tabs shown together (first, second), or nil.
    private(set) var split: (String, String)?
    private var splitView: SplitView?

    func inSplit(_ tab: Tab?) -> Bool { guard let tab, case let (a, b)? = split else { return false }; return tab.id == a || tab.id == b }
    func splitPartner(_ tab: Tab) -> Tab? {
        guard case let (a, b)? = split else { return nil }
        return tab.id == a ? self.tab(b) : tab.id == b ? self.tab(a) : nil
    }

    /// [with] beside [tab], the one you're on first. Both must be websites in the same space.
    func startSplit(_ tab: Tab, with other: Tab) {
        if tab === other || tab.space != other.space || tab.isHome || other.isHome { return }
        split = (tab.id, other.id)
        activate(tab)
    }

    /// The pane you're on grows over the other one, then the split is gone.
    func endSplit() {
        guard let pair = split, let keep = active else { return }
        if let sv = splitView, sv.window != nil, !inBackground {
            sv.leave(keeping: pair.0 == keep.id ? 0 : 1) { [weak self] in
                guard let self, let now = self.split, now == pair else { return }
                self.finishSplit(keep)
            }
        } else { finishSplit(keep) }
    }

    private func finishSplit(_ keep: Tab) {
        splitPartner(keep)?.view?.removeFromSuperview()
        split = nil
        dropSplitView()
        show(keep)
        enforceBudget()
        host?.tabsChanged()
    }

    private func dropSplitView() { splitView?.removeFromSuperview(); splitView = nil }

    private func focusPane(_ tab: Tab) {
        activeSpace.activeTab = tab.id
        tab.lastUsed = Date().timeIntervalSince1970
        splitView?.activeIndex = split?.0 == tab.id ? 0 : 1
        host?.activeChanged()
        saveLater()
    }

    // MARK: Memory

    private let liveExtra = 2

    /// Keeps at most the active tab plus the two used most recently live; older ones rest.
    private func enforceBudget() {
        let act = active
        tabs.filter { $0.view != nil && $0 !== act && !$0.playing && !inSplit($0) }
            .sorted { $0.lastUsed > $1.lastUsed }
            .dropFirst(liveExtra)
            .forEach { rest($0) }
    }

    func rest(_ tab: Tab) {
        guard let v = tab.view else { return }
        if tab.playing || (tab === active && host != nil && !inBackground) || (inSplit(tab) && !inBackground) { return }
        tab.saved = v.interactionState
        destroyView(tab)
        host?.tabsChanged()
    }

    /// Low memory: every tab but the one you're on rests.
    func trim() {
        let act = active
        tabs.filter { $0.view != nil && $0 !== act && !$0.playing && !inSplit($0) }.forEach { rest($0) }
    }

    private func destroyView(_ tab: Tab) {
        guard let v = tab.view else { return }
        tab.view = nil
        tab.playing = false
        delegates[tab.id]?.detach(v)
        delegates[tab.id] = nil
        v.removeFromSuperview()
        v.stopLoading()
        v.configuration.userContentController.removeAllScriptMessageHandlers()
    }

    func onBackground() { inBackground = true; save() }
    func onForeground() { inBackground = false }

    // MARK: Saving

    func saveLater() {
        saveTimer?.invalidate()
        saveTimer = Timer.scheduledTimer(withTimeInterval: 2, repeats: false) { [weak self] _ in self?.save() }
    }

    func save() {
        saveTimer?.invalidate()
        store.save(Store.State(spaces: spaces, tabs: tabs, activeSpace: activeSpaceId == Browser.privateSpace ? lastSpaceId : activeSpaceId,
                               history: history, bookmarks: bookmarks, readLater: readLater, closed: recentlyClosed, groups: groups, jars: jars))
    }

    fileprivate func remember(_ url: String, _ title: String) {
        if !Navigation.isWeb(url) { return }
        let was = history.first { $0.url == url }
        history.removeAll { $0.url == url }
        history.insert(HistoryEntry(url: url, title: String(title.prefix(1024)), visits: (was?.visits ?? 0) + 1), at: 0)
        if history.count > 500 { history.removeLast(history.count - 500) }
        saveLater()
    }

    /// The sites you visit most (desktop's TopSites): history grouped by origin, scored by visits, newest first on a tie.
    func topSites(_ count: Int) -> [HistoryEntry] {
        let groups = Dictionary(grouping: history.filter { Navigation.isWeb($0.url) }) { Domains.origin($0.url) }
        let scored: [(HistoryEntry, Int)] = groups.values.map { (pages: [HistoryEntry]) -> (HistoryEntry, Int) in
            (pages.max { $0.at < $1.at }!, pages.reduce(0) { $0 + max(1, $1.visits) })
        }
        let sorted = scored.sorted { (a: (HistoryEntry, Int), b: (HistoryEntry, Int)) -> Bool in
            if a.1 != b.1 { return a.1 > b.1 }
            if a.0.at != b.0.at { return a.0.at > b.0.at }
            return a.0.url < b.0.url
        }
        return sorted.prefix(count).map { $0.0 }
    }

    /// Takes a site off the home page: its pages leave history.
    func forgetSite(_ url: String) {
        let o = Domains.origin(url)
        history.removeAll { Domains.origin($0.url) == o }
        saveLater()
    }

    /// Settings → Clear browsing data. History goes by [since] (nil: all of it); website data by date too.
    func clearData(since: Date?, history clearHistory: Bool, cookies: Bool, cache: Bool) {
        if clearHistory {
            if let since { history.removeAll { $0.at >= since.timeIntervalSince1970 } } else { history.removeAll(); recentlyClosed.removeAll() }
            saveLater()
        }
        var types = Set<String>()
        if cookies { types.formUnion([WKWebsiteDataTypeCookies, WKWebsiteDataTypeLocalStorage, WKWebsiteDataTypeSessionStorage, WKWebsiteDataTypeIndexedDBDatabases, WKWebsiteDataTypeWebSQLDatabases, WKWebsiteDataTypeServiceWorkerRegistrations]) }
        if cache { types.formUnion([WKWebsiteDataTypeDiskCache, WKWebsiteDataTypeMemoryCache, WKWebsiteDataTypeFetchCache]) }
        guard !types.isEmpty else { return }
        let from = since ?? Date(timeIntervalSince1970: 0)
        WKWebsiteDataStore.default().removeData(ofTypes: types, modifiedSince: from) {}
        for jar in jars { if let u = UUID(uuidString: jar.id) { WKWebsiteDataStore(forIdentifier: u).removeData(ofTypes: types, modifiedSince: from) {} } }
    }

    /// Cookies and stored data for the tab's site, then a reload.
    func clearSiteData(_ tab: Tab) {
        let host = tab.host
        guard !host.isEmpty else { return }
        let site = Domains.registrable(host)
        let store = dataStore(for: tab)
        store.fetchDataRecords(ofTypes: WKWebsiteDataStore.allWebsiteDataTypes()) { records in
            let mine = records.filter { $0.displayName == site || Domains.registrable($0.displayName) == site }
            store.removeData(ofTypes: WKWebsiteDataStore.allWebsiteDataTypes(), for: mine) { tab.view?.reload() }
        }
    }

    // MARK: Site text size

    func applyZoom(_ v: WKWebView, host: String) {
        let z = CGFloat(Prefs.shared.zoom(for: host)) / 100
        if v.pageZoom != z { v.pageZoom = z }
    }

    func setZoom(_ tab: Tab, _ percent: Int) {
        let host = tab.host
        guard !host.isEmpty else { return }
        Prefs.shared.setZoom(host, percent)
        for t in tabs where t.host == host { if let v = t.view { applyZoom(v, host: host) } }
    }

    /// After a theme change: flip open game pages in place.
    func themeGames(dark: Bool) {
        let script = String(format: SchemeHandler.themeScript, dark ? "true" : "false")
        for t in tabs where Navigation.isGame(t.url) { t.view?.evaluateJavaScript(script) }
    }

    /// Ads on or off for the site: pages there reload with or without the list.
    func setAdsAllowed(_ tab: Tab, _ allowed: Bool) {
        Blocker.shared.setAllowed(tab.host, allowed)
        let site = Domains.registrable(tab.host)
        for t in tabs where Domains.registrable(t.host) == site {
            if let v = t.view { Blocker.shared.apply(v.configuration.userContentController, host: t.host); v.reload() }
        }
    }

    func applyBlockerEverywhere() {
        for t in tabs { if let v = t.view { Blocker.shared.apply(v.configuration.userContentController, host: t.host); v.reload() } }
    }

    // MARK: Web views

    private lazy var loginScripts: String = ["login", "totp", "passkey"].compactMap { name in
        Bundle.main.url(forResource: name, withExtension: "js", subdirectory: "Shared/scripts").flatMap { try? String(contentsOf: $0, encoding: .utf8) }
    }.joined(separator: "\n")

    func configuration(for tab: Tab) -> WKWebViewConfiguration {
        let c = WKWebViewConfiguration()
        c.websiteDataStore = dataStore(for: tab)
        c.setURLSchemeHandler(SchemeHandler.shared, forURLScheme: "mica")
        c.allowsInlineMediaPlayback = true
        c.allowsPictureInPictureMediaPlayback = true
        c.mediaTypesRequiringUserActionForPlayback = .audio
        c.preferences.javaScriptCanOpenWindowsAutomatically = false
        c.preferences.isFraudulentWebsiteWarningEnabled = true
        c.defaultWebpagePreferences.preferredContentMode = tab.desktop ? .desktop : .mobile
        c.dataDetectorTypes = []
        c.upgradeKnownHostsToHTTPS = true
        return c
    }

    @discardableResult
    func createView(_ tab: Tab, configuration given: WKWebViewConfiguration? = nil) -> WKWebView {
        let c = given ?? configuration(for: tab)
        let ucc = c.userContentController
        if given == nil {
            let proxy = MessageProxy(self)
            ucc.add(proxy, name: "micaMedia")
            ucc.add(proxy, name: "micaReader")
            ucc.addUserScript(WKUserScript(source: Browser.mediaScript, injectionTime: .atDocumentStart, forMainFrameOnly: false))
            // Login forms, submitted passwords and 2FA boxes (desktop's scripts); never in private tabs.
            if !tab.isPrivate && Prefs.shared.micaPasswords {
                ucc.add(proxy, name: "micaLogin")
                ucc.addUserScript(WKUserScript(source: Browser.loginShim + loginScripts, injectionTime: .atDocumentStart, forMainFrameOnly: true))
            }
        }
        let v = PageView(frame: .zero, configuration: c)
        v.tab = tab
        tab.view = v
        v.isOpaque = false
        v.backgroundColor = Mica.canvas
        v.scrollView.backgroundColor = Mica.canvas
        v.allowsBackForwardNavigationGestures = true
        v.allowsLinkPreview = false
        v.isFindInteractionEnabled = true
        if #available(iOS 16.4, *) { v.isInspectable = false }
        let d = PageDelegate(browser: self, tab: tab)
        delegates[tab.id] = d
        d.attach(v)
        Blocker.shared.apply(ucc, host: Domains.hostOf(tab.url))
        return v
    }

    // MARK: Saved sessions

    /// Every website tab outside private browsing, for Sessions.save.
    func sessionTabs() -> [Tab] { tabs.filter { !$0.isPrivate && Navigation.isWeb($0.url) } }

    /// A saved session comes back beside what's open, never instead of it: one folded group per space, named
    /// after the session, its tabs resting until you open them.
    @discardableResult
    func restore(_ session: Sessions.Saved) -> Int {
        var n = 0
        var used = Set(groups.map { $0.color })
        let fallback = spaces.first { $0.id != Browser.privateSpace }?.id ?? activeSpaceId
        let pages = session.tabs.filter { Navigation.isWeb($0.url) }
        let bySpace = Dictionary(grouping: pages) { p in spaces.contains { $0.id == p.space && p.space != Browser.privateSpace } ? p.space : fallback }
        for (spaceId, list) in bySpace.sorted(by: { $0.key < $1.key }) {
            let color = Mica.palette.first { !used.contains($0) } ?? Mica.palette[groups.count % Mica.palette.count]
            used.insert(color)
            let g = TabGroup(id: "g" + String(Int(Date().timeIntervalSince1970 * 1000), radix: 36) + String(n), name: session.name, color: color, space: spaceId, collapsed: true)
            groups.append(g)
            for p in list {
                let t = Tab(space: spaceId, url: p.url, title: p.title)
                t.group = g.id
                t.lastUsed = Date().timeIntervalSince1970 - 60
                tabs.append(t)
                n += 1
            }
        }
        normalizeGroups()
        host?.tabsChanged()
        saveLater()
        return n
    }

    // MARK: Peek

    /// The page inside a peek (desktop's BrowserPeek): ads blocked as in tabs, but no windows or apps, and it only
    /// goes to another page when you tap. Peeking from a private tab stays private.
    func peekView(source: Tab) -> WKWebView {
        let c = configuration(for: source)
        let v = WKWebView(frame: .zero, configuration: c)
        v.isOpaque = false
        v.backgroundColor = Mica.canvas
        v.allowsLinkPreview = false
        Blocker.shared.apply(c.userContentController, host: "")
        return v
    }

    // Reports whether audible media plays in a frame. Muted autoplay (previews, ads) doesn't count.
    static let mediaScript = """
    (()=>{if(window.__micaMedia)return;window.__micaMedia=1;let last='0';const send=m=>{try{webkit.messageHandlers.micaMedia.postMessage(m)}catch(e){}};
    const post=()=>{let on='0';try{for(const m of document.querySelectorAll('video,audio'))if(!m.paused&&!m.ended&&!m.muted&&m.volume>0&&m.readyState>2){on='1';break}}catch(e){}
    if(on!==last){last=on;send(on)}};
    for(const t of ['play','playing','pause','ended','volumechange','emptied'])document.addEventListener(t,()=>setTimeout(post,0),true);
    addEventListener('pagehide',()=>{if(last==='1'){last='0';send('0')}});})()
    """

    /// The shared login scripts post to micaLogin, as on Android.
    static let loginShim = "window.micaLogin=window.micaLogin||{postMessage:m=>{try{webkit.messageHandlers.micaLogin.postMessage(String(m))}catch(e){}}};\n"

    // The page's own colour for the island and Spotlight glass: theme-color, else the body background.
    static let tintScript = """
    (()=>{const m=document.querySelector('meta[name="theme-color"]');let c=m&&m.content;
    if(!c||c==='transparent'){const b=document.body;c=b?getComputedStyle(b).backgroundColor:''}return c||''})()
    """

    fileprivate func received(_ message: WKScriptMessage) {
        guard let v = message.webView, let tab = tab(of: v) else { return }
        let body = message.body as? String ?? ""
        switch message.name {
        case "micaMedia":
            let was = tab.playing
            tab.playing = body == "1"
            if was != tab.playing { host?.tabsChanged() }
        case "micaReader":
            if message.frameInfo.isMainFrame, message.frameInfo.request.url?.scheme == "mica", body.count < 100 { host?.readerMessage(tab, body) }
        case "micaLogin":
            let origin = message.frameInfo.securityOrigin
            if message.frameInfo.isMainFrame, origin.protocol == "https", body.count < 80_000 {
                host?.loginMessage(tab, origin: "https://" + origin.host + (origin.port > 0 && origin.port != 443 ? ":\(origin.port)" : ""), json: body)
            }
        default: break
        }
    }

    // MARK: Page events

    fileprivate func committed(_ tab: Tab, _ v: WKWebView) {
        if let u = v.url?.absoluteString { tab.url = u }
        applyZoom(v, host: tab.host)
        tab.tint = nil
        host?.pageChanged(tab)
    }

    fileprivate func finished(_ tab: Tab, _ v: WKWebView, failed: Bool) {
        if let u = v.url?.absoluteString { tab.url = u }
        if let t = v.title, !t.isEmpty, !t.hasPrefix("http") { tab.title = t }
        if !failed {
            if tab.url.hasPrefix("https://") { tab.upgraded = nil }
            if !tab.isPrivate { remember(tab.url, tab.title) }
            if v.themeColor == nil {
                v.evaluateJavaScript(Browser.tintScript) { [weak self] raw, _ in
                    tab.tint = UIColor.css(raw as? String)
                    self?.host?.pageChanged(tab)
                }
            }
            Favicons.fetch(for: tab)
        }
        host?.pageChanged(tab)
        saveLater()
    }

    fileprivate func processGone(_ tab: Tab, _ v: WKWebView) {
        // The page's process was killed (memory) or crashed: drop the view, reload the tab when shown.
        guard tab.view === v else { return }
        destroyView(tab)
        if tab === active || inSplit(tab) { show(active) }
    }

    /// A window the page opened from a tap: a new tab right after it, on the configuration WebKit gave.
    fileprivate func openWindow(from tab: Tab, configuration: WKWebViewConfiguration) -> WKWebView {
        let child = Tab(space: tab.space, url: "")
        child.parent = tab.id
        child.desktop = tab.desktop
        child.jar = tab.jar
        let at = (tabs.firstIndex { $0 === tab } ?? tabs.count - 1) + 1
        tabs.insert(child, at: min(at, tabs.count))
        let v = createView(child, configuration: configuration)
        activate(child)
        return v
    }

    fileprivate func closeWindow(_ v: WKWebView) { if let t = tab(of: v) { close(t) } }

    var isActiveOrShown: (Tab) -> Bool { { [weak self] t in t === self?.active || self?.inSplit(t) == true } }
}

/// WKUserContentController keeps its handlers; this keeps the browser from being kept by them.
private final class MessageProxy: NSObject, WKScriptMessageHandler {
    weak var browser: Browser?
    init(_ b: Browser) { browser = b }
    func userContentController(_ c: WKUserContentController, didReceive message: WKScriptMessage) { browser?.received(message) }
}

/// A tab's web view.
final class PageView: WKWebView {
    weak var tab: Tab?
}

/// A tab's navigation and UI delegate (android Browser.Client and Browser.Chrome).
private final class PageDelegate: NSObject, WKNavigationDelegate, WKUIDelegate {
    weak var browser: Browser?
    weak var tab: Tab?
    private var observations: [NSKeyValueObservation] = []
    private var lastY: CGFloat = 0
    /// The page shown in place of one that failed, so its finish isn't remembered as a visit.
    private var failedURL: String?
    private var dialogTimes: [Date] = []
    private var dialogsMuted = false

    init(browser: Browser, tab: Tab) { self.browser = browser; self.tab = tab }

    func attach(_ v: WKWebView) {
        v.navigationDelegate = self
        v.uiDelegate = self
        observations = [
            v.observe(\.estimatedProgress) { [weak self] v, _ in
                guard let self, let tab = self.tab else { return }
                tab.progress = v.isLoading ? v.estimatedProgress : 1
                self.browser?.host?.progressChanged(tab)
            },
            v.observe(\.isLoading) { [weak self] v, _ in
                guard let self, let tab = self.tab else { return }
                tab.progress = v.isLoading ? max(0.05, v.estimatedProgress) : 1
                self.browser?.host?.progressChanged(tab)
            },
            v.observe(\.title) { [weak self] v, _ in
                guard let self, let tab = self.tab, let t = v.title, !t.isEmpty, !t.hasPrefix("http"), self.failedURL == nil else { return }
                tab.title = t
                self.browser?.host?.pageChanged(tab)
            },
            v.observe(\.url) { [weak self] v, _ in
                guard let self, let tab = self.tab, let u = v.url?.absoluteString else { return }
                tab.url = u
                self.browser?.host?.pageChanged(tab)
            },
            v.observe(\.themeColor) { [weak self] v, _ in
                guard let self, let tab = self.tab, let c = v.themeColor else { return }
                tab.tint = c
                self.browser?.host?.pageChanged(tab)
            },
            v.scrollView.observe(\.contentOffset) { [weak self] s, _ in
                guard let self, let tab = self.tab, let b = self.browser, tab === b.active, s.isTracking || s.isDecelerating else { return }
                let y = s.contentOffset.y + s.adjustedContentInset.top
                b.host?.scrolled(y - self.lastY, atTop: y <= 0)
                self.lastY = y
            },
        ]
    }

    func detach(_ v: WKWebView) {
        observations.forEach { $0.invalidate() }
        observations = []
        v.navigationDelegate = nil
        v.uiDelegate = nil
    }

    // MARK: Navigation

    func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction, preferences: WKWebpagePreferences,
                 decisionHandler: @escaping (WKNavigationActionPolicy, WKWebpagePreferences) -> Void) {
        guard let url = action.request.url, let browser, let tab else { return decisionHandler(.cancel, preferences) }
        let address = url.absoluteString
        let scheme = url.scheme?.lowercased() ?? ""
        let main = action.targetFrame?.isMainFrame ?? true
        let tapped = action.navigationType == .linkActivated
        preferences.preferredContentMode = tab.desktop ? .desktop : .mobile

        if scheme == "mica" {
            if address == Navigation.home && main { decisionHandler(.cancel, preferences); browser.navigate(tab, Navigation.home); return }
            if main { Blocker.shared.apply(webView.configuration.userContentController, host: "") }
            return decisionHandler(.allow, preferences)
        }
        let host = url.host?.lowercased() ?? ""
        if Navigation.isMicaHost(host) {
            decisionHandler(.cancel, preferences)
            // "Continue without HTTPS" on Mica's error page: only from a tap, and only to a plain web address.
            if host == Https.continueHost {
                if let plain = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems?.first(where: { $0.name == "u" })?.value,
                   plain.lowercased().hasPrefix("http://"), let u = URL(string: plain) {
                    Https.allow(Domains.hostOf(plain)); tab.upgraded = nil; webView.load(URLRequest(url: u))
                }
                return
            }
            let mapped = Navigation.fromEngine(address)
            if mapped != address, let u = URL(string: mapped) { webView.load(URLRequest(url: u)) }
            return
        }
        if scheme == "http" && main {
            // The secure address sent you back to the plain one: the site has no HTTPS, so stop trying.
            if let was = tab.upgraded, action.navigationType == .other, Domains.registrable(Domains.hostOf(was)) == Domains.registrable(host), webView.url?.scheme == "https" {
                Https.allow(host); tab.upgraded = nil
                browser.host?.announce(L("no_https_redirect", host))
            } else if let secure = Https.upgrade(address), let u = URL(string: secure) {
                decisionHandler(.cancel, preferences)
                tab.upgraded = address
                webView.load(URLRequest(url: u))
                return
            }
        }
        if ["http", "https", "about", "data", "blob", "javascript"].contains(scheme) {
            if main { Blocker.shared.apply(webView.configuration.userContentController, host: host) }
            return decisionHandler(.allow, preferences)
        }
        decisionHandler(.cancel, preferences)
        // Apps (mail, phone, App Store, …) only open from a tap, so a page can't bounce you into one on its own.
        if tapped || action.navigationType == .formSubmitted { browser.host?.openExternal(url) }
    }

    func webView(_ webView: WKWebView, decidePolicyFor response: WKNavigationResponse, decisionHandler: @escaping (WKNavigationResponsePolicy) -> Void) {
        if let http = response.response as? HTTPURLResponse,
           let cd = http.value(forHTTPHeaderField: "Content-Disposition"), cd.lowercased().hasPrefix("attachment") {
            return decisionHandler(.download)
        }
        decisionHandler(response.canShowMIMEType ? .allow : .download)
    }

    func webView(_ webView: WKWebView, navigationAction: WKNavigationAction, didBecome download: WKDownload) { Downloads.shared.track(download) }
    func webView(_ webView: WKWebView, navigationResponse: WKNavigationResponse, didBecome download: WKDownload) { Downloads.shared.track(download) }

    func webView(_ webView: WKWebView, didStartProvisionalNavigation navigation: WKNavigation!) {
        failedURL = nil
        dialogsMuted = false
        dialogTimes = []
    }

    func webView(_ webView: WKWebView, didCommit navigation: WKNavigation!) {
        guard let tab, let browser else { return }
        browser.committed(tab, webView)
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        guard let tab, let browser else { return }
        browser.finished(tab, webView, failed: failedURL != nil)
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) {
        let e = error as NSError
        // A navigation that was replaced or cancelled, or handed to a download, isn't an error you need to see.
        if e.code == NSURLErrorCancelled || (e.domain == "WebKitErrorDomain" && (e.code == 102 || e.code == 101)) { return }
        guard let tab, let browser else { return }
        let failing = (e.userInfo[NSURLErrorFailingURLStringErrorKey] as? String) ?? webView.url?.absoluteString ?? tab.url
        let insecure: String? = tab.upgraded.flatMap { was in Domains.hostOf(was) == Domains.hostOf(failing) ? was : nil }
        let offline = e.code == NSURLErrorNotConnectedToInternet || e.code == NSURLErrorDataNotAllowed
        let description = e.localizedDescription
        failedURL = failing
        tab.title = L(offline ? "error_offline_title" : insecure != nil ? "no_https_title" : "error_title")
        let html = ErrorPage.html(url: failing, code: e.code, description: description, offline: offline, insecure: insecure, dark: SchemeHandler.dark)
        webView.loadHTMLString(html, baseURL: URL(string: failing))
        browser.host?.pageChanged(tab)
    }

    func webView(_ webView: WKWebView, didReceive challenge: URLAuthenticationChallenge, completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void) {
        let method = challenge.protectionSpace.authenticationMethod
        guard method == NSURLAuthenticationMethodHTTPBasic || method == NSURLAuthenticationMethodHTTPDigest else {
            return completionHandler(.performDefaultHandling, nil)
        }
        guard let tab, let browser, browser.isActiveOrShown(tab), let host = browser.host, challenge.previousFailureCount < 3 else {
            return completionHandler(.cancelAuthenticationChallenge, nil)
        }
        host.httpAuth(tab, host: challenge.protectionSpace.host) { credential in
            completionHandler(credential == nil ? .cancelAuthenticationChallenge : .useCredential, credential)
        }
    }

    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        guard let tab, let browser else { return }
        browser.processGone(tab, webView)
    }

    // MARK: UI

    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration, for action: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        guard let tab, let browser else { return nil }
        return browser.openWindow(from: tab, configuration: configuration)
    }

    func webViewDidClose(_ webView: WKWebView) { browser?.closeWindow(webView) }

    func webView(_ webView: WKWebView, contextMenuConfigurationForElement elementInfo: WKContextMenuElementInfo,
                 completionHandler: @escaping (UIContextMenuConfiguration?) -> Void) {
        guard let tab, let link = elementInfo.linkURL, let host = browser?.host else { return completionHandler(nil) }
        completionHandler(host.linkMenu(tab, link: link))
    }

    func webView(_ webView: WKWebView, requestMediaCapturePermissionFor origin: WKSecurityOrigin, initiatedByFrame frame: WKFrameInfo,
                 type: WKMediaCaptureType, decisionHandler: @escaping (WKPermissionDecision) -> Void) {
        guard let tab, let browser, let host = browser.host, browser.isActiveOrShown(tab) else { return decisionHandler(.deny) }
        let o = origin.protocol + "://" + origin.host + (origin.port > 0 ? ":\(origin.port)" : "")
        let kind: String = type == .camera ? "camera" : type == .microphone ? "microphone" : "camera_microphone"
        host.sitePermission(tab, origin: o, kind: kind) { decisionHandler($0 ? .grant : .deny) }
    }

    // The page's own dialogs, as Mica cards: never from a tab you can't see, and at most 3 in 10 s.
    private func dialog(_ frame: WKFrameInfo, _ kind: DialogKind, _ message: String, _ def: String?, _ answer: @escaping (Bool, String?) -> Void) {
        guard let tab, let browser, let host = browser.host, !dialogsMuted else { return answer(false, nil) }
        let origin = frame.securityOrigin.host
        if !browser.isActiveOrShown(tab) { host.announce(L("dialog_background", origin)); return answer(false, nil) }
        let now = Date()
        dialogTimes = dialogTimes.filter { now.timeIntervalSince($0) < 10 } + [now]
        if dialogTimes.count > 3 { dialogsMuted = true; host.announce(L("dialogs_stopped", origin)); return answer(false, nil) }
        host.pageDialog(tab, kind: kind, origin: origin, message: message, defaultText: def, answer: answer)
    }

    func webView(_ webView: WKWebView, runJavaScriptAlertPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping () -> Void) {
        dialog(frame, .alert, message, nil) { _, _ in completionHandler() }
    }

    func webView(_ webView: WKWebView, runJavaScriptConfirmPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping (Bool) -> Void) {
        dialog(frame, .confirm, message, nil) { ok, _ in completionHandler(ok) }
    }

    func webView(_ webView: WKWebView, runJavaScriptTextInputPanelWithPrompt prompt: String, defaultText: String?, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping (String?) -> Void) {
        dialog(frame, .prompt, prompt, defaultText) { ok, text in completionHandler(ok ? (text ?? "") : nil) }
    }
}
