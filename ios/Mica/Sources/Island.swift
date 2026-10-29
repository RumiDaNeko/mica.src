import UIKit

/// Desktop's status island, the only chrome while browsing: a frosted pill with the space's dot, the page's host, a dot
/// per tab and the loading progress. Tap for Spotlight, swipe sideways for the next tab, swipe up for the deck, long
/// press for page actions, tap the space dot for the rail. It shrinks to the dot while you read and widens for news.
final class Island: UIView {
    var onTap: (() -> Void)?
    var onSpaceDot: (() -> Void)?
    var onSwipe: ((Int) -> Void)?
    var onSwipeUp: (() -> Void)?
    var onLongPress: (() -> Void)?

    /// Set by the owner; the island animates it to fit what it shows.
    var widthConstraint: NSLayoutConstraint?
    static let height: CGFloat = 44

    private let blur = UIVisualEffectView(effect: UIBlurEffect(style: .systemThinMaterial))
    private let tintLayer = UIView()
    private let dot = UIView()
    private let text = UILabel()
    private let outgoing = UILabel()
    private let dots = CAShapeLayer()
    private let activeDot = CAShapeLayer()
    private let progressBar = UIView()
    private let playingMark = UILabel()

    private var labelText = ""
    private var news: String?
    private var newsTimer: Timer?
    private(set) var shrunk = false
    private var tabCount = 1
    private var activeIndex = 0
    private var dragX: CGFloat = 0

    private let padX: CGFloat = 16
    private let dotSize: CGFloat = 10
    private let tabGap: CGFloat = 8
    private let maxDots = 7

    override init(frame: CGRect) {
        super.init(frame: frame)
        layer.cornerRadius = Island.height / 2
        layer.cornerCurve = .continuous
        layer.shadowColor = UIColor.black.cgColor
        layer.shadowOpacity = 0.14
        layer.shadowRadius = 12
        layer.shadowOffset = CGSize(width: 0, height: 4)

        blur.clipsToBounds = true
        blur.layer.cornerRadius = Island.height / 2
        blur.layer.cornerCurve = .continuous
        blur.layer.borderWidth = 0.75
        blur.layer.borderColor = Mica.line.cgColor
        addSubview(blur)
        blur.pin(to: self)
        tintLayer.isUserInteractionEnabled = false
        blur.contentView.addSubview(tintLayer)
        tintLayer.pin(to: blur.contentView)
        let glass = UIView()
        glass.backgroundColor = Mica.glass.withAlphaComponent(0.55)
        glass.isUserInteractionEnabled = false
        blur.contentView.insertSubview(glass, at: 0)
        glass.pin(to: blur.contentView)

        dot.round(dotSize / 2)
        blur.contentView.addSubview(dot)

        for l in [text, outgoing] {
            l.font = Mica.font(14, .medium)
            l.textColor = Mica.ink
            l.lineBreakMode = .byTruncatingMiddle
            blur.contentView.addSubview(l)
        }
        outgoing.alpha = 0

        dots.fillColor = Mica.faint.cgColor
        activeDot.fillColor = Mica.accent.cgColor
        blur.contentView.layer.addSublayer(dots)
        blur.contentView.layer.addSublayer(activeDot)

        playingMark.text = "♪"
        playingMark.font = Mica.font(12, .semibold)
        playingMark.textColor = Mica.accent
        playingMark.isHidden = true
        blur.contentView.addSubview(playingMark)

        progressBar.backgroundColor = Mica.accent
        progressBar.alpha = 0
        blur.contentView.addSubview(progressBar)

        isAccessibilityElement = true
        accessibilityTraits = .button
        accessibilityLabel = L("search_hint")

        addGestureRecognizer(UITapGestureRecognizer(target: self, action: #selector(tapped(_:))))
        let long = UILongPressGestureRecognizer(target: self, action: #selector(longPressed(_:)))
        long.minimumPressDuration = 0.4
        addGestureRecognizer(long)
        addGestureRecognizer(UIPanGestureRecognizer(target: self, action: #selector(panned(_:))))
    }

    required init?(coder: NSCoder) { fatalError() }

    override func traitCollectionDidChange(_ previous: UITraitCollection?) {
        super.traitCollectionDidChange(previous)
        blur.layer.borderColor = Mica.line.resolvedColor(with: traitCollection).cgColor
        dots.fillColor = Mica.faint.resolvedColor(with: traitCollection).cgColor
        activeDot.fillColor = Mica.accent.resolvedColor(with: traitCollection).cgColor
    }

    // MARK: State

    func update(label: String, spaceColor: UIColor, tabCount: Int, activeIndex: Int, playing: Bool, tint: UIColor?) {
        let changed = label != labelText
        labelText = label
        dot.backgroundColor = spaceColor
        self.tabCount = tabCount
        self.activeIndex = activeIndex
        playingMark.isHidden = !playing
        tintLayer.backgroundColor = tint?.withAlphaComponent(0.22)
        if news == nil { text.text = label }
        accessibilityValue = label
        if changed { resize() } else { setNeedsLayout() }
    }

    /// The tab changed by a swipe: the old label slides out as the new one slides in.
    func slide(to label: String, dir: Int) {
        outgoing.text = text.text
        outgoing.alpha = 1
        outgoing.transform = CGAffineTransform(translationX: dragX, y: 0)
        let w = bounds.width * 0.5
        text.text = news ?? label
        labelText = label
        text.transform = CGAffineTransform(translationX: CGFloat(dir) * w, y: 0)
        text.alpha = 0
        dragX = 0
        UIView.animate(withDuration: 0.28, delay: 0, options: [.curveEaseOut]) {
            self.outgoing.transform = CGAffineTransform(translationX: -CGFloat(dir) * w, y: 0)
            self.outgoing.alpha = 0
            self.text.transform = .identity
            self.text.alpha = 1
        }
        resize()
    }

    func setProgress(_ p: Double) {
        let loading = p > 0 && p < 1
        let w = bounds.width * CGFloat(max(0.05, min(1, p)))
        UIView.animate(withDuration: loading ? 0.2 : 0.35, delay: 0, options: [.beginFromCurrentState, .curveEaseOut]) {
            self.progressBar.frame = CGRect(x: 0, y: self.bounds.height - 2.5, width: loading ? w : self.bounds.width, height: 2.5)
            self.progressBar.alpha = loading ? 0.9 : 0
        }
    }

    /// News for three seconds ("12 blocked", "Saved for later"); the island widens for it.
    func announce(_ s: String) {
        news = s
        text.text = s
        newsTimer?.invalidate()
        newsTimer = Timer.scheduledTimer(withTimeInterval: 3, repeats: false) { [weak self] _ in
            guard let self else { return }
            self.news = nil
            UIView.transition(with: self.text, duration: 0.2, options: .transitionCrossDissolve) { self.text.text = self.labelText }
            self.resize()
        }
        UIAccessibility.post(notification: .announcement, argument: s)
        if shrunk { setShrunk(false) } else { resize() }
    }

    func setShrunk(_ value: Bool) {
        guard shrunk != value else { return }
        shrunk = value
        resize()
    }

    private var dotsWidth: CGFloat { tabCount <= 1 ? 0 : CGFloat(min(tabCount, maxDots)) * tabGap + 10 }

    private func fullWidth() -> CGFloat {
        let maxW = (superview?.bounds.width ?? 390) * 0.82
        let shown = (text.text ?? "") as NSString
        let textW = min(shown.size(withAttributes: [.font: text.font as Any]).width.rounded(.up), maxW / 2 + 40)
        return max(120, min(maxW, padX + dotSize + 10 + textW + dotsWidth + (playingMark.isHidden ? 0 : 16) + padX))
    }

    private func resize() {
        let target = shrunk ? Island.height : fullWidth()
        guard let c = widthConstraint, abs(c.constant - target) > 0.5 else { setNeedsLayout(); return }
        c.constant = target
        UIView.animate(withDuration: 0.3, delay: 0, usingSpringWithDamping: 0.9, initialSpringVelocity: 0, options: [.beginFromCurrentState, .allowUserInteraction]) {
            self.superview?.layoutIfNeeded()
        }
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        let h = bounds.height, w = bounds.width
        let shrinkT: CGFloat = shrunk ? 1 : 0
        let dotX = shrunk ? (w - dotSize) / 2 : padX
        dot.frame = CGRect(x: dotX, y: (h - dotSize) / 2, width: dotSize, height: dotSize)
        let textX = padX + dotSize + 10
        let right = w - padX - dotsWidth - (playingMark.isHidden ? 0 : 16)
        let tw = max(0, right - textX)
        text.frame = CGRect(x: textX, y: 0, width: tw, height: h)
        outgoing.frame = text.frame
        text.alpha = shrunk ? 0 : (text.transform == .identity ? 1 : text.alpha)
        playingMark.frame = CGRect(x: right + 2, y: 0, width: 14, height: h)
        // A dot per tab (up to seven), the one you're on in the accent colour.
        let path = UIBezierPath(), act = UIBezierPath()
        if tabCount > 1 && !shrunk {
            let n = min(tabCount, maxDots)
            let start = w - padX - CGFloat(n) * tabGap + tabGap / 2
            let shownActive = tabCount <= maxDots ? activeIndex : Int((Double(activeIndex) / Double(max(1, tabCount - 1)) * Double(n - 1)).rounded())
            for i in 0..<n {
                let r: CGFloat = i == shownActive ? 3 : 2.25
                let c = CGPoint(x: start + CGFloat(i) * tabGap, y: h / 2)
                (i == shownActive ? act : path).append(UIBezierPath(arcCenter: c, radius: r, startAngle: 0, endAngle: .pi * 2, clockwise: true))
            }
        }
        dots.path = path.cgPath
        activeDot.path = act.cgPath
        dots.opacity = Float(1 - shrinkT)
        activeDot.opacity = Float(1 - shrinkT)
        if progressBar.alpha == 0 { progressBar.frame = CGRect(x: 0, y: h - 2.5, width: 0, height: 2.5) }
    }

    // MARK: Gestures

    @objc private func tapped(_ g: UITapGestureRecognizer) {
        UIImpactFeedbackGenerator(style: .light).impactOccurred(intensity: 0.6)
        if shrunk { setShrunk(false); return }
        if g.location(in: self).x < padX + dotSize + 8 { onSpaceDot?() } else { onTap?() }
    }

    @objc private func longPressed(_ g: UILongPressGestureRecognizer) {
        guard g.state == .began else { return }
        UIImpactFeedbackGenerator(style: .medium).impactOccurred()
        onLongPress?()
    }

    @objc private func panned(_ g: UIPanGestureRecognizer) {
        let t = g.translation(in: self)
        switch g.state {
        case .changed:
            if abs(t.x) > abs(t.y) {
                dragX = t.x * 0.6
                text.transform = CGAffineTransform(translationX: dragX, y: 0)
            }
        case .ended, .cancelled:
            let v = g.velocity(in: self)
            if -t.y > 30 && abs(t.y) > abs(t.x) || v.y < -600 && abs(v.y) > abs(v.x) {
                resetDrag()
                UIImpactFeedbackGenerator(style: .light).impactOccurred()
                onSwipeUp?()
            } else if abs(t.x) > 50 || abs(v.x) > 500 {
                UISelectionFeedbackGenerator().selectionChanged()
                onSwipe?(t.x < 0 ? 1 : -1)
                if text.transform != .identity && outgoing.alpha == 0 { resetDrag() }
            } else { resetDrag() }
        default: break
        }
    }

    private func resetDrag() {
        dragX = 0
        UIView.animate(withDuration: 0.25, delay: 0, usingSpringWithDamping: 0.8, initialSpringVelocity: 0) { self.text.transform = .identity }
    }
}
