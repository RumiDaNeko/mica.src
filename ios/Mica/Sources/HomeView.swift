import UIKit

/// The new tab page (desktop's RenderHome): the time, a greeting, the sites you visit most and the tabs you closed
/// last. Tap anywhere else for Spotlight. In private browsing it only says where you are.
final class HomeView: UIView {
    var onOpen: ((String) -> Void)?
    var onSpotlight: (() -> Void)?
    var onForget: ((HistoryEntry) -> Void)?

    private let column = UIStackView()
    private let time = UILabel()
    private let date = makeLabel("", 14, Mica.muted)
    private let greeting = makeLabel("", 17, Mica.accent)
    private let tiles = UIStackView()
    private let closedHead = makeLabel(L("recently_closed").uppercased(), 11, Mica.faint, weight: .medium)
    private let closed = UIStackView()
    private let closedScroll = UIScrollView()
    private var isPrivate = false
    private var timer: Timer?

    override init(frame: CGRect) {
        super.init(frame: frame)
        backgroundColor = Mica.canvas
        addGestureRecognizer(UITapGestureRecognizer(target: self, action: #selector(tapped)))
        time.font = Mica.serif(60)
        time.textColor = Mica.ink
        time.adjustsFontForContentSizeCategory = true
        for l in [time, date, greeting] { l.textAlignment = .center }
        tiles.axis = .vertical
        tiles.spacing = 14
        closed.axis = .horizontal
        closed.spacing = 8
        closedScroll.showsHorizontalScrollIndicator = false
        closedScroll.addSubview(closed)
        closed.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            closed.leadingAnchor.constraint(equalTo: closedScroll.contentLayoutGuide.leadingAnchor),
            closed.trailingAnchor.constraint(equalTo: closedScroll.contentLayoutGuide.trailingAnchor),
            closed.topAnchor.constraint(equalTo: closedScroll.contentLayoutGuide.topAnchor),
            closed.bottomAnchor.constraint(equalTo: closedScroll.contentLayoutGuide.bottomAnchor),
            closedScroll.heightAnchor.constraint(equalTo: closed.heightAnchor),
        ])
        column.axis = .vertical
        column.alignment = .fill
        column.spacing = 4
        [time, date, greeting].forEach { column.addArrangedSubview($0) }
        column.setCustomSpacing(10, after: date)
        column.setCustomSpacing(30, after: greeting)
        column.addArrangedSubview(tiles)
        column.setCustomSpacing(26, after: tiles)
        column.addArrangedSubview(closedHead)
        column.setCustomSpacing(8, after: closedHead)
        column.addArrangedSubview(closedScroll)
        addSubview(column)
        column.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            column.leadingAnchor.constraint(equalTo: readableContentGuide.leadingAnchor, constant: 8),
            column.trailingAnchor.constraint(equalTo: readableContentGuide.trailingAnchor, constant: -8),
            column.centerYAnchor.constraint(equalTo: centerYAnchor, constant: -40),
        ])
    }

    required init?(coder: NSCoder) { fatalError() }

    @objc private func tapped() { onSpotlight?() }

    override func didMoveToWindow() {
        super.didMoveToWindow()
        if window == nil { timer?.invalidate(); timer = nil } else { scheduleTick() }
    }

    /// Fills it in again (on showing a new tab, after a change of space, a visit or a removal).
    func render(isPrivate: Bool, top: [HistoryEntry], closedTabs: [HistoryEntry]) {
        self.isPrivate = isPrivate
        showTime()
        tiles.arrangedSubviews.forEach { $0.removeFromSuperview() }
        closed.arrangedSubviews.forEach { $0.removeFromSuperview() }
        var delay: TimeInterval = 0
        func rise(_ v: UIView) {
            // Each piece settles in a beat after the one before.
            v.alpha = 0
            v.transform = CGAffineTransform(translationX: 0, y: 10)
            UIView.animate(withDuration: 0.32, delay: delay, usingSpringWithDamping: 0.9, initialSpringVelocity: 0) {
                v.alpha = 1; v.transform = .identity
            }
            delay += 0.035
        }
        [time, date, greeting].forEach { rise($0) }
        if isPrivate {
            let l = makeLabel(L("private_top_sites"), 14, Mica.muted, lines: 0)
            l.textAlignment = .center
            tiles.addArrangedSubview(l)
            closedHead.isHidden = true; closedScroll.isHidden = true
            return
        }
        if top.isEmpty {
            let l = makeLabel(L("top_sites_empty"), 14, Mica.muted, lines: 0)
            l.textAlignment = .center
            tiles.addArrangedSubview(l)
        }
        let perRow = traitCollection.horizontalSizeClass == .regular ? 8 : 4
        stride(from: 0, to: top.count, by: perRow).forEach { start in
            let row = UIStackView()
            row.axis = .horizontal
            row.distribution = .fillEqually
            row.alignment = .top
            for e in top[start..<min(top.count, start + perRow)] { row.addArrangedSubview(tile(e).withRise(rise)) }
            for _ in 0..<(perRow - min(perRow, top.count - start)) { row.addArrangedSubview(UIView()) }
            tiles.addArrangedSubview(row)
        }
        let recent = closedTabs.filter { Navigation.isWeb($0.url) }.prefix(3)
        closedHead.isHidden = recent.isEmpty
        closedScroll.isHidden = recent.isEmpty
        for e in recent {
            let chip = PressRow(radius: 16) { [weak self] in self?.onOpen?(e.url) }
            chip.backgroundColor = Mica.card
            chip.round(16, border: Mica.line)
            let l = makeLabel(e.title.isEmpty ? Navigation.label(e.url) : e.title, 13)
            l.lineBreakMode = .byTruncatingTail
            l.isUserInteractionEnabled = false
            chip.addSubview(l)
            l.pin(to: chip, insets: UIEdgeInsets(top: 8, left: 12, bottom: 8, right: 12))
            l.widthAnchor.constraint(lessThanOrEqualToConstant: 190).isActive = true
            closed.addArrangedSubview(chip)
            rise(chip)
        }
    }

    private func tile(_ e: HistoryEntry) -> UIView {
        var host = Navigation.label(e.url)
        if host.hasPrefix("www.") { host = String(host.dropFirst(4)) }
        let site = Domains.registrable(host)
        let name = (site.split(separator: ".").first.map(String.init) ?? site).capitalized
        let cell = PressRow(radius: 14) { [weak self] in self?.onOpen?(e.url) }
        let face = UIView()
        face.backgroundColor = Mica.card
        face.round(16, border: Mica.line)
        face.isUserInteractionEnabled = false
        if let icon = Favicons.icon(for: host) {
            let img = UIImageView(image: icon)
            img.contentMode = .scaleAspectFit
            face.addSubview(img)
            img.pin(to: face, insets: UIEdgeInsets(top: 15, left: 15, bottom: 15, right: 15))
        } else {
            let letter = makeLabel(String(name.prefix(1)), 20, Mica.spaceColor(Mica.palette[host.unicodeScalars.reduce(0) { ($0 &* 31 &+ Int($1.value)) & 0xFFFF } % Mica.palette.count]), weight: .bold)
            letter.textAlignment = .center
            face.addSubview(letter)
            letter.pin(to: face)
        }
        let caption = makeLabel(name, 12, Mica.muted)
        caption.textAlignment = .center
        caption.isUserInteractionEnabled = false
        for v in [face, caption] { cell.addSubview(v); v.translatesAutoresizingMaskIntoConstraints = false }
        NSLayoutConstraint.activate([
            face.topAnchor.constraint(equalTo: cell.topAnchor, constant: 6),
            face.centerXAnchor.constraint(equalTo: cell.centerXAnchor),
            face.widthAnchor.constraint(equalToConstant: 56), face.heightAnchor.constraint(equalToConstant: 56),
            caption.topAnchor.constraint(equalTo: face.bottomAnchor, constant: 6),
            caption.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 2),
            caption.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -2),
            caption.bottomAnchor.constraint(equalTo: cell.bottomAnchor, constant: -6),
        ])
        cell.accessibilityLabel = L("open_site_named", host)
        cell.addInteraction(UIContextMenuInteraction(delegate: TileMenu.shared))
        TileMenu.shared.entries[ObjectIdentifier(cell)] = (e, { [weak self] in self?.onForget?(e) })
        return cell
    }

    private func showTime() {
        let now = Date()
        time.text = DateFormatter.localizedString(from: now, dateStyle: .none, timeStyle: .short)
        date.text = DateFormatter.localizedString(from: now, dateStyle: .full, timeStyle: .none)
        if isPrivate { greeting.text = L("private_greeting"); return }
        let h = Calendar.current.component(.hour, from: now)
        greeting.text = L(h < 5 ? "good_night" : h < 12 ? "good_morning" : h < 18 ? "good_afternoon" : "good_evening")
    }

    /// The clock moves on the minute, only while the page is showing.
    private func scheduleTick() {
        timer?.invalidate()
        guard window != nil, !isHidden else { return }
        let next = 60 - Date().timeIntervalSince1970.truncatingRemainder(dividingBy: 60) + 0.05
        timer = Timer.scheduledTimer(withTimeInterval: next, repeats: false) { [weak self] _ in self?.showTime(); self?.scheduleTick() }
    }

    override var isHidden: Bool { didSet { if !isHidden { scheduleTick() } } }
}

private extension UIView {
    func withRise(_ rise: (UIView) -> Void) -> UIView { rise(self); return self }
}

/// Long press on a home tile: take the site off the home page.
private final class TileMenu: NSObject, UIContextMenuInteractionDelegate {
    static let shared = TileMenu()
    var entries: [ObjectIdentifier: (HistoryEntry, () -> Void)] = [:]

    func contextMenuInteraction(_ interaction: UIContextMenuInteraction, configurationForMenuAtLocation location: CGPoint) -> UIContextMenuConfiguration? {
        guard let view = interaction.view, let (_, forget) = entries[ObjectIdentifier(view)] else { return nil }
        return UIContextMenuConfiguration(identifier: nil, previewProvider: nil) { _ in
            UIMenu(children: [UIAction(title: L("remove_top_site"), image: UIImage(systemName: "minus.circle"), attributes: .destructive) { _ in forget() }])
        }
    }
}
