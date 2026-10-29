import UIKit

/// Every tab of the space as cards (desktop's Spotlight tab list, Android's deck), with tab groups as headers you
/// fold. Swipe a card away to close it; press and hold for its menu.
final class Deck: UIView, UITableViewDataSource, UITableViewDelegate {
    weak var main: MainViewController?
    private let scrim = UIControl()
    private let card = UIView()
    private let count = makeLabel("", 13, Mica.muted, weight: .medium)
    private let table = UITableView(frame: .zero, style: .insetGrouped)
    private let buttons = UIStackView()
    private(set) var showing = false
    private var browser: Browser { Browser.shared }

    /// Sections: ungrouped tabs (nil) and each group, in tab order.
    private var sections: [(TabGroup?, [Tab])] = []

    override init(frame: CGRect) {
        super.init(frame: frame)
        isHidden = true
        scrim.backgroundColor = Mica.scrim
        scrim.addTarget(self, action: #selector(close), for: .touchUpInside)
        addSubview(scrim)
        scrim.pin(to: self)

        card.backgroundColor = Mica.panel
        card.round(28, border: Mica.line)
        card.clipsToBounds = true
        card.translatesAutoresizingMaskIntoConstraints = false
        addSubview(card)
        table.backgroundColor = .clear
        table.dataSource = self
        table.delegate = self
        table.register(DeckCell.self, forCellReuseIdentifier: "card")
        table.sectionHeaderTopPadding = 4
        buttons.axis = .horizontal
        buttons.spacing = 10
        buttons.distribution = .fillEqually
        for v in [count, table, buttons] as [UIView] { card.addSubview(v); v.translatesAutoresizingMaskIntoConstraints = false }
        NSLayoutConstraint.activate([
            card.leadingAnchor.constraint(equalTo: safeAreaLayoutGuide.leadingAnchor, constant: 10),
            card.trailingAnchor.constraint(equalTo: safeAreaLayoutGuide.trailingAnchor, constant: -10),
            card.topAnchor.constraint(equalTo: safeAreaLayoutGuide.topAnchor, constant: 30),
            card.bottomAnchor.constraint(equalTo: safeAreaLayoutGuide.bottomAnchor, constant: -10),
            count.topAnchor.constraint(equalTo: card.topAnchor, constant: 16),
            count.leadingAnchor.constraint(equalTo: card.leadingAnchor, constant: 22),
            table.topAnchor.constraint(equalTo: count.bottomAnchor, constant: 4),
            table.leadingAnchor.constraint(equalTo: card.leadingAnchor),
            table.trailingAnchor.constraint(equalTo: card.trailingAnchor),
            buttons.topAnchor.constraint(equalTo: table.bottomAnchor, constant: 8),
            buttons.leadingAnchor.constraint(equalTo: card.leadingAnchor, constant: 14),
            buttons.trailingAnchor.constraint(equalTo: card.trailingAnchor, constant: -14),
            buttons.bottomAnchor.constraint(equalTo: card.bottomAnchor, constant: -14),
            buttons.heightAnchor.constraint(equalToConstant: 46),
        ])
    }

    required init?(coder: NSCoder) { fatalError() }

    func show() {
        refresh()
        guard !showing else { return }
        showing = true
        isHidden = false
        layoutIfNeeded()
        card.transform = CGAffineTransform(translationX: 0, y: bounds.height)
        scrim.alpha = 0
        UIView.animate(withDuration: 0.42, delay: 0, usingSpringWithDamping: 0.88, initialSpringVelocity: 0) {
            self.card.transform = .identity
            self.scrim.alpha = 1
        }
        if let a = browser.active, let ip = indexPath(of: a) { table.scrollToRow(at: ip, at: .middle, animated: false) }
    }

    @objc func close() {
        guard showing else { return }
        showing = false
        UIView.animate(withDuration: 0.25, delay: 0, options: [.curveEaseIn]) {
            self.card.transform = CGAffineTransform(translationX: 0, y: self.bounds.height)
            self.scrim.alpha = 0
        } completion: { _ in if !self.showing { self.isHidden = true } }
    }

    func refresh() {
        let list = browser.tabsIn()
        sections = []
        var loose: [Tab] = []
        var seen = Set<String>()
        for t in list {
            if let g = browser.group(t.group) {
                if seen.insert(g.id).inserted { sections.append((g, list.filter { $0.group == g.id })) }
            } else { loose.append(t) }
        }
        sections.insert((nil, loose), at: 0)
        count.text = L("tabs_count", list.count)
        renderButtons()
        table.reloadData()
    }

    private func renderButtons() {
        buttons.arrangedSubviews.forEach { $0.removeFromSuperview() }
        let inPrivate = browser.activeSpaceId == Browser.privateSpace
        func button(_ title: String, filled: Bool, _ run: @escaping () -> Void) {
            let b = UIButton(type: .system)
            var cfg = filled ? UIButton.Configuration.filled() : UIButton.Configuration.plain()
            cfg.title = title
            cfg.cornerStyle = .capsule
            cfg.baseBackgroundColor = Mica.accent
            cfg.baseForegroundColor = filled ? Mica.canvas : Mica.accent
            b.configuration = cfg
            b.addAction(UIAction { _ in run() }, for: .touchUpInside)
            buttons.addArrangedSubview(b)
        }
        button("+  " + L(inPrivate ? "new_private_tab" : "new_tab"), filled: true) { [weak self] in self?.close(); self?.main?.newTab() }
        if inPrivate { button(L("close_private_tabs"), filled: false) { [weak self] in self?.close(); self?.browser.closePrivate() } }
        else { button("◐  " + L("private_tabs"), filled: false) { [weak self] in self?.close(); self?.main?.newPrivateTab() } }
    }

    private func indexPath(of tab: Tab) -> IndexPath? {
        for (s, sec) in sections.enumerated() {
            if sec.0?.collapsed == true { continue }
            if let r = sec.1.firstIndex(where: { $0 === tab }) { return IndexPath(row: r, section: s) }
        }
        return nil
    }

    private func tab(at ip: IndexPath) -> Tab { sections[ip.section].1[ip.row] }

    // MARK: Table

    func numberOfSections(in tableView: UITableView) -> Int { sections.count }

    func tableView(_ t: UITableView, numberOfRowsInSection s: Int) -> Int {
        sections[s].0?.collapsed == true ? 0 : sections[s].1.count
    }

    func tableView(_ t: UITableView, viewForHeaderInSection s: Int) -> UIView? {
        guard let g = sections[s].0 else { return nil }
        let row = PressRow(radius: 12) { [weak self] in
            guard let self else { return }
            self.browser.setCollapsed(g, !g.collapsed)
            self.table.reloadSections(IndexSet(integer: s), with: .fade)
        }
        let dot = UIView()
        dot.backgroundColor = Mica.spaceColor(g.color)
        dot.round(5)
        let name = makeLabel(g.name.isEmpty ? L("tab_group") : g.name, 14, Mica.ink, weight: .semibold)
        let n = makeLabel((g.collapsed ? "▸ " : "▾ ") + "\(sections[s].1.count)", 13, Mica.muted)
        let stack = UIStackView(arrangedSubviews: [dot, name, UIView(), n])
        stack.spacing = 10
        stack.alignment = .center
        stack.isUserInteractionEnabled = false
        dot.widthAnchor.constraint(equalToConstant: 10).isActive = true
        dot.heightAnchor.constraint(equalToConstant: 10).isActive = true
        row.addSubview(stack)
        stack.pin(to: row, insets: UIEdgeInsets(top: 8, left: 22, bottom: 8, right: 22))
        row.addInteraction(UIContextMenuInteraction(delegate: GroupMenu(deck: self, group: g)))
        return row
    }

    func tableView(_ t: UITableView, heightForHeaderInSection s: Int) -> CGFloat { sections[s].0 == nil ? 6 : UITableView.automaticDimension }

    func tableView(_ t: UITableView, cellForRowAt ip: IndexPath) -> UITableViewCell {
        let c = t.dequeueReusableCell(withIdentifier: "card", for: ip) as! DeckCell
        let tab = self.tab(at: ip)
        c.show(tab, active: tab === browser.active, jar: browser.jar(tab.jar))
        return c
    }

    func tableView(_ t: UITableView, didSelectRowAt ip: IndexPath) {
        let tab = self.tab(at: ip)
        close()
        browser.activate(tab)
    }

    func tableView(_ t: UITableView, trailingSwipeActionsConfigurationForRowAt ip: IndexPath) -> UISwipeActionsConfiguration? {
        let tab = self.tab(at: ip)
        let a = UIContextualAction(style: .destructive, title: L("close")) { [weak self] _, _, done in
            self?.browser.close(tab); self?.refresh(); done(true)
        }
        let c = UISwipeActionsConfiguration(actions: [a])
        c.performsFirstActionWithFullSwipe = true
        return c
    }

    func tableView(_ t: UITableView, contextMenuConfigurationForRowAt ip: IndexPath, point: CGPoint) -> UIContextMenuConfiguration? {
        let tab = self.tab(at: ip)
        return UIContextMenuConfiguration(identifier: nil, previewProvider: nil) { [weak self] _ in self?.main?.tabMenu(tab) { self?.refresh() } }
    }

    fileprivate func groupMenu(_ g: TabGroup) -> UIMenu? { main?.groupMenu(g) { [weak self] in self?.refresh() } }
}

private final class GroupMenu: NSObject, UIContextMenuInteractionDelegate {
    weak var deck: Deck?
    let group: TabGroup
    init(deck: Deck, group: TabGroup) { self.deck = deck; self.group = group; super.init(); GroupMenu.keep.append(self) }
    private static var keep: [GroupMenu] = []
    func contextMenuInteraction(_ i: UIContextMenuInteraction, configurationForMenuAtLocation l: CGPoint) -> UIContextMenuConfiguration? {
        UIContextMenuConfiguration(identifier: nil, previewProvider: nil) { [weak self] _ in self.flatMap { $0.deck?.groupMenu($0.group) } }
    }
}

final class DeckCell: UITableViewCell {
    private let title = makeLabel("", 15, Mica.ink, weight: .medium)
    private let detail = makeLabel("", 12, Mica.muted)
    private let icon = UIImageView()
    private let jarDot = UIView()

    override init(style: UITableViewCell.CellStyle, reuseIdentifier: String?) {
        super.init(style: style, reuseIdentifier: reuseIdentifier)
        backgroundColor = Mica.card
        icon.contentMode = .scaleAspectFit
        icon.round(6)
        icon.clipsToBounds = true
        jarDot.round(4)
        let texts = UIStackView(arrangedSubviews: [title, detail])
        texts.axis = .vertical
        texts.spacing = 3
        for v in [icon, texts, jarDot] as [UIView] { contentView.addSubview(v); v.translatesAutoresizingMaskIntoConstraints = false }
        NSLayoutConstraint.activate([
            icon.leadingAnchor.constraint(equalTo: contentView.leadingAnchor, constant: 14),
            icon.centerYAnchor.constraint(equalTo: contentView.centerYAnchor),
            icon.widthAnchor.constraint(equalToConstant: 22), icon.heightAnchor.constraint(equalToConstant: 22),
            texts.leadingAnchor.constraint(equalTo: icon.trailingAnchor, constant: 12),
            texts.trailingAnchor.constraint(equalTo: jarDot.leadingAnchor, constant: -10),
            texts.topAnchor.constraint(equalTo: contentView.topAnchor, constant: 12),
            texts.bottomAnchor.constraint(equalTo: contentView.bottomAnchor, constant: -12),
            jarDot.trailingAnchor.constraint(equalTo: contentView.trailingAnchor, constant: -16),
            jarDot.centerYAnchor.constraint(equalTo: contentView.centerYAnchor),
            jarDot.widthAnchor.constraint(equalToConstant: 8), jarDot.heightAnchor.constraint(equalToConstant: 8),
        ])
    }

    required init?(coder: NSCoder) { fatalError() }

    func show(_ tab: Tab, active: Bool, jar: Jar?) {
        title.text = tab.title.isEmpty ? (tab.isHome ? L("new_tab") : Navigation.label(tab.url)) : tab.title
        var bits: [String] = []
        if tab.playing { bits.append("♪ " + L("playing")) }
        else if tab.view == nil && !tab.isHome { bits.append(L("resting").capitalized) }
        else if tab.pinned { bits.append(L("pinned").capitalized) }
        if !tab.isHome { bits.append(Navigation.label(tab.url)) }
        if let jar { bits.append(jar.name) }
        detail.text = bits.joined(separator: " · ")
        icon.image = tab.icon ?? Favicons.icon(for: tab.host)
        icon.backgroundColor = icon.image == nil ? Mica.soft : .clear
        jarDot.backgroundColor = jar.map { Mica.spaceColor($0.color) } ?? .clear
        layer.borderColor = Mica.accent.cgColor
        layer.borderWidth = active ? 1.5 : 0
        layer.cornerRadius = 12
        accessibilityLabel = (title.text ?? "") + ", " + (detail.text ?? "")
        accessibilityTraits = active ? [.button, .selected] : .button
    }
}
