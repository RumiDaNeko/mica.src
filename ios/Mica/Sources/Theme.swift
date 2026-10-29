import UIKit

/// Desktop Mica's palette (muted lavender, charcoal ink, warm ivory), light and dark, as on Android.
enum Mica {
    static let canvas = dynamic(0xFAF9F6, 0x17151B)
    static let panel = dynamic(0xF4F0F7, 0x211D27)
    static let glass = dynamic(0xF4F0F7, 0x262130, alpha: 0.92, darkAlpha: 0.9)
    static let soft = dynamic(0xECE7F2, 0x2A2433)
    static let card = dynamic(0xFFFFFF, 0x2E2838)
    static let ink = dynamic(0x2A2530, 0xF7F4FA)
    static let muted = dynamic(0x716B7A, 0xA69AAA)
    static let faint = dynamic(0xA69AAA, 0x7A7084)
    static let line = dynamic(0xD1C2E0, 0x3A3344)
    static let accent = dynamic(0x71608C, 0xC8B4E0)
    static let accentSoft = dynamic(0xC4B4DA, 0x5A4D6E)
    static let scrim = dynamic(0x2A2530, 0x000000, alpha: 0.35, darkAlpha: 0.5)
    static let good = dynamic(0x3B7D4F, 0x7FD49A)

    /// Colours for spaces, tab groups and cookie jars.
    static let palette: [UInt32] = [0x71608C, 0x7C977D, 0xB0708A, 0x5F86A8, 0xB08A4F, 0x7A7084]

    static func dynamic(_ light: UInt32, _ dark: UInt32, alpha: CGFloat = 1, darkAlpha: CGFloat = 1) -> UIColor {
        UIColor { $0.userInterfaceStyle == .dark ? UIColor(rgb: dark, alpha: darkAlpha) : UIColor(rgb: light, alpha: alpha) }
    }

    /// A space's colour, lightened in dark mode so its dot stays visible.
    static func spaceColor(_ rgb: UInt32) -> UIColor {
        UIColor { $0.userInterfaceStyle == .dark ? UIColor(rgb: rgb).mixed(with: .white, 0.3) : UIColor(rgb: rgb) }
    }

    static func font(_ size: CGFloat, _ weight: UIFont.Weight = .regular) -> UIFont { .systemFont(ofSize: size, weight: weight) }
    static func serif(_ size: CGFloat) -> UIFont {
        if let d = UIFont.systemFont(ofSize: size).fontDescriptor.withDesign(.serif) { return UIFont(descriptor: d, size: size) }
        return UIFont(name: "Georgia", size: size) ?? .systemFont(ofSize: size)
    }
}

extension UIColor {
    convenience init(rgb: UInt32, alpha: CGFloat = 1) {
        self.init(red: CGFloat((rgb >> 16) & 0xFF) / 255, green: CGFloat((rgb >> 8) & 0xFF) / 255, blue: CGFloat(rgb & 0xFF) / 255, alpha: alpha)
    }

    /// Mixes [other] into this colour by t (0…1), keeping this alpha.
    func mixed(with other: UIColor, _ t: CGFloat) -> UIColor {
        var r1: CGFloat = 0, g1: CGFloat = 0, b1: CGFloat = 0, a1: CGFloat = 0
        var r2: CGFloat = 0, g2: CGFloat = 0, b2: CGFloat = 0, a2: CGFloat = 0
        getRed(&r1, green: &g1, blue: &b1, alpha: &a1)
        other.getRed(&r2, green: &g2, blue: &b2, alpha: &a2)
        return UIColor(red: r1 + (r2 - r1) * t, green: g1 + (g2 - g1) * t, blue: b1 + (b2 - b1) * t, alpha: a1)
    }

    var rgbValue: UInt32 {
        var r: CGFloat = 0, g: CGFloat = 0, b: CGFloat = 0, a: CGFloat = 0
        getRed(&r, green: &g, blue: &b, alpha: &a)
        return (UInt32(max(0, min(1, r)) * 255) << 16) | (UInt32(max(0, min(1, g)) * 255) << 8) | UInt32(max(0, min(1, b)) * 255)
    }

    /// A page's theme-color or background, as the island and Spotlight tint (desktop's TINT_SCRIPT on Android).
    static func css(_ value: String?) -> UIColor? {
        guard var s = value?.trimmingCharacters(in: CharacterSet(charactersIn: "\" ")).lowercased(), !s.isEmpty else { return nil }
        if s.hasPrefix("#") {
            s.removeFirst()
            if s.count == 3 { s = s.map { "\($0)\($0)" }.joined() }
            guard s.count >= 6, let v = UInt32(s.prefix(6), radix: 16) else { return nil }
            return UIColor(rgb: v)
        }
        let nums = s.components(separatedBy: CharacterSet(charactersIn: "0123456789.").inverted).compactMap { Double($0) }
        guard s.hasPrefix("rgb"), nums.count >= 3 else { return nil }
        if nums.count >= 4 && nums[3] < 0.5 { return nil }
        return UIColor(red: nums[0] / 255, green: nums[1] / 255, blue: nums[2] / 255, alpha: 1)
    }
}

/// The text for a key in Localizable.strings (the Android string names), formatted with args.
func L(_ key: String, _ args: CVarArg...) -> String {
    let format = Bundle.main.localizedString(forKey: key, value: key, table: nil)
    return args.isEmpty ? format : String(format: format, locale: Locale.current, arguments: args)
}

extension UIView {
    @discardableResult func round(_ radius: CGFloat, border: UIColor? = nil, width: CGFloat = 0.75) -> Self {
        layer.cornerRadius = radius
        layer.cornerCurve = .continuous
        if let border { layer.borderColor = border.cgColor; layer.borderWidth = width }
        return self
    }

    func pin(to other: UIView, insets: UIEdgeInsets = .zero) {
        translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            leadingAnchor.constraint(equalTo: other.leadingAnchor, constant: insets.left),
            trailingAnchor.constraint(equalTo: other.trailingAnchor, constant: -insets.right),
            topAnchor.constraint(equalTo: other.topAnchor, constant: insets.top),
            bottomAnchor.constraint(equalTo: other.bottomAnchor, constant: -insets.bottom),
        ])
    }
}

func makeLabel(_ text: String = "", _ size: CGFloat = 15, _ color: UIColor = Mica.ink, weight: UIFont.Weight = .regular, lines: Int = 1) -> UILabel {
    let l = UILabel()
    l.text = text
    l.font = Mica.font(size, weight)
    l.textColor = color
    l.numberOfLines = lines
    l.adjustsFontForContentSizeCategory = true
    return l
}

/// A row that highlights while pressed and runs [action] on tap.
class PressRow: UIControl {
    var action: (() -> Void)?
    private let highlight = UIView()

    init(radius: CGFloat = 14, action: (() -> Void)? = nil) {
        self.action = action
        super.init(frame: .zero)
        highlight.backgroundColor = Mica.accentSoft.withAlphaComponent(0.45)
        highlight.alpha = 0
        highlight.isUserInteractionEnabled = false
        highlight.round(radius)
        addSubview(highlight)
        highlight.pin(to: self)
        addTarget(self, action: #selector(tapped), for: .touchUpInside)
    }

    required init?(coder: NSCoder) { fatalError() }

    override var isHighlighted: Bool {
        didSet { UIView.animate(withDuration: isHighlighted ? 0.05 : 0.25) { self.highlight.alpha = self.isHighlighted ? 1 : 0 } }
    }

    override var isEnabled: Bool { didSet { alpha = isEnabled ? 1 : 0.45 } }

    @objc private func tapped() {
        UIImpactFeedbackGenerator(style: .light).impactOccurred(intensity: 0.5)
        action?()
    }
}

extension String {
    var html: String {
        replacingOccurrences(of: "&", with: "&amp;").replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;").replacingOccurrences(of: "\"", with: "&quot;")
    }

    /// As a JavaScript string literal.
    var js: String {
        let data = try? JSONSerialization.data(withJSONObject: [self], options: [.fragmentsAllowed])
        let array = data.flatMap { String(data: $0, encoding: .utf8) } ?? "[\"\"]"
        return String(array.dropFirst().dropLast())
    }
}
