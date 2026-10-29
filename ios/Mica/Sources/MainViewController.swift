import UIKit
import WebKit

/// The one screen (android MainActivity): the page edge to edge, the island over it, and Spotlight, the rail, the deck,
/// peek and Mica's cards on top when called for.
final class MainViewController: UIViewController, BrowserHost {
    let webHost = UIView()
    let island = Island()
    private let home = HomeView()
    private let spotlight = Spotlight()
    private let rail = Rail()
    private let deck = Deck()
    private lazy var peek = Peek(main: self)
    private var islandBottom: NSLayoutConstraint!
    var browser: Browser { Browser.shared }

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = Mica.canvas
        // The page scrolls clear of the island.
        additionalSafeAreaInsets.bottom = 52

        for v in [webHost, home] as [UIView] {
            view.addSubview(v)
            v.translatesAutoresizingMaskIntoConstraints = false
            NSLayoutConstraint.activate([
                v.leadingAnchor.constraint(equalTo: view.leadingAnchor),
                v.trailingAnchor.constraint(equalTo: view.trailingAnchor),
                v.topAnchor.constraint(equalTo: view.topAnchor),
                v.bottomAnchor.constraint(equalTo: view.bottomAnchor),
            ])
        }
        home.onOpen = { [weak self] in self?.navigateActive($0) }
        home.onSpotlight = { [weak self] in self?.openSpotlight() }
        home.onForget = { [weak self] e in self?.browser.forgetSite(e.url); self?.renderHome() }

        view.addSubview(island)
        island.translatesAutoresizingMaskIntoConstraints = false
        islandBottom = island.bottomAnchor.constraint(equalTo: view.safeAreaLayoutGuide.bottomAnchor, constant: 44)
        let width = island.widthAnchor.constraint(equalToConstant: 160)
        island.widthConstraint = width
        NSLayoutConstraint.activate([
            island.centerXAnchor.constraint(equalTo: view.centerXAnchor),
            islandBottom, width,
            island.heightAnchor.constraint(equalToConstant: Island.height),
        ])
        island.onTap = { [weak self] in self?.openSpotlight() }
        island.onSpaceDot = { [weak self] in self?.rail.show() }
        island.onSwipe = { [weak self] dir in self?.swipeTab(dir) }
        island.onSwipeUp = { [weak self] in self?.deck.show() }
        island.onLongPress = { [weak self] in self?.pageMenu() }

        for v in [spotlight, rail, deck] as [UIView] {
            view.addSubview(v)
            v.pin(to: view)
        }
        spotlight.main = self
        rail.main = self
        deck.main = self

        let edge = UIScreenEdgePanGestureRecognizer(target: self, action: #selector(edgePanned(_:)))
        edge.edges = .left
        view.addGestureRecognizer(edge)

        Downloads.shared.announce = { [weak self] in self?.island.announce($0) }
        SchemeHandler.dark = traitCollection.userInterfaceStyle == .dark
        browser.attach(self)
        refreshChrome()
        hookAccount()
    }

    // MARK: Account

    private var signInTab: Tab?
    private var remindedAt: String?
    private var sentTabsTimer: Timer?

    private func hookAccount() {
        let account = Account.shared
        account.currentDark = { [weak self] in
            let t = Prefs.shared.theme
            return t == .dark || (t == .system && self?.traitCollection.userInterfaceStyle == .dark)
        }
        account.applyDark = { [weak self] dark in
            Prefs.shared.theme = dark ? .dark : .light
            self?.view.window?.overrideUserInterfaceStyle = Prefs.shared.theme.style
        }
        account.onSignedIn = { [weak self] u, e in
            guard let self else { return }
            if let u { if Sheet.isShowing { Sheet.dismissCurrent() }; self.island.announce(L("signed_in_as", u.username)) }
            else if let e { Sheet.form(L("sign_in_failed"), detail: e.localizedDescription, content: [], buttons: [Sheet.Button(title: L("ok"), primary: true) { true }], in: self.view) }
        }
        account.onTabsArrived = { [weak self] in self?.tabsArrived($0) }
        account.pullSettings()
        account.checkSentTabs()
        sentTabsTimer = Timer.scheduledTimer(withTimeInterval: 120, repeats: true) { _ in
            if UIApplication.shared.applicationState == .active { Account.shared.checkSentTabs(force: false) }
        }
    }

    /// Discord opens in a Mica tab, with the code in a card to type (or paste) on Discord's page.
    func showSignInCode() {
        guard let s = Account.shared.signIn else { return }
        signInTab = browser.open(s.url)
        remindedAt = nil
        signInCard()
    }

    /// Forgot the code? Once the sign-in tab reaches the service's "type the code" page, the card shows again. Once
    /// per page, so closing it sticks.
    private func remindSignInCode(_ tab: Tab) {
        guard Account.shared.signIn != nil, tab === signInTab, !Sheet.isShowing, tab.progress >= 1,
              tab.host == URL(string: Api.base)?.host, remindedAt != tab.url else { return }
        remindedAt = tab.url
        signInCard()
    }

    private func signInCard() {
        guard let s = Account.shared.signIn else { return }
        Sheet.form(L("sign_in_code_title", s.code), detail: L("sign_in_code_detail"), content: [], buttons: [
            Sheet.Button(title: L("cancel")) { Account.shared.cancelSignIn(); return true },
            // Copies and closes; the sign-in tab shows it again if it's needed.
            Sheet.Button(title: L("copy"), primary: true) { [weak self] in
                UIPasteboard.general.string = s.code
                self?.island.announce(L("code_copied"))
                return true
            },
        ], in: view, closable: false)
    }

    private func tabsArrived(_ sent: [Account.SentTab]) {
        let added = sent.filter { t in Navigation.isWeb(t.url) && !browser.tabs.contains { $0.url == t.url } }
        guard let last = added.last else { return }
        for t in added { browser.open(t.url, activate: false).title = t.title.isEmpty ? Navigation.label(t.url) : t.title }
        let from = Array(NSOrderedSet(array: added.map { $0.from })).compactMap { $0 as? String }.joined(separator: ", ")
        let title = added.count == 1 ? L("tab_arrived", added[0].title.isEmpty ? added[0].url : added[0].title) : L("tabs_arrived", added.count)
        Sheet.form(title, detail: L("from_device", from), content: [], buttons: [
            Sheet.Button(title: L("close")) { true },
            Sheet.Button(title: L("open"), primary: true) { [weak self] in
                if let t = self?.browser.tabs.last(where: { $0.url == last.url }) { self?.browser.activate(t) }
                return true
            },
        ], in: view)
    }

    override func viewDidAppear(_ animated: Bool) {
        super.viewDidAppear(animated)
        if browser.active?.isHome == true && browser.tabs.count == 1 && browser.history.isEmpty { openSpotlight() }
    }

    override func traitCollectionDidChange(_ previous: UITraitCollection?) {
        super.traitCollectionDidChange(previous)
        let dark = traitCollection.userInterfaceStyle == .dark
        if dark != SchemeHandler.dark {
            SchemeHandler.dark = dark
            browser.themeGames(dark: dark)
        }
    }

    override var keyCommands: [UIKeyCommand]? {
        [UIKeyCommand(title: L("search_hint"), action: #selector(keyFocus), input: "l", modifierFlags: .command),
         UIKeyCommand(title: L("new_tab"), action: #selector(keyNewTab), input: "t", modifierFlags: .command),
         UIKeyCommand(title: L("close_tab"), action: #selector(keyClose), input: "w", modifierFlags: .command),
         UIKeyCommand(title: L("find_in_page"), action: #selector(keyFind), input: "f", modifierFlags: .command),
         UIKeyCommand(action: #selector(keyNext), input: "]", modifierFlags: [.command, .shift]),
         UIKeyCommand(action: #selector(keyPrevious), input: "[", modifierFlags: [.command, .shift])]
    }
    @objc private func keyFocus() { openSpotlight() }
    @objc private func keyNewTab() { newTab() }
    @objc private func keyClose() { if let t = browser.active { browser.close(t) } }
    @objc private func keyFind() { showFind() }
    @objc private func keyNext() { browser.step(1) }
    @objc private func keyPrevious() { browser.step(-1) }

    @objc private func edgePanned(_ g: UIScreenEdgePanGestureRecognizer) {
        if g.state == .began { rail.show() }
    }

    // MARK: Chrome

    func refreshChrome() {
        guard let tab = browser.active else { return }
        let list = browser.tabsIn()
        home.isHidden = !tab.isHome
        if tab.isHome { renderHome() }
        island.update(label: tab.isHome ? L("new_tab") : Navigation.label(tab.url), spaceColor: Mica.spaceColor(browser.activeSpace.color),
                      tabCount: list.count, activeIndex: list.firstIndex { $0 === tab } ?? 0, playing: browser.tabs.contains { $0.playing }, tint: tab.tint)
        island.setProgress(tab.isHome ? 1 : tab.progress)
        if rail.showing { rail.refresh() }
        if deck.showing { deck.refresh() }
    }

    private func renderHome() {
        guard let tab = browser.active, tab.isHome else { return }
        home.render(isPrivate: tab.isPrivate, top: browser.topSites(8), closedTabs: browser.recentlyClosed)
    }

    private func swipeTab(_ dir: Int) {
        let list = browser.tabsIn()
        guard list.count > 1 else { return }
        browser.step(dir)
        if let t = browser.active { island.slide(to: t.isHome ? L("new_tab") : Navigation.label(t.url), dir: dir) }
    }

    func openSpotlight(_ prefill: String? = nil) {
        rail.close(); deck.close()
        let current = browser.active.flatMap { $0.isHome ? nil : $0.url } ?? ""
        spotlight.show(prefill: prefill ?? current, from: island)
    }

    func spotlightClosed() {}

    // MARK: BrowserHost

    func activeChanged() { refreshChrome() }
    func tabsChanged() { refreshChrome() }

    func progressChanged(_ tab: Tab) {
        if tab === browser.active { island.setProgress(tab.progress) }
    }

    func pageChanged(_ tab: Tab) {
        if tab === browser.active { refreshChrome(); remindSignInCode(tab) }
    }

    func scrolled(_ dy: CGFloat, atTop: Bool) {
        if atTop || dy < -14 { island.setShrunk(false) } else if dy > 14 { island.setShrunk(true) }
    }

    func announce(_ text: String) { island.announce(text) }

    func openExternal(_ url: URL) {
        UIApplication.shared.open(url, options: [:]) { [weak self] ok in if !ok { self?.island.announce(L("no_app_for_link")) } }
    }

    func pageDialog(_ tab: Tab, kind: DialogKind, origin: String, message: String, defaultText: String?, answer: @escaping (Bool, String?) -> Void) {
        let title = L("page_says", origin)
        var content: [UIView] = [Sheet.text(String(message.prefix(1000)), size: 15, color: Mica.ink)]
        var field: UITextField?
        if kind == .prompt {
            let f = Sheet.field("", text: defaultText ?? "")
            content.append(f)
            field = f
        }
        var buttons: [Sheet.Button] = []
        if kind != .alert { buttons.append(Sheet.Button(title: L("cancel")) { answer(false, nil); return true }) }
        buttons.append(Sheet.Button(title: L("ok"), primary: true) { answer(true, field?.text); return true })
        Sheet.form(title, content: content, buttons: buttons, in: view, unanswered: { answer(false, nil) })
    }

    func httpAuth(_ tab: Tab, host: String, answer: @escaping (URLCredential?) -> Void) {
        let user = Sheet.field(L("username"))
        let pass = Sheet.field(L("password"), secure: true)
        let secure = tab.url.hasPrefix("https://")
        Sheet.form(L("http_auth_title"), detail: L(secure ? "http_auth_detail" : "http_auth_insecure", host), content: [user, pass], buttons: [
            Sheet.Button(title: L("cancel")) { answer(nil); return true },
            Sheet.Button(title: L("ok"), primary: true) {
                answer(URLCredential(user: user.text ?? "", password: pass.text ?? "", persistence: .forSession)); return true
            },
        ], in: view, unanswered: { answer(nil) })
    }

    func sitePermission(_ tab: Tab, origin: String, kind: String, answer: @escaping (Bool) -> Void) {
        let key = origin + "\t" + kind
        if let known = Prefs.shared.sitePermissions[key] { return answer(known) }
        let what = L("perm_" + kind)
        Sheet.form(L("site_wants", Domains.hostOf(origin)) + " " + what, content: [], buttons: [
            Sheet.Button(title: L("block")) { Prefs.shared.sitePermissions[key] = false; answer(false); return true },
            Sheet.Button(title: L("allow"), primary: true) { Prefs.shared.sitePermissions[key] = true; answer(true); return true },
        ], in: view, unanswered: { answer(false) })
    }

    func linkMenu(_ tab: Tab, link: URL) -> UIContextMenuConfiguration? {
        let url = link.absoluteString
        guard Navigation.isWeb(url) else { return nil }
        return UIContextMenuConfiguration(identifier: nil, previewProvider: nil) { [weak self] _ in
            guard let self else { return nil }
            var items: [UIMenuElement] = [
                UIAction(title: L("peek"), image: UIImage(systemName: "eye")) { [weak self] _ in self?.peek.show(from: tab, url: url) },
                UIAction(title: L("open_in_new_tab"), image: UIImage(systemName: "plus.square.on.square")) { [weak self] _ in self?.browser.open(url, parent: tab) },
                UIAction(title: L("open_in_background"), image: UIImage(systemName: "square.stack")) { [weak self] _ in
                    self?.browser.open(url, activate: false, parent: tab); self?.island.announce(Navigation.label(url))
                },
            ]
            if Navigation.isWeb(tab.url) && !self.browser.inSplit(tab) {
                items.append(UIAction(title: L("open_in_split"), image: UIImage(systemName: "rectangle.split.2x1")) { [weak self] _ in
                    guard let self else { return }
                    self.browser.startSplit(tab, with: self.browser.open(url, activate: false, parent: tab))
                })
            }
            items.append(UIAction(title: L("copy_link"), image: UIImage(systemName: "doc.on.doc")) { _ in UIPasteboard.general.url = link })
            items.append(UIAction(title: L("share_link"), image: UIImage(systemName: "square.and.arrow.up")) { [weak self] _ in self?.share(url) })
            return UIMenu(title: url, children: items)
        }
    }

    func readerMessage(_ tab: Tab, _ message: String) {
        if message.hasPrefix("size:"), let n = Int(message.dropFirst(5)) { Prefs.shared.readerSize = n }
        else if message.hasPrefix("serif:") { Prefs.shared.readerSerif = message.hasSuffix("true"); tab.view?.reload() }
        else if message == "original" { openOriginal(tab) }
        else if message == "save" {
            let url = browser.pageKey(tab)
            guard let article = Reader.load(Reader.id(tab.url)) else { return }
            if Reader.isSaved(url) { island.announce(L("reader_already_saved")); return }
            Reader.keep(article, saved: true)
            if !browser.isSavedForLater(url) {
                browser.readLater.insert(HistoryEntry(url: url, title: (article["title"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? tab.title), at: 0)
            }
            browser.saveLater()
            island.announce(L("saved_offline"))
            tab.view?.reload()
        }
    }

    func loginMessage(_ tab: Tab, origin: String, json: String) {
        Autofill.shared.received(tab, origin: origin, json: json, main: self)
    }

    // MARK: Opening things

    /// A link from another app (or the share sheet): a new tab, or the empty one you're on.
    func open(external url: URL) {
        var address = url.absoluteString
        var games = false
        if url.scheme == "mica-open", let inner = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems?.first(where: { $0.name == "u" })?.value {
            address = inner
            games = Navigation.isGame(inner)
        }
        guard Navigation.isWeb(address) || games else { return }
        spotlight.close(); rail.close(); deck.close(); Sheet.dismissCurrent()
        if let t = browser.active, t.isHome { browser.navigate(t, address) } else { browser.open(address) }
    }

    func navigateActive(_ url: String) {
        if let t = browser.active { browser.navigate(t, url) } else { browser.open(url) }
    }

    func openHit(_ hit: Navigation.Hit) {
        if hit.kind == .tab, let t = browser.tab(hit.tabId) { browser.activate(t); return }
        navigateActive(hit.url)
    }

    func newTab() {
        if let t = browser.active, t.isHome { openSpotlight(""); return }
        browser.open(Navigation.home)
        openSpotlight("")
    }

    func newPrivateTab() {
        browser.openPrivate()
        openSpotlight("")
    }

    func share(_ url: String) {
        guard let u = URL(string: url) else { return }
        let vc = UIActivityViewController(activityItems: [u], applicationActivities: nil)
        vc.popoverPresentationController?.sourceView = island
        vc.popoverPresentationController?.sourceRect = island.bounds
        present(vc, animated: true)
    }

    func sharePage() {
        guard let t = browser.active, !t.isHome else { return }
        share(browser.pageKey(t))
    }

    func toggleBookmark() {
        guard let t = browser.active, Navigation.isWeb(t.url) else { return }
        island.announce(L(browser.toggleBookmark(t) ? "bookmarked" : "bookmark_removed"))
    }

    func toggleReadLater() {
        guard let t = browser.active, Navigation.isWeb(t.url) || Reader.isReader(t.url) else { return }
        let url = browser.pageKey(t)
        if browser.isSavedForLater(url) {
            browser.readLater.removeAll { $0.url == url }
            Reader.forget(url)
            browser.saveLater()
            island.announce(L("removed_from_read_later"))
            return
        }
        browser.readLater.insert(HistoryEntry(url: url, title: t.title.isEmpty ? Navigation.label(url) : t.title), at: 0)
        browser.saveLater()
        island.announce(L("saved_for_later"))
        // A copy to read without a connection, when the page is an article.
        browser.readArticle(t) { article in if let article { Reader.keep(article, saved: true) } }
    }

    func openReader(_ tab: Tab) {
        browser.readArticle(tab) { [weak self] article in
            guard let self else { return }
            guard let article else { self.island.announce(L("not_an_article")); return }
            let address = Reader.keep(article, saved: false)
            if self.browser.tab(tab.id) != nil { self.browser.navigate(tab, address) }
        }
    }

    /// The page this reader tab came from: back if it's the page before, else load it.
    private func openOriginal(_ tab: Tab) {
        let url = browser.pageKey(tab)
        if let v = tab.view, v.backForwardList.backItem?.url.absoluteString == url { v.goBack() } else { browser.navigate(tab, url) }
    }

    /// A read-later entry: its saved copy (offline) when there is one, else the page.
    func openReadLater(_ url: String) { openFromLibrary(Reader.savedAddress(url) ?? url) }

    func openFromLibrary(_ url: String) {
        presentedViewController?.dismiss(animated: true)
        if let t = browser.active, t.isHome { browser.navigate(t, url) } else { browser.open(url) }
    }

    func showFind() {
        guard let v = browser.active?.view else { return }
        v.findInteraction?.presentFindNavigator(showingReplace: false)
    }

    func openLibrary() { present(UINavigationController(rootViewController: LibraryViewController(main: self)), animated: true) }
    func openSettings() { present(UINavigationController(rootViewController: SettingsViewController(main: self)), animated: true) }
    func openPasswords() { present(UINavigationController(rootViewController: PasswordsViewController(main: self)), animated: true) }

    // MARK: Page menu

    func pageMenu() {
        guard let tab = browser.active else { return }
        let v = tab.view
        let web = Navigation.isWeb(tab.url)
        var items: [Sheet.Item] = []
        func state(_ key: String, _ on: Bool) -> String { L(key) + ": " + L(on ? "on" : "off") }
        if v?.canGoForward == true { items.append(Sheet.Item(title: L("forward")) { v?.goForward() }) }
        let loading = tab.progress > 0 && tab.progress < 1
        if v != nil { items.append(Sheet.Item(title: L(loading ? "stop" : "reload")) { if loading { v?.stopLoading() } else { _ = v?.reload() } }) }
        if v != nil { items.append(Sheet.Item(title: L("share")) { [weak self] in self?.sharePage() }) }
        if Reader.isReader(tab.url) { items.append(Sheet.Item(title: L("reader_original")) { [weak self] in self?.openOriginal(tab) }) }
        if web && !tab.isPrivate { items.append(Sheet.Item(title: L("reader")) { [weak self] in self?.openReader(tab) }) }
        if web {
            items.append(Sheet.Item(title: L(browser.isBookmarked(tab.url) ? "remove_bookmark" : "add_bookmark")) { [weak self] in self?.toggleBookmark() })
            // A private tab keeps nothing on the device, saved articles included.
            if !tab.isPrivate { items.append(Sheet.Item(title: L(browser.isSavedForLater(tab.url) ? "remove_from_read_later" : "read_later_add")) { [weak self] in self?.toggleReadLater() }) }
            items.append(Sheet.Item(title: L("site_info")) { [weak self] in self?.siteCard(tab) })
        }
        if browser.split != nil { items.append(Sheet.Item(title: L("end_split")) { [weak self] in self?.browser.endSplit() }) }
        else if web { items.append(Sheet.Item(title: L("split_view")) { [weak self] in self?.pickSplit(tab) }) }
        if v != nil { items.append(Sheet.Item(title: L("find_in_page")) { [weak self] in self?.showFind() }) }
        if v != nil { items.append(Sheet.Item(title: state("desktop_site", tab.desktop)) { [weak self] in self?.browser.setDesktop(tab, !tab.desktop) }) }
        var detail: String?
        if web && Prefs.shared.blockerOn {
            let allowed = Blocker.shared.isAllowed(tab.host)
            items.append(Sheet.Item(title: L("ads_on_this_site") + ": " + L(allowed ? "allowed" : "blocked")) { [weak self] in
                self?.browser.setAdsAllowed(tab, !allowed)
            })
            detail = L(allowed ? "ads_allowed_here" : "ads_blocked_here")
        }
        if web && !tab.isPrivate && Account.shared.signedIn { items.append(Sheet.Item(title: L("send_to_device")) { [weak self] in self?.sendToDevice(tab) }) }
        items.append(Sheet.Item(title: L("tab_options")) { [weak self] in self?.tabSheet(tab) })
        items.append(Sheet.Item(title: L("close_tab")) { [weak self] in self?.browser.close(tab) })
        let title = tab.title.isEmpty ? (tab.isHome ? L("new_tab") : Navigation.label(tab.url)) : tab.title
        Sheet.menu(title, detail: detail, items: items, in: view)
    }

    /// Split view from the page menu: which tab goes beside this one.
    private func pickSplit(_ tab: Tab) {
        let others = browser.tabsIn(tab.space).filter { $0 !== tab && Navigation.isWeb($0.url) }
        if others.isEmpty { island.announce(L("split_none")); return }
        Sheet.menu(L("split_pick"), items: others.map { o in
            Sheet.Item(title: o.title.isEmpty ? Navigation.label(o.url) : o.title, detail: Navigation.label(o.url)) { [weak self] in self?.browser.startSplit(tab, with: o) }
        }, in: view)
    }

    /// What Mica knows about this site and what you can change for it (desktop's site card).
    func siteCard(_ tab: Tab) {
        let host = tab.host
        guard !host.isEmpty else { return }
        let secure = tab.url.hasPrefix("https://") && tab.view?.hasOnlySecureContent != false
        var detail = L(secure ? "site_secure" : "site_not_secure")
        if secure, let trust = tab.view?.serverTrust, let chain = SecTrustCopyCertificateChain(trust) as? [SecCertificate], chain.count > 1 {
            let issuer = SecCertificateCopySubjectSummary(chain[1]) as String? ?? ""
            if !issuer.isEmpty { detail += "\n" + L("site_issued_by", issuer) }
        }
        var content: [UIView] = []
        func line(_ label: String, _ tail: [UIView]) {
            let name = makeLabel(label, 15)
            let row = UIStackView(arrangedSubviews: [name, UIView()] + tail)
            row.alignment = .center
            row.spacing = 6
            row.isLayoutMarginsRelativeArrangement = true
            row.directionalLayoutMargins = NSDirectionalEdgeInsets(top: 4, leading: 6, bottom: 4, trailing: 0)
            content.append(row)
        }
        func button(_ title: String, _ run: @escaping (UIButton) -> Void) -> UIButton {
            let b = UIButton(type: .system)
            b.setTitle(title, for: .normal)
            b.titleLabel?.font = Mica.font(15, .semibold)
            b.tintColor = Mica.accent
            b.contentEdgeInsets = UIEdgeInsets(top: 8, left: 10, bottom: 8, right: 10)
            b.addAction(UIAction { _ in run(b) }, for: .touchUpInside)
            return b
        }
        let percent = makeLabel("\(Prefs.shared.zoom(for: host))%", 14, Mica.muted)
        percent.textAlignment = .center
        percent.widthAnchor.constraint(equalToConstant: 52).isActive = true
        let smaller = button("A−") { [weak self] _ in self?.browser.setZoom(tab, max(50, Prefs.shared.zoom(for: host) - 10)); percent.text = "\(Prefs.shared.zoom(for: host))%" }
        let bigger = button("A+") { [weak self] _ in self?.browser.setZoom(tab, min(300, Prefs.shared.zoom(for: host) + 10)); percent.text = "\(Prefs.shared.zoom(for: host))%" }
        line(L("text_size"), [smaller, percent, bigger])
        if Prefs.shared.blockerOn {
            let ads = button(L(Blocker.shared.isAllowed(host) ? "allowed" : "blocked")) { [weak self] b in
                let allowed = Blocker.shared.isAllowed(host)
                self?.browser.setAdsAllowed(tab, !allowed)
                b.setTitle(L(!allowed ? "allowed" : "blocked"), for: .normal)
            }
            line(L("ads_on_this_site"), [ads])
        }
        for (key, allowed) in Prefs.shared.sitePermissions where Domains.hostOf(String(key.split(separator: "\t").first ?? "")) == host {
            let kind = String(key.split(separator: "\t").last ?? "")
            var row: UIView?
            let forget = button(L("forget")) { _ in Prefs.shared.sitePermissions.removeValue(forKey: key); row?.isHidden = true }
            line(L("perm_" + kind).capitalized + ": " + L(allowed ? "allowed" : "blocked"), [forget])
            row = content.last
        }
        let clear = button(L("clear_site_data")) { [weak self] _ in
            guard let self else { return }
            Sheet.form(L("clear_site_data_q", host), detail: L("clear_site_data_detail"), content: [], buttons: [
                Sheet.Button(title: L("cancel")) { true },
                Sheet.Button(title: L("clear"), primary: true) { [weak self] in
                    self?.browser.clearSiteData(tab); self?.island.announce(L("site_data_cleared")); return true
                },
            ], in: self.view)
        }
        clear.contentHorizontalAlignment = .leading
        content.append(clear)
        Sheet.form(host, detail: detail, content: content, buttons: [Sheet.Button(title: L("close")) { true }], in: view)
    }

    // MARK: Tab menus

    /// Tab options as a card (from the page menu).
    private func tabSheet(_ tab: Tab) {
        Sheet.menu(tab.title.isEmpty ? (tab.isHome ? L("new_tab") : Navigation.label(tab.url)) : tab.title, items: tabActions(tab, changed: {}).map { a in
            Sheet.Item(title: a.title, destructive: a.destructive, action: a.run)
        }, in: view)
    }

    private struct TabAction { let title: String; var destructive = false; let run: () -> Void }

    private func tabActions(_ tab: Tab, changed: @escaping () -> Void) -> [TabAction] {
        var a: [TabAction] = []
        if !tab.isPrivate { a.append(TabAction(title: L(tab.pinned ? "unpin_tab" : "pin_tab")) { [weak self] in self?.browser.setPinned(tab, !tab.pinned); changed() }) }
        a.append(TabAction(title: L("add_to_group")) { [weak self] in self?.pickGroup(tab, changed) })
        if tab.group != nil { a.append(TabAction(title: L("remove_from_group")) { [weak self] in self?.browser.leaveGroup(tab); changed() }) }
        if !tab.isPrivate {
            for s in browser.spaces where s.id != tab.space && s.id != Browser.privateSpace {
                a.append(TabAction(title: L("move_to", browser.spaceName(s))) { [weak self] in self?.browser.moveTo(tab, spaceId: s.id); changed() })
            }
        }
        if let cur = browser.active, cur !== tab, cur.space == tab.space, Navigation.isWeb(cur.url), Navigation.isWeb(tab.url), !browser.inSplit(tab) {
            a.append(TabAction(title: L("split_with_current")) { [weak self] in self?.browser.startSplit(cur, with: tab) })
        }
        if browser.inSplit(tab) { a.append(TabAction(title: L("end_split")) { [weak self] in self?.browser.endSplit() }) }
        if !tab.isPrivate {
            a.append(TabAction(title: L("cookie_jar") + (browser.jar(tab.jar).map { " · " + $0.name } ?? "")) { [weak self] in self?.jarMenu(tab, changed) })
        }
        a.append(TabAction(title: L("close_tab"), destructive: true) { [weak self] in self?.browser.close(tab); changed() })
        return a
    }

    /// The same actions as a context menu (rail and deck).
    func tabMenu(_ tab: Tab, changed: @escaping () -> Void) -> UIMenu {
        UIMenu(title: tab.title.isEmpty ? Navigation.label(tab.url) : tab.title, children: tabActions(tab, changed: changed).map { a in
            UIAction(title: a.title, attributes: a.destructive ? .destructive : []) { _ in a.run() }
        })
    }

    func groupMenu(_ g: TabGroup, changed: @escaping () -> Void) -> UIMenu {
        UIMenu(title: g.name.isEmpty ? L("tab_group") : g.name, children: [
            UIAction(title: L("edit_group")) { [weak self] _ in self?.editGroup(g, tab: nil, changed) },
            UIAction(title: L("ungroup")) { [weak self] _ in self?.browser.ungroup(g); changed() },
            UIAction(title: L("close_group"), attributes: .destructive) { [weak self] _ in self?.browser.closeGroup(g); changed() },
        ])
    }

    private func pickGroup(_ tab: Tab, _ changed: @escaping () -> Void) {
        let groups = browser.groupsIn(tab.space).filter { $0.id != tab.group }
        if groups.isEmpty { editGroup(nil, tab: tab, changed); return }
        var items = groups.map { g in
            Sheet.Item(title: g.name.isEmpty ? L("tab_group") : g.name, detail: L("tabs_count", browser.members(g).count)) { [weak self] in
                self?.browser.addToGroup(tab, g); changed()
            }
        }
        items.append(Sheet.Item(title: L("new_group")) { [weak self] in self?.editGroup(nil, tab: tab, changed) })
        Sheet.menu(L("add_to_group"), items: items, in: view)
    }

    /// A new group for [tab] (group nil), or a group's name and colour.
    private func editGroup(_ group: TabGroup?, tab: Tab?, _ changed: @escaping () -> Void) {
        let name = Sheet.field(L("group_name"), text: group?.name ?? "")
        let swatches = Sheet.Swatches(Mica.palette, selected: group?.color ?? Mica.palette[browser.groupsIn().count % Mica.palette.count])
        Sheet.form(L(group == nil ? "new_group" : "edit_group"), content: [name, swatches], buttons: [
            Sheet.Button(title: L("cancel")) { true },
            Sheet.Button(title: L("save"), primary: true) { [weak self] in
                guard let self else { return true }
                let n = (name.text ?? "").trimmingCharacters(in: .whitespaces)
                if let group { self.browser.renameGroup(group, name: n, color: swatches.selected) }
                else if let tab { self.browser.newGroup(tab, name: n, color: swatches.selected) }
                changed()
                return true
            },
        ], in: view)
    }

    // MARK: Cookie jars

    private func jarMenu(_ tab: Tab, _ changed: @escaping () -> Void) {
        var items: [Sheet.Item] = [Sheet.Item(title: L("jar_none"), checked: tab.jar == nil) { [weak self] in
            self?.browser.moveToJar(tab, nil); self?.island.announce(L("jar_left")); changed()
        }]
        for j in browser.jars {
            items.append(Sheet.Item(title: j.name, checked: tab.jar == j.id) { [weak self] in
                self?.browser.moveToJar(tab, j); self?.island.announce(L("jar_moved", j.name)); changed()
            })
        }
        items.append(Sheet.Item(title: L("new_jar")) { [weak self] in self?.editJar(nil, tab: tab, changed) })
        items.append(Sheet.Item(title: L("manage_jars")) { [weak self] in self?.manageJars() })
        Sheet.menu(L("cookie_jar"), detail: L("cookie_jars_detail"), items: items, in: view)
    }

    func manageJars() {
        Sheet.menu(L("cookie_jars"), detail: L("cookie_jars_detail"), items: browser.jars.map { j in
            Sheet.Item(title: j.name, detail: L("tabs_count", browser.tabs.filter { $0.jar == j.id }.count)) { [weak self] in self?.editJar(j, tab: nil, {}) }
        } + [Sheet.Item(title: L("new_jar")) { [weak self] in self?.editJar(nil, tab: nil, {}) }], in: view)
    }

    private func editJar(_ jar: Jar?, tab: Tab?, _ changed: @escaping () -> Void) {
        let name = Sheet.field(L("jar_name"), text: jar?.name ?? "")
        var buttons: [Sheet.Button] = []
        if let jar {
            buttons.append(Sheet.Button(title: L("delete"), destructive: true) { [weak self] in
                guard let self else { return true }
                Sheet.form(L("delete_jar"), detail: L("delete_jar_detail", jar.name), content: [], buttons: [
                    Sheet.Button(title: L("cancel")) { true },
                    Sheet.Button(title: L("delete"), primary: true, destructive: true) { [weak self] in
                        self?.browser.deleteJar(jar); self?.island.announce(L("jar_deleted", jar.name)); changed(); return true
                    },
                ], in: self.view)
                return false
            })
        }
        buttons.append(Sheet.Button(title: L("cancel")) { true })
        buttons.append(Sheet.Button(title: L("save"), primary: true) { [weak self] in
            guard let self else { return true }
            let n = (name.text ?? "").trimmingCharacters(in: .whitespaces)
            if n.isEmpty { return false }
            if let jar { self.browser.renameJar(jar, n) }
            else {
                let j = self.browser.newJar(n)
                if let tab { self.browser.moveToJar(tab, j); self.island.announce(L("jar_moved", j.name)) }
            }
            changed()
            return true
        })
        Sheet.form(L(jar == nil ? "new_jar" : "rename"), content: [name], buttons: buttons, in: view)
    }

    // MARK: Sessions

    func sessionsMenu() {
        let saved = Sessions.list()
        var items: [Sheet.Item] = [Sheet.Item(title: L("save_session"), detail: L("save_session_detail")) { [weak self] in self?.saveSession() }]
        for s in saved {
            items.append(Sheet.Item(title: s.name, detail: L("tabs_count", s.tabs.count)) { [weak self] in self?.sessionMenu(s) })
        }
        Sheet.menu(L("sessions"), detail: saved.isEmpty ? L("sessions_empty") : L("sessions_hint"), items: items, in: view)
    }

    private func saveSession() {
        let tabs = browser.sessionTabs()
        if tabs.isEmpty { island.announce(L("no_web_tabs")); return }
        let name = Sheet.field(L("session_name"), text: L("session_default", DateFormatter.localizedString(from: Date(), dateStyle: .medium, timeStyle: .short)))
        Sheet.form(L("save_session"), content: [name], buttons: [
            Sheet.Button(title: L("cancel")) { true },
            Sheet.Button(title: L("save"), primary: true) { [weak self] in
                let n = (name.text ?? "").trimmingCharacters(in: .whitespaces)
                if n.isEmpty { return false }
                self?.island.announce(L(Sessions.save(n, tabs) ? "session_saved" : "session_save_failed", n))
                return true
            },
        ], in: view)
    }

    private func sessionMenu(_ s: Sessions.Saved) {
        Sheet.menu(s.name, detail: L("tabs_count", s.tabs.count), items: [
            Sheet.Item(title: L("restore")) { [weak self] in
                guard let self else { return }
                if self.browser.restore(s) > 0 { self.island.announce(L("session_restored", s.name)) }
            },
            Sheet.Item(title: L("delete"), destructive: true) { Sessions.delete(s) },
        ], in: view)
    }

    // MARK: Send to device

    func sendToDevice(_ tab: Tab) {
        Account.shared.devices { [weak self] devices in
            guard let self else { return }
            let others = devices.filter { !$0.isThis }
            if others.isEmpty { self.island.announce(L("no_other_devices")); return }
            Sheet.menu(L("send_to_device"), detail: tab.title, items: others.map { d in
                Sheet.Item(title: d.name) { [weak self] in
                    Account.shared.send(url: self?.browser.pageKey(tab) ?? tab.url, title: tab.title, to: d.id) { error in
                        self?.island.announce(error ?? L("sent_to", d.name))
                    }
                }
            }, in: self.view)
        }
    }
}
