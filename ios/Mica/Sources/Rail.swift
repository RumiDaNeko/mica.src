import UIKit

/// The spaces rail (desktop's sidebar): the spaces along the top, the tabs of the one you're in, and the way to
/// everything else. Slides in from the left edge, or from the island's space dot.
final class Rail: UIView, UITableViewDataSource, UITableViewDelegate {
    weak var main: MainViewController?
    private let scrim = UIControl()
    private let panel = UIView()
    private let table = UITableView(frame: .zero, style: .plain)
    private let chips = UIStackView()
    private let title = makeLabel("", 22, Mica.ink, weight: .semibold)
    private(set) var showing = false
    private var pinned: [Tab] = []
    private var rest: [Tab] = []
    private var browser: Browser { Browser.shared }
    private let width: CGFloat = 310

    override init(frame: CGRect) {
        super.init(frame: frame)
        isHidden = true
        scrim.backgroundColor = Mica.scrim
        scrim.addTarget(self, action: #selector(close), for: .touchUpInside)
        addSubview(scrim)
        scrim.pin(to: self)

        panel.backgroundColor = Mica.panel
        panel.layer.shadowColor = UIColor.black.cgColor
        panel.layer.shadowOpacity = 0.2
        panel.layer.shadowRadius = 20
        addSubview(panel)
        panel.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            panel.leadingAnchor.constraint(equalTo: leadingAnchor),
            panel.topAnchor.constraint(equalTo: topAnchor),
            panel.bottomAnchor.constraint(equalTo: bottomAnchor),
            panel.widthAnchor.constraint(equalToConstant: width),
        ])
        table.backgroundColor = .clear
        table.separatorStyle = .none
        table.dataSource = self
        table.delegate = self
        table.register(TabCell.self, forCellReuseIdentifier: "tab")
        table.sectionHeaderTopPadding = 0
        panel.addSubview(table)
        table.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            table.leadingAnchor.constraint(equalTo: panel.leadingAnchor),
            table.trailingAnchor.constraint(equalTo: panel.trailingAnchor),
            table.topAnchor.constraint(equalTo: panel.safeAreaLayoutGuide.topAnchor),
            table.bottomAnchor.constraint(equalTo: panel.bottomAnchor),
        ])
        let pan = UIPanGestureRecognizer(target: self, action: #selector(dragged(_:)))
        panel.addGestureRecognizer(pan)
    }

    required init?(coder: NSCoder) { fatalError() }

    func show() {
        refresh()
        guard !showing else { return }
        showing = true
        isHidden = false
        layoutIfNeeded()
        panel.transform = CGAffineTransform(translationX: -width - 30, y: 0)
        scrim.alpha = 0
        UIView.animate(withDuration: 0.36, delay: 0, usingSpringWithDamping: 0.9, initialSpringVelocity: 0) {
            self.panel.transform = .identity
            self.scrim.alpha = 1
        }
    }

    @objc func close() {
        guard showing else { return }
        showing = false
        UIView.animate(withDuration: 0.24, delay: 0, options: [.curveEaseIn]) {
            self.panel.transform = CGAffineTransform(translationX: -self.width - 30, y: 0)
            self.scrim.alpha = 0
        } completion: { _ in if !self.showing { self.isHidden = true } }
    }

    @objc private func dragged(_ g: UIPanGestureRecognizer) {
        let x = min(0, g.translation(in: self).x)
        switch g.state {
        case .changed: panel.transform = CGAffineTransform(translationX: x, y: 0); scrim.alpha = 1 + x / width
        case .ended, .cancelled:
            if x < -80 || g.velocity(in: self).x < -500 { close() }
            else { UIView.animate(withDuration: 0.25) { self.panel.transform = .identity; self.scrim.alpha = 1 } }
        default: break
        }
    }

    func refresh() {
        let list = browser.tabsIn()
        pinned = list.filter { $0.pinned }
        rest = list.filter { !$0.pinned }
        table.tableHeaderView = header()
        table.tableFooterView = footer()
        table.reloadData()
    }

    // MARK: Header: spaces

    private func header() -> UIView {
        chips.arrangedSubviews.forEach { $0.removeFromSuperview() }
        chips.axis = .horizontal
        chips.spacing = 8
        for s in browser.spaces {
            let on = s.id == browser.activeSpaceId
            let chip = PressRow(radius: 12) { [weak self] in
                self?.browser.switchSpace(s.id); self?.refresh()
            }
            chip.backgroundColor = on ? Mica.spaceColor(s.color) : Mica.soft
            chip.round(12)
            let letter = makeLabel(String(browser.spaceName(s).prefix(1)).uppercased(), 15, on ? .white : Mica.ink, weight: .semibold)
            letter.textAlignment = .center
            letter.isUserInteractionEnabled = false
            chip.addSubview(letter)
            letter.pin(to: chip)
            chip.widthAnchor.constraint(equalToConstant: 40).isActive = true
            chip.heightAnchor.constraint(equalToConstant: 40).isActive = true
            chip.accessibilityLabel = browser.spaceName(s)
            chip.addGestureRecognizer(UILongPressGestureRecognizer(target: self, action: #selector(editChip(_:))))
            chip.tag = browser.spaces.firstIndex { $0 === s } ?? 0
            chips.addArrangedSubview(chip)
        }
        let add = PressRow(radius: 12) { [weak self] in self?.editSpace(nil) }
        add.round(12, border: Mica.line)
        let plus = makeLabel("+", 18, Mica.muted)
        plus.textAlignment = .center
        plus.isUserInteractionEnabled = false
        add.addSubview(plus)
        plus.pin(to: add)
        add.widthAnchor.constraint(equalToConstant: 40).isActive = true
        add.heightAnchor.constraint(equalToConstant: 40).isActive = true
        add.accessibilityLabel = L("new_space")
        chips.addArrangedSubview(add)

        title.text = browser.spaceName(browser.activeSpace)
        title.textColor = Mica.spaceColor(browser.activeSpace.color)
        title.isUserInteractionEnabled = true
        title.gestureRecognizers?.forEach { title.removeGestureRecognizer($0) }
        title.addGestureRecognizer(UILongPressGestureRecognizer(target: self, action: #selector(editTitle(_:))))

        let scroller = UIScrollView()
        scroller.showsHorizontalScrollIndicator = false
        scroller.addSubview(chips)
        chips.translatesAutoresizingMaskIntoConstraints = false
        let box = UIView(frame: CGRect(x: 0, y: 0, width: width, height: 118))
        for v in [scroller, title] as [UIView] { box.addSubview(v); v.translatesAutoresizingMaskIntoConstraints = false }
        NSLayoutConstraint.activate([
            scroller.leadingAnchor.constraint(equalTo: box.leadingAnchor, constant: 16),
            scroller.trailingAnchor.constraint(equalTo: box.trailingAnchor, constant: -16),
            scroller.topAnchor.constraint(equalTo: box.topAnchor, constant: 14),
            scroller.heightAnchor.constraint(equalToConstant: 40),
            chips.leadingAnchor.constraint(equalTo: scroller.contentLayoutGuide.leadingAnchor),
            chips.trailingAnchor.constraint(equalTo: scroller.contentLayoutGuide.trailingAnchor),
            chips.topAnchor.constraint(equalTo: scroller.contentLayoutGuide.topAnchor),
            chips.bottomAnchor.constraint(equalTo: scroller.contentLayoutGuide.bottomAnchor),
            chips.heightAnchor.constraint(equalTo: scroller.frameLayoutGuide.heightAnchor),
            title.leadingAnchor.constraint(equalTo: box.leadingAnchor, constant: 20),
            title.trailingAnchor.constraint(equalTo: box.trailingAnchor, constant: -20),
            title.topAnchor.constraint(equalTo: scroller.bottomAnchor, constant: 18),
        ])
        return box
    }

    @objc private func editChip(_ g: UILongPressGestureRecognizer) {
        guard g.state == .began, let v = g.view, v.tag < browser.spaces.count else { return }
        let s = browser.spaces[v.tag]
        if s.id != Browser.privateSpace { editSpace(s) }
    }

    @objc private func editTitle(_ g: UILongPressGestureRecognizer) {
        if g.state == .began, browser.activeSpace.id != Browser.privateSpace { editSpace(browser.activeSpace) }
    }

    /// New space (nil) or rename / recolour / delete an existing one.
    private func editSpace(_ space: Space?) {
        guard let host = main?.view else { return }
        let name = Sheet.field(L("space_name"), text: space.map { browser.spaceName($0) } ?? "")
        let used = Set(browser.spaces.map { $0.color })
        let swatches = Sheet.Swatches(Mica.palette, selected: space?.color ?? (Mica.palette.first { !used.contains($0) } ?? Mica.palette[0]))
        var buttons: [Sheet.Button] = []
        if let space, browser.spaces.filter({ $0.id != Browser.privateSpace }).count > 1 {
            buttons.append(Sheet.Button(title: L("delete"), destructive: true) { [weak self] in self?.browser.deleteSpace(space); self?.refresh(); return true })
        }
        buttons.append(Sheet.Button(title: L("cancel")) { true })
        buttons.append(Sheet.Button(title: L("save"), primary: true) { [weak self] in
            guard let self else { return true }
            let n = (name.text ?? "").trimmingCharacters(in: .whitespaces)
            if n.isEmpty { return false }
            if let space { self.browser.editSpace(space, name: n, color: swatches.selected) } else { self.browser.addSpace(name: n, color: swatches.selected) }
            self.refresh()
            self.main?.refreshChrome()
            return true
        })
        Sheet.form(L(space == nil ? "new_space" : "rename_space"), content: [name, swatches], buttons: buttons, in: host)
    }

    // MARK: Footer: everything else

    private func footer() -> UIView {
        let col = UIStackView()
        col.axis = .vertical
        col.spacing = 0
        func item(_ glyph: String, _ key: String, _ color: UIColor, _ run: @escaping () -> Void) {
            let r = PressRow(radius: 12) { run() }
            let g = makeLabel(glyph, 16, color)
            g.textAlignment = .center
            let t = makeLabel(L(key), 16, Mica.ink)
            let row = UIStackView(arrangedSubviews: [g, t])
            row.spacing = 12
            row.isUserInteractionEnabled = false
            g.widthAnchor.constraint(equalToConstant: 24).isActive = true
            r.addSubview(row)
            row.pin(to: r, insets: UIEdgeInsets(top: 11, left: 12, bottom: 11, right: 12))
            col.addArrangedSubview(r)
        }
        item("+", "new_tab", Mica.accent) { [weak self] in self?.close(); self?.main?.newTab() }
        item("◐", "new_private_tab", Mica.muted) { [weak self] in self?.close(); self?.main?.newPrivateTab() }
        let line = UIView()
        line.backgroundColor = Mica.line
        line.heightAnchor.constraint(equalToConstant: 0.5).isActive = true
        col.addArrangedSubview(line)
        col.setCustomSpacing(6, after: line)
        item("◆", "games", Mica.muted) { [weak self] in self?.close(); self?.main?.navigateActive("mica://games") }
        item("▤", "library", Mica.muted) { [weak self] in self?.close(); self?.main?.openLibrary() }
        item("❏", "sessions", Mica.muted) { [weak self] in self?.close(); self?.main?.sessionsMenu() }
        item("⚿", "passwords_sync", Mica.muted) { [weak self] in self?.close(); self?.main?.openPasswords() }
        item("☰", "settings", Mica.muted) { [weak self] in self?.close(); self?.main?.openSettings() }
        let box = UIView()
        box.addSubview(col)
        col.pin(to: box, insets: UIEdgeInsets(top: 16, left: 8, bottom: 30, right: 8))
        let size = box.systemLayoutSizeFitting(CGSize(width: width, height: 0), withHorizontalFittingPriority: .required, verticalFittingPriority: .fittingSizeLevel)
        box.frame = CGRect(x: 0, y: 0, width: width, height: size.height)
        return box
    }

    // MARK: Tabs

    func numberOfSections(in tableView: UITableView) -> Int { 2 }
    func tableView(_ t: UITableView, numberOfRowsInSection s: Int) -> Int { s == 0 ? pinned.count : rest.count }

    func tableView(_ t: UITableView, viewForHeaderInSection s: Int) -> UIView? {
        if s == 0 && pinned.isEmpty { return nil }
        let l = makeLabel(L(s == 0 ? "pinned" : "tabs_header").uppercased(), 11, Mica.faint, weight: .medium)
        let box = UIView()
        box.addSubview(l)
        l.pin(to: box, insets: UIEdgeInsets(top: 12, left: 20, bottom: 6, right: 20))
        return box
    }

    func tableView(_ t: UITableView, heightForHeaderInSection s: Int) -> CGFloat { s == 0 && pinned.isEmpty ? 0 : UITableView.automaticDimension }

    func tableView(_ t: UITableView, cellForRowAt ip: IndexPath) -> UITableViewCell {
        let c = t.dequeueReusableCell(withIdentifier: "tab", for: ip) as! TabCell
        let tab = (ip.section == 0 ? pinned : rest)[ip.row]
        c.show(tab, active: tab === browser.active)
        return c
    }

    func tableView(_ t: UITableView, didSelectRowAt ip: IndexPath) {
        let tab = (ip.section == 0 ? pinned : rest)[ip.row]
        close()
        browser.activate(tab)
    }

    func tableView(_ t: UITableView, trailingSwipeActionsConfigurationForRowAt ip: IndexPath) -> UISwipeActionsConfiguration? {
        let tab = (ip.section == 0 ? pinned : rest)[ip.row]
        let a = UIContextualAction(style: .destructive, title: L("close")) { [weak self] _, _, done in
            self?.browser.close(tab); self?.refresh(); done(true)
        }
        return UISwipeActionsConfiguration(actions: [a])
    }

    func tableView(_ t: UITableView, contextMenuConfigurationForRowAt ip: IndexPath, point: CGPoint) -> UIContextMenuConfiguration? {
        let tab = (ip.section == 0 ? pinned : rest)[ip.row]
        return UIContextMenuConfiguration(identifier: nil, previewProvider: nil) { [weak self] _ in self?.main?.tabMenu(tab) { self?.refresh() } }
    }
}

final class TabCell: UITableViewCell {
    private let dot = UIView()
    private let title = makeLabel("", 15)
    private let pill = UIView()

    override init(style: UITableViewCell.CellStyle, reuseIdentifier: String?) {
        super.init(style: style, reuseIdentifier: reuseIdentifier)
        backgroundColor = .clear
        selectionStyle = .none
        pill.round(12)
        contentView.addSubview(pill)
        dot.round(3)
        for v in [dot, title] as [UIView] { contentView.addSubview(v); v.translatesAutoresizingMaskIntoConstraints = false }
        pill.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            pill.leadingAnchor.constraint(equalTo: contentView.leadingAnchor, constant: 10),
            pill.trailingAnchor.constraint(equalTo: contentView.trailingAnchor, constant: -10),
            pill.topAnchor.constraint(equalTo: contentView.topAnchor, constant: 1),
            pill.bottomAnchor.constraint(equalTo: contentView.bottomAnchor, constant: -1),
            dot.leadingAnchor.constraint(equalTo: contentView.leadingAnchor, constant: 22),
            dot.centerYAnchor.constraint(equalTo: contentView.centerYAnchor),
            dot.widthAnchor.constraint(equalToConstant: 6), dot.heightAnchor.constraint(equalToConstant: 6),
            title.leadingAnchor.constraint(equalTo: dot.trailingAnchor, constant: 12),
            title.trailingAnchor.constraint(equalTo: contentView.trailingAnchor, constant: -20),
            title.topAnchor.constraint(equalTo: contentView.topAnchor, constant: 12),
            title.bottomAnchor.constraint(equalTo: contentView.bottomAnchor, constant: -12),
        ])
    }

    required init?(coder: NSCoder) { fatalError() }

    func show(_ tab: Tab, active: Bool) {
        title.text = tab.title.isEmpty ? (tab.isHome ? L("new_tab") : Navigation.label(tab.url)) : tab.title
        let resting = tab.view == nil && !tab.isHome
        title.font = resting ? UIFont.italicSystemFont(ofSize: 15) : Mica.font(15)
        title.textColor = resting ? Mica.muted : Mica.ink
        dot.backgroundColor = tab.playing ? Mica.accent : active ? Mica.accent : Mica.faint
        pill.backgroundColor = active ? Mica.soft : .clear
        accessibilityLabel = title.text
        accessibilityTraits = active ? [.button, .selected] : .button
    }
}
