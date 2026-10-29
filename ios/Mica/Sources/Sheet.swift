import UIKit

/// Mica's card rising from the bottom (android Sheet and Bubble): menus, small forms and the page's own dialogs.
/// A tap outside closes a menu; a form closes from its buttons. [unanswered] runs once if it closes without an answer.
final class Sheet: UIView {
    struct Item {
        var title: String
        var detail: String? = nil
        var checked = false
        var destructive = false
        var enabled = true
        var action: () -> Void
    }

    struct Button {
        var title: String
        var primary = false
        var destructive = false
        /// Returns false to keep the sheet open (a form that isn't filled in yet).
        var action: () -> Bool
    }

    private let scrim = UIControl()
    private let card = UIView()
    private let stack = UIStackView()
    private let scroll = UIScrollView()
    private var unanswered: (() -> Void)?
    private var answered = false
    private var closable = true
    private(set) var showing = false
    private static weak var current: Sheet?

    static var isShowing: Bool { current?.showing == true }
    static func dismissCurrent() { current?.dismiss() }

    // MARK: Building

    /// A list of actions under a heading.
    @discardableResult
    static func menu(_ title: String?, detail: String? = nil, items: [Item], in host: UIView, unanswered: (() -> Void)? = nil) -> Sheet {
        let s = Sheet(host: host)
        s.unanswered = unanswered
        s.heading(title, detail)
        for item in items { s.stack.addArrangedSubview(s.row(item)) }
        s.present()
        return s
    }

    /// A form: [content] (fields, swatches, text) with buttons along the bottom.
    @discardableResult
    static func form(_ title: String?, detail: String? = nil, content: [UIView], buttons: [Button], in host: UIView,
                     closable: Bool = true, unanswered: (() -> Void)? = nil) -> Sheet {
        let s = Sheet(host: host)
        s.unanswered = unanswered
        s.closable = closable
        s.heading(title, detail)
        for c in content { s.stack.addArrangedSubview(c) }
        let bar = UIStackView()
        bar.axis = .horizontal
        bar.spacing = 8
        bar.alignment = .center
        bar.addArrangedSubview(UIView())
        for b in buttons {
            let button = UIButton(type: .system)
            var cfg = b.primary ? UIButton.Configuration.filled() : UIButton.Configuration.plain()
            cfg.title = b.title
            cfg.cornerStyle = .capsule
            cfg.baseBackgroundColor = Mica.accentSoft
            cfg.baseForegroundColor = b.destructive ? .systemRed : (b.primary ? Mica.ink : Mica.accent)
            cfg.contentInsets = NSDirectionalEdgeInsets(top: 10, leading: 18, bottom: 10, trailing: 18)
            button.configuration = cfg
            button.addAction(UIAction { [weak s] _ in
                guard let s else { return }
                if b.action() { s.answered = true; s.dismiss() }
            }, for: .touchUpInside)
            bar.addArrangedSubview(button)
        }
        s.stack.setCustomSpacing(16, after: s.stack.arrangedSubviews.last ?? bar)
        s.stack.addArrangedSubview(bar)
        s.present()
        DispatchQueue.main.async { (content.first { $0 is UITextField } as? UITextField)?.becomeFirstResponder() }
        return s
    }

    private init(host: UIView) {
        super.init(frame: host.bounds)
        Sheet.current?.dismiss()
        Sheet.current = self
        autoresizingMask = [.flexibleWidth, .flexibleHeight]
        host.addSubview(self)

        scrim.backgroundColor = Mica.scrim
        scrim.alpha = 0
        scrim.addTarget(self, action: #selector(outside), for: .touchUpInside)
        addSubview(scrim)
        scrim.pin(to: self)

        card.backgroundColor = Mica.panel
        card.round(26, border: Mica.line)
        card.layer.shadowColor = UIColor.black.cgColor
        card.layer.shadowOpacity = 0.18
        card.layer.shadowRadius = 24
        card.translatesAutoresizingMaskIntoConstraints = false
        addSubview(card)

        scroll.showsVerticalScrollIndicator = false
        scroll.alwaysBounceVertical = false
        scroll.translatesAutoresizingMaskIntoConstraints = false
        card.addSubview(scroll)

        stack.axis = .vertical
        stack.spacing = 2
        stack.translatesAutoresizingMaskIntoConstraints = false
        scroll.addSubview(stack)

        let fit = scroll.heightAnchor.constraint(equalTo: stack.heightAnchor)
        fit.priority = .defaultHigh
        let wide = card.widthAnchor.constraint(equalTo: widthAnchor, constant: -20)
        wide.priority = .defaultHigh
        NSLayoutConstraint.activate([
            card.centerXAnchor.constraint(equalTo: centerXAnchor),
            wide,
            card.widthAnchor.constraint(lessThanOrEqualToConstant: 520),
            card.bottomAnchor.constraint(equalTo: keyboardLayoutGuide.topAnchor, constant: -10),
            card.topAnchor.constraint(greaterThanOrEqualTo: safeAreaLayoutGuide.topAnchor, constant: 16),
            scroll.leadingAnchor.constraint(equalTo: card.leadingAnchor, constant: 14),
            scroll.trailingAnchor.constraint(equalTo: card.trailingAnchor, constant: -14),
            scroll.topAnchor.constraint(equalTo: card.topAnchor, constant: 16),
            scroll.bottomAnchor.constraint(equalTo: card.bottomAnchor, constant: -12),
            fit,
            stack.leadingAnchor.constraint(equalTo: scroll.contentLayoutGuide.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: scroll.contentLayoutGuide.trailingAnchor),
            stack.topAnchor.constraint(equalTo: scroll.contentLayoutGuide.topAnchor),
            stack.bottomAnchor.constraint(equalTo: scroll.contentLayoutGuide.bottomAnchor),
            stack.widthAnchor.constraint(equalTo: scroll.frameLayoutGuide.widthAnchor),
        ])
        keyboardLayoutGuide.followsUndockedKeyboard = true
    }

    required init?(coder: NSCoder) { fatalError() }

    private func heading(_ title: String?, _ detail: String?) {
        guard title != nil || detail != nil else { return }
        let box = UIStackView()
        box.axis = .vertical
        box.spacing = 3
        box.isLayoutMarginsRelativeArrangement = true
        box.directionalLayoutMargins = NSDirectionalEdgeInsets(top: 2, leading: 6, bottom: 10, trailing: 6)
        if let title { box.addArrangedSubview(makeLabel(title, 17, Mica.ink, weight: .semibold, lines: 2)) }
        if let detail, !detail.isEmpty { box.addArrangedSubview(makeLabel(detail, 13, Mica.muted, lines: 4)) }
        stack.addArrangedSubview(box)
    }

    private func row(_ item: Item) -> UIView {
        let row = PressRow(radius: 14) { [weak self] in
            self?.answered = true
            self?.dismiss()
            item.action()
        }
        row.isEnabled = item.enabled
        let t = makeLabel(item.title, 16, item.destructive ? .systemRed : Mica.ink, lines: 2)
        let texts = UIStackView(arrangedSubviews: [t])
        texts.axis = .vertical
        texts.spacing = 2
        texts.isUserInteractionEnabled = false
        if let d = item.detail, !d.isEmpty { texts.addArrangedSubview(makeLabel(d, 12, Mica.muted, lines: 2)) }
        row.addSubview(texts)
        texts.translatesAutoresizingMaskIntoConstraints = false
        var cs = [
            texts.leadingAnchor.constraint(equalTo: row.leadingAnchor, constant: 14),
            texts.topAnchor.constraint(equalTo: row.topAnchor, constant: 11),
            texts.bottomAnchor.constraint(equalTo: row.bottomAnchor, constant: -11),
            row.heightAnchor.constraint(greaterThanOrEqualToConstant: 44),
        ]
        if item.checked {
            let check = makeLabel("✓", 16, Mica.accent, weight: .semibold)
            check.isUserInteractionEnabled = false
            check.translatesAutoresizingMaskIntoConstraints = false
            row.addSubview(check)
            cs += [check.trailingAnchor.constraint(equalTo: row.trailingAnchor, constant: -14), check.centerYAnchor.constraint(equalTo: row.centerYAnchor),
                   texts.trailingAnchor.constraint(lessThanOrEqualTo: check.leadingAnchor, constant: -8)]
        } else {
            cs.append(texts.trailingAnchor.constraint(lessThanOrEqualTo: row.trailingAnchor, constant: -14))
        }
        NSLayoutConstraint.activate(cs)
        row.accessibilityLabel = item.title
        row.accessibilityTraits = item.checked ? [.button, .selected] : .button
        return row
    }

    // MARK: Showing

    private func present() {
        showing = true
        layoutIfNeeded()
        card.transform = CGAffineTransform(translationX: 0, y: card.bounds.height + 40)
        UIView.animate(withDuration: 0.42, delay: 0, usingSpringWithDamping: 0.86, initialSpringVelocity: 0.2) {
            self.card.transform = .identity
            self.scrim.alpha = 1
        }
    }

    @objc private func outside() { if closable { dismiss() } }

    func dismiss() {
        guard showing else { return }
        showing = false
        endEditing(true)
        if !answered { answered = true; unanswered?() }
        UIView.animate(withDuration: 0.22, delay: 0, options: [.curveEaseIn]) {
            self.card.transform = CGAffineTransform(translationX: 0, y: self.card.bounds.height + 40)
            self.scrim.alpha = 0
        } completion: { _ in self.removeFromSuperview() }
    }

    // MARK: Pieces for forms

    static func field(_ placeholder: String, text: String = "", secure: Bool = false, url: Bool = false) -> UITextField {
        let f = PaddedField()
        f.placeholder = placeholder
        f.text = text
        f.isSecureTextEntry = secure
        f.font = Mica.font(16)
        f.textColor = Mica.ink
        f.backgroundColor = Mica.card
        f.round(14, border: Mica.line)
        f.autocorrectionType = .no
        f.autocapitalizationType = .none
        if url { f.keyboardType = .URL }
        f.heightAnchor.constraint(equalToConstant: 46).isActive = true
        return f
    }

    static func text(_ s: String, size: CGFloat = 14, color: UIColor = Mica.muted) -> UIView {
        let l = makeLabel(s, size, color, lines: 0)
        let box = UIView()
        box.addSubview(l)
        l.pin(to: box, insets: UIEdgeInsets(top: 0, left: 6, bottom: 6, right: 6))
        return box
    }

    /// A row of colour dots; [selected] changes as you tap.
    final class Swatches: UIStackView {
        private(set) var selected: UInt32
        private var buttons: [(UInt32, UIView)] = []
        init(_ colors: [UInt32], selected: UInt32) {
            self.selected = selected
            super.init(frame: .zero)
            axis = .horizontal
            spacing = 12
            isLayoutMarginsRelativeArrangement = true
            directionalLayoutMargins = NSDirectionalEdgeInsets(top: 10, leading: 6, bottom: 6, trailing: 6)
            for c in colors {
                let b = UIControl()
                b.backgroundColor = Mica.spaceColor(c)
                b.round(15)
                b.widthAnchor.constraint(equalToConstant: 30).isActive = true
                b.heightAnchor.constraint(equalToConstant: 30).isActive = true
                b.addAction(UIAction { [weak self] _ in self?.pick(c) }, for: .touchUpInside)
                b.accessibilityLabel = String(format: "#%06X", c)
                addArrangedSubview(b)
                buttons.append((c, b))
            }
            addArrangedSubview(UIView())
            pick(selected)
        }
        required init(coder: NSCoder) { fatalError() }
        private func pick(_ c: UInt32) {
            selected = c
            for (color, b) in buttons {
                b.layer.borderWidth = color == c ? 3 : 0
                b.layer.borderColor = Mica.ink.cgColor
            }
        }
    }
}

final class PaddedField: UITextField {
    override func textRect(forBounds b: CGRect) -> CGRect { b.insetBy(dx: 16, dy: 0) }
    override func editingRect(forBounds b: CGRect) -> CGRect { b.insetBy(dx: 16, dy: 0) }
    override func placeholderRect(forBounds b: CGRect) -> CGRect { b.insetBy(dx: 16, dy: 0) }
}
