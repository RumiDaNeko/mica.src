import WebKit

/// The ad and tracker blocker. Its lists (the same ones desktop and Android use) are converted at build time to one
/// WebKit content-blocker list (ios/tools/blocker-rules.js), compiled once on the device and kept by WebKit; WebKit
/// then blocks inside its own network process, so Mica never sees the requests (and can't count them, unlike Android).
/// Sites where you allow ads simply get pages without the list.
final class Blocker {
    static let shared = Blocker()

    private(set) var list: WKContentRuleList?
    private var waiting: [WeakController] = []
    private(set) var meta: (rules: Int, lists: Int) = (0, 0)

    private struct WeakController { weak var controller: WKUserContentController?; let host: () -> String }

    private var identifier: String {
        let build = Bundle.main.infoDictionary?["CFBundleVersion"] as? String ?? "0"
        return "mica-blocker-\(build)-\(meta.rules)"
    }

    func load() {
        if let u = Bundle.main.url(forResource: "meta", withExtension: "json", subdirectory: "Blocker"),
           let d = try? Data(contentsOf: u), let o = try? JSONSerialization.jsonObject(with: d) as? [String: Int] {
            meta = (o["rules"] ?? 0, o["lists"] ?? 0)
        }
        guard let store = WKContentRuleListStore.default() else { return }
        let id = identifier
        store.lookUpContentRuleList(forIdentifier: id) { [weak self] found, _ in
            if let found { self?.ready(found); return }
            // First start, or a new build: compile it (a few seconds, off the main thread inside WebKit) and drop old ones.
            store.getAvailableContentRuleListIdentifiers { ids in
                for old in ids ?? [] where old != id { store.removeContentRuleList(forIdentifier: old) { _ in } }
            }
            DispatchQueue.global(qos: .userInitiated).async {
                guard let u = Bundle.main.url(forResource: "rules", withExtension: "json", subdirectory: "Blocker"),
                      let json = try? String(contentsOf: u, encoding: .utf8) else { return }
                store.compileContentRuleList(forIdentifier: id, encodedContentRuleList: json) { list, error in
                    if let error { NSLog("Mica blocker: %@", error.localizedDescription) }
                    if let list { DispatchQueue.main.async { self?.ready(list) } }
                }
            }
        }
    }

    private func ready(_ list: WKContentRuleList) {
        self.list = list
        for w in waiting { if let c = w.controller { apply(c, host: w.host()) } }
        waiting.removeAll()
    }

    func isAllowed(_ host: String) -> Bool {
        !host.isEmpty && Prefs.shared.allowedSites.contains(Domains.registrable(host))
    }

    /// Puts the list on (or takes it off) a page's content controller for the site it's showing.
    func apply(_ controller: WKUserContentController, host: String) {
        guard let list else {
            if !waiting.contains(where: { $0.controller === controller }) { waiting.append(WeakController(controller: controller, host: { host })) }
            return
        }
        controller.remove(list)
        if Prefs.shared.blockerOn && !isAllowed(host) && !Navigation.isMicaHost(host) { controller.add(list) }
    }

    func setAllowed(_ host: String, _ allowed: Bool) {
        var all = Prefs.shared.allowedSites
        let site = Domains.registrable(host)
        if allowed { all.insert(site) } else { all.remove(site) }
        Prefs.shared.allowedSites = all
    }
}
