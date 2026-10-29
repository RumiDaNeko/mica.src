import UIKit

/// Desktop's Spotlight as a card rising from the island: the field sits at the bottom by your thumb, results grow
/// upward. Empty, it offers page actions, the games and recent tabs.
final class Spotlight: UIView, UITextFieldDelegate {
    weak var main: MainViewController?
    private let scrim = UIControl()
    private let card = UIView()
    private let tintView = UIView()
    private let results = UIStackView()
    private let scroll = UIScrollView()
    let field = PaddedField()
    private(set) var showing = false
    private var browser: Browser { Browser.shared }

    override init(frame: CGRect) {
        super.init(frame: frame)
        isHidden = true
        scrim.backgroundColor = Mica.scrim
        scrim.addTarget(self, action: #selector(close), for: .touchUpInside)
        addSubview(scrim)
        scrim.pin(to: self)

        card.backgroundColor = Mica.panel
        card.round(26, border: Mica.line)
        card.layer.shadowColor = UIColor.black.cgColor
        card.layer.shadowOpacity = 0.2
        card.layer.shadowRadius = 24
        card.translatesAutoresizingMaskIntoConstraints = false
        addSubview(card)
        tintView.isUserInteractionEnabled = false
        tintView.round(26)
        card.addSubview(tintView)
        tintView.pin(to: card)

        results.axis = .vertical
        results.spacing = 0
        results.translatesAutoresizingMaskIntoConstraints = false
        scroll.addSubview(results)
        scroll.showsVerticalScrollIndicator = false
        scroll.keyboardDismissMode = .interactive
        scroll.translatesAutoresizingMaskIntoConstraints = false
        card.addSubview(scroll)

        field.placeholder = L("search_hint")
        field.font = Mica.font(16)
        field.textColor = Mica.ink
        field.backgroundColor = Mica.card
        field.round(23, border: Mica.line)
        field.keyboardType = .webSearch
        field.returnKeyType = .go
        field.autocorrectionType = .no
        field.autocapitalizationType = .none
        field.clearButtonMode = .whileEditing
        field.delegate = self
        field.addTarget(self, action: #selector(changed), for: .editingChanged)
        field.translatesAutoresizingMaskIntoConstraints = false
        card.addSubview(field)

        let fit = scroll.heightAnchor.constraint(equalTo: results.heightAnchor)
        fit.priority = .defaultHigh
        let wide = card.widthAnchor.constraint(equalTo: widthAnchor, constant: -20)
        wide.priority = .defaultHigh
        NSLayoutConstraint.activate([
            card.centerXAnchor.constraint(equalTo: centerXAnchor),
            wide,
            card.widthAnchor.constraint(lessThanOrEqualToConstant: 640),
            card.bottomAnchor.constraint(equalTo: keyboardLayoutGuide.topAnchor, constant: -10),
            card.topAnchor.constraint(greaterThanOrEqualTo: safeAreaLayoutGuide.topAnchor, constant: 20),
            scroll.topAnchor.constraint(equalTo: card.topAnchor, constant: 12),
            scroll.leadingAnchor.constraint(equalTo: card.leadingAnchor, constant: 12),
            scroll.trailingAnchor.constraint(equalTo: card.trailingAnchor, constant: -12),
            fit,
            results.leadingAnchor.constraint(equalTo: scroll.contentLayoutGuide.leadingAnchor),
            results.trailingAnchor.constraint(equalTo: scroll.contentLayoutGuide.trailingAnchor),
            results.topAnchor.constraint(equalTo: scroll.contentLayoutGuide.topAnchor),
            results.bottomAnchor.constraint(equalTo: scroll.contentLayoutGuide.bottomAnchor),
            results.widthAnchor.constraint(equalTo: scroll.frameLayoutGuide.widthAnchor),
            field.topAnchor.constraint(equalTo: scroll.bottomAnchor, constant: 10),
            field.leadingAnchor.constraint(equalTo: card.leadingAnchor, constant: 12),
            field.trailingAnchor.constraint(equalTo: card.trailingAnchor, constant: -12),
            field.bottomAnchor.constraint(equalTo: card.bottomAnchor, constant: -12),
            field.heightAnchor.constraint(equalToConstant: 46),
        ])
    }

    required init?(coder: NSCoder) { fatalError() }

    func show(prefill: String = "", from island: UIView) {
        tintView.backgroundColor = browser.active?.tint?.withAlphaComponent(0.14)
        let opening = !showing
        showing = true
        isHidden = false
        field.text = prefill
        render(prefill)
        if opening {
            // Grows out of the island.
            layoutIfNeeded()
            let from = island.convert(island.bounds, to: self)
            let to = card.frame
            card.transform = CGAffineTransform(translationX: from.midX - to.midX, y: from.midY - to.midY)
                .scaledBy(x: max(0.2, from.width / max(1, to.width)), y: max(0.05, from.height / max(1, to.height)))
            card.alpha = 0.4
            scrim.alpha = 0
            UIView.animate(withDuration: 0.38, delay: 0, usingSpringWithDamping: 0.86, initialSpringVelocity: 0) {
                self.card.transform = .identity
                self.card.alpha = 1
                self.scrim.alpha = 1
            }
        }
        field.becomeFirstResponder()
        DispatchQueue.main.async { self.field.selectAll(nil) }
    }

    @objc func close() {
        guard showing else { return }
        showing = false
        field.resignFirstResponder()
        UIView.animate(withDuration: 0.2, delay: 0, options: [.curveEaseIn]) {
            self.card.transform = CGAffineTransform(translationX: 0, y: 40).scaledBy(x: 0.96, y: 0.96)
            self.card.alpha = 0
            self.scrim.alpha = 0
        } completion: { _ in
            if !self.showing { self.isHidden = true; self.card.transform = .identity; self.card.alpha = 1 }
        }
        main?.spotlightClosed()
    }

    func textFieldShouldReturn(_ textField: UITextField) -> Bool {
        let text = (field.text ?? "").trimmingCharacters(in: .whitespaces)
        guard let url = Navigation.resolve(text) else { return false }
        let first = (results.arrangedSubviews.first as? HitRow)?.hit
        close()
        if let first, first.kind == .shortcut { main?.openHit(first) } else { main?.navigateActive(url) }
        return false
    }

    @objc private func changed() { if showing { render(field.text ?? "") } }

    private func render(_ query: String) {
        results.arrangedSubviews.forEach { $0.removeFromSuperview() }
        let active = browser.active
        let q = query == active?.url ? "" : query
        if q.trimmingCharacters(in: .whitespaces).isEmpty { renderEmpty(); return }
        let tabs = browser.tabs.filter { $0 !== active }.sorted { $0.lastUsed > $1.lastUsed }.map { t in
            Navigation.Entry(url: t.url, title: t.title, tabId: t.id, detail: spaceName(t) + " · " + Navigation.label(t.url))
        }
        let history = browser.history.map { Navigation.Entry(url: $0.url, title: $0.title) }
        let marks = browser.bookmarks.map { Navigation.Entry(url: $0.url, title: $0.title, detail: "★ " + Navigation.label($0.url)) }
        for hit in Navigation.search(q, tabs: tabs, history: history, bookmarks: marks) { results.addArrangedSubview(row(hit)) }
    }

    private func spaceName(_ t: Tab) -> String { browser.space(t.space).map { browser.spaceName($0) } ?? "" }

    private func row(_ hit: Navigation.Hit) -> UIView {
        let r = HitRow(hit: hit) { [weak self] in self?.close(); self?.main?.openHit(hit) }
        let color: UIColor
        switch hit.kind {
        case .tab: color = browser.tab(hit.tabId).flatMap { browser.space($0.space) }.map { Mica.spaceColor($0.color) } ?? Mica.accent
        case .history: color = Mica.faint
        default: color = Mica.accent
        }
        let dot = UIView()
        dot.backgroundColor = color
        dot.round(4)
        let title = makeLabel(hit.title, 15)
        let detail = makeLabel(hit.detail, 12, Mica.muted)
        detail.lineBreakMode = .byTruncatingMiddle
        let texts = UIStackView(arrangedSubviews: [title, detail])
        texts.axis = .vertical
        texts.spacing = 3
        for v in [dot, texts] as [UIView] { v.isUserInteractionEnabled = false; v.translatesAutoresizingMaskIntoConstraints = false; r.addSubview(v) }
        NSLayoutConstraint.activate([
            dot.leadingAnchor.constraint(equalTo: r.leadingAnchor, constant: 10),
            dot.centerYAnchor.constraint(equalTo: r.centerYAnchor),
            dot.widthAnchor.constraint(equalToConstant: 8), dot.heightAnchor.constraint(equalToConstant: 8),
            texts.leadingAnchor.constraint(equalTo: dot.trailingAnchor, constant: 12),
            texts.trailingAnchor.constraint(equalTo: r.trailingAnchor, constant: -10),
            texts.topAnchor.constraint(equalTo: r.topAnchor, constant: 10),
            texts.bottomAnchor.constraint(equalTo: r.bottomAnchor, constant: -10),
        ])
        r.accessibilityLabel = hit.title + ", " + hit.detail
        return r
    }

    private func renderEmpty() {
        let tab = browser.active
        let web = tab != nil && !(tab!.isHome)
        let v = tab?.view
        let loading = (tab?.progress ?? 1) < 1 && (tab?.progress ?? 1) > 0
        let actions = UIStackView()
        actions.axis = .horizontal
        actions.distribution = .fillEqually
        func action(_ glyph: String, _ key: String, enabled: Bool = true, run: @escaping () -> Void) {
            let cell = PressRow(radius: 14) { [weak self] in self?.close(); run() }
            cell.isEnabled = enabled
            let g = makeLabel(glyph, 20, enabled ? Mica.ink : Mica.faint)
            let l = makeLabel(L(key), 11, Mica.muted)
            for x in [g, l] { x.textAlignment = .center; x.isUserInteractionEnabled = false }
            let col = UIStackView(arrangedSubviews: [g, l])
            col.axis = .vertical
            col.spacing = 4
            col.isUserInteractionEnabled = false
            cell.addSubview(col)
            col.pin(to: cell, insets: UIEdgeInsets(top: 8, left: 2, bottom: 8, right: 2))
            cell.accessibilityLabel = L(key)
            actions.addArrangedSubview(cell)
        }
        action("←", "back", enabled: v?.canGoBack == true) { v?.goBack() }
        action("→", "forward", enabled: v?.canGoForward == true) { v?.goForward() }
        action(loading ? "✕" : "↻", loading ? "stop" : "reload", enabled: web) { if loading { v?.stopLoading() } else { _ = v?.reload() } }
        action("↗", "share", enabled: web) { [weak self] in self?.main?.sharePage() }
        let marked = web && browser.isBookmarked(tab!.url)
        action(marked ? "★" : "☆", marked ? "bookmarked_short" : "bookmark", enabled: web) { [weak self] in self?.main?.toggleBookmark() }
        action("▤", "library") { [weak self] in self?.main?.openLibrary() }
        action("☰", "settings") { [weak self] in self?.main?.openSettings() }
        results.addArrangedSubview(actions)

        results.addArrangedSubview(header("games"))
        let strip = UIStackView()
        strip.axis = .horizontal
        strip.spacing = 8
        let colors: [UInt32] = [0x7A9CFF, 0x4CBF6A, 0xF2B179, 0x888780, 0xE5534B, 0x3D6DF2, 0xE5534B, 0xF2C230, 0x35B9D6, 0x4CBF6A, 0xF2C230]
        let list = [(Navigation.tetris, "Tetris")] + Navigation.games.dropFirst().map { ($0.address, $0.title) }
        for (i, (address, title)) in list.enumerated() {
            let chip = PressRow(radius: 16) { [weak self] in self?.close(); self?.main?.navigateActive(address) }
            chip.backgroundColor = Mica.card
            chip.round(16, border: Mica.line)
            let d = UIView()
            d.backgroundColor = UIColor(rgb: colors[i % colors.count])
            d.round(5)
            let l = makeLabel(title, 13)
            let row = UIStackView(arrangedSubviews: [d, l])
            row.spacing = 8
            row.alignment = .center
            row.isUserInteractionEnabled = false
            d.widthAnchor.constraint(equalToConstant: 10).isActive = true
            d.heightAnchor.constraint(equalToConstant: 10).isActive = true
            chip.addSubview(row)
            row.pin(to: chip, insets: UIEdgeInsets(top: 9, left: 12, bottom: 9, right: 12))
            strip.addArrangedSubview(chip)
        }
        let all = PressRow(radius: 16) { [weak self] in self?.close(); self?.main?.navigateActive("mica://games") }
        let dots = makeLabel("•••", 13, Mica.accent)
        dots.isUserInteractionEnabled = false
        all.addSubview(dots)
        dots.pin(to: all, insets: UIEdgeInsets(top: 9, left: 12, bottom: 9, right: 12))
        all.accessibilityLabel = L("games")
        strip.addArrangedSubview(all)
        let hs = UIScrollView()
        hs.showsHorizontalScrollIndicator = false
        hs.addSubview(strip)
        strip.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            strip.leadingAnchor.constraint(equalTo: hs.contentLayoutGuide.leadingAnchor),
            strip.trailingAnchor.constraint(equalTo: hs.contentLayoutGuide.trailingAnchor),
            strip.topAnchor.constraint(equalTo: hs.contentLayoutGuide.topAnchor),
            strip.bottomAnchor.constraint(equalTo: hs.contentLayoutGuide.bottomAnchor),
            hs.heightAnchor.constraint(equalTo: strip.heightAnchor),
        ])
        results.addArrangedSubview(hs)

        let recent = browser.tabs.filter { $0 !== tab && !$0.isHome && !$0.url.isEmpty }.sorted { $0.lastUsed > $1.lastUsed }.prefix(4)
        if !recent.isEmpty {
            results.addArrangedSubview(header("tabs_header"))
            for t in recent {
                results.addArrangedSubview(row(Navigation.Hit(kind: .tab, title: t.title.isEmpty ? Navigation.label(t.url) : t.title,
                                                               detail: spaceName(t) + " · " + Navigation.label(t.url), url: t.url, tabId: t.id)))
            }
        }
    }

    private func header(_ key: String) -> UIView {
        let l = makeLabel(L(key).uppercased(), 11, Mica.faint, weight: .medium)
        let box = UIView()
        box.addSubview(l)
        l.pin(to: box, insets: UIEdgeInsets(top: 14, left: 10, bottom: 6, right: 10))
        return box
    }
}

final class HitRow: PressRow {
    let hit: Navigation.Hit
    init(hit: Navigation.Hit, action: @escaping () -> Void) {
        self.hit = hit
        super.init(radius: 14, action: action)
    }
    required init?(coder: NSCoder) { fatalError() }
}
