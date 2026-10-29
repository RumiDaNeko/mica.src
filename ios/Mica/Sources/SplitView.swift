import UIKit
import WebKit

/// Two pages at once (desktop's split view): side by side when there's room, one above the other on a phone held
/// upright. Drag the divider to share the space; a touch in a pane makes it the one you're on (ringed).
final class SplitView: UIView {
    private(set) var first: UIView?
    private(set) var second: UIView?
    private let divider = UIView()
    private let grip = UIView()
    private let ring = UIView()
    private let onFocus: (Int) -> Void
    /// The first pane's share of the space.
    private var ratio: CGFloat = 0.5
    /// 0…1 while the second pane comes in or goes; 1 when settled.
    private var reveal: CGFloat = 1
    private var keepIndex = 0
    private let gap: CGFloat = 6

    var activeIndex = 0 { didSet { if oldValue != activeIndex { setNeedsLayout(); flashRing() } } }

    private var sideBySide: Bool { bounds.width > bounds.height * 0.9 || bounds.width >= 700 }

    init(onFocus: @escaping (Int) -> Void) {
        self.onFocus = onFocus
        super.init(frame: .zero)
        backgroundColor = Mica.panel
        grip.backgroundColor = Mica.faint
        grip.round(2)
        divider.addSubview(grip)
        divider.addGestureRecognizer(UIPanGestureRecognizer(target: self, action: #selector(dragged(_:))))
        ring.isUserInteractionEnabled = false
        ring.layer.borderColor = Mica.accent.cgColor
        ring.layer.borderWidth = 2
        ring.round(14)
        ring.alpha = 0
        let tap = UITapGestureRecognizer(target: self, action: #selector(touched(_:)))
        tap.cancelsTouchesInView = false
        tap.delegate = self
        addGestureRecognizer(tap)
    }

    required init?(coder: NSCoder) { fatalError() }

    func put(_ a: UIView, _ b: UIView) {
        if first !== a { first?.removeFromSuperview(); first = a }
        if second !== b { second?.removeFromSuperview(); second = b }
        for v in [a, b] where v.superview !== self {
            v.removeFromSuperview()
            v.translatesAutoresizingMaskIntoConstraints = true
            addSubview(v)
            v.layer.cornerRadius = 12
            v.layer.cornerCurve = .continuous
            v.clipsToBounds = true
        }
        if divider.superview == nil { addSubview(divider) }
        if ring.superview == nil { addSubview(ring) }
        bringSubviewToFront(divider)
        bringSubviewToFront(ring)
        setNeedsLayout()
    }

    /// The second pane slides in beside the first.
    func enter() {
        keepIndex = 0
        reveal = 0
        layoutIfNeeded()
        UIView.animate(withDuration: 0.38, delay: 0, usingSpringWithDamping: 0.88, initialSpringVelocity: 0) {
            self.reveal = 1
            self.layoutIfNeeded()
        }
    }

    /// The pane at [keeping] grows over the other one; then [done].
    func leave(keeping: Int, done: @escaping () -> Void) {
        keepIndex = keeping
        UIView.animate(withDuration: 0.3, delay: 0, options: [.curveEaseInOut]) {
            self.reveal = 0
            self.ring.alpha = 0
            self.layoutIfNeeded()
        } completion: { _ in done() }
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        guard let first, let second else { return }
        let b = bounds
        let g = gap * reveal
        let across = sideBySide
        let total = (across ? b.width : b.height) - g
        // The first pane's size: everything at reveal 0 (the second coming in, or the first kept), nothing when the
        // second is kept, its share once settled.
        let settled = total * ratio
        let a = max(0, min(total, keepIndex == 1 ? settled * reveal : settled * reveal + total * (1 - reveal)))
        if across {
            first.frame = CGRect(x: 0, y: 0, width: a, height: b.height)
            second.frame = CGRect(x: a + g, y: 0, width: max(0, total - a), height: b.height)
            divider.frame = CGRect(x: a - 12 + g / 2, y: 0, width: 24, height: b.height)
            grip.frame = CGRect(x: 10, y: b.height / 2 - 22, width: 4, height: 44)
        } else {
            first.frame = CGRect(x: 0, y: 0, width: b.width, height: a)
            second.frame = CGRect(x: 0, y: a + g, width: b.width, height: max(0, total - a))
            divider.frame = CGRect(x: 0, y: a - 12 + g / 2, width: b.width, height: 24)
            grip.frame = CGRect(x: b.width / 2 - 22, y: 10, width: 44, height: 4)
        }
        divider.alpha = reveal
        ring.frame = (activeIndex == 0 ? first.frame : second.frame).insetBy(dx: -1, dy: -1)
    }

    private func flashRing() {
        ring.alpha = 1
        UIView.animate(withDuration: 0.6, delay: 0.5, options: [.curveEaseOut]) { self.ring.alpha = 0.35 }
    }

    @objc private func dragged(_ g: UIPanGestureRecognizer) {
        guard reveal == 1 else { return }
        let p = g.location(in: self)
        let across = sideBySide
        let total = (across ? bounds.width : bounds.height) - gap
        ratio = max(0.2, min(0.8, (across ? p.x : p.y) / max(1, total)))
        setNeedsLayout()
    }

    @objc private func touched(_ g: UITapGestureRecognizer) {
        let p = g.location(in: self)
        if let s = second, s.frame.contains(p) { if activeIndex != 1 { onFocus(1) } }
        else if let f = first, f.frame.contains(p) { if activeIndex != 0 { onFocus(0) } }
    }
}

extension SplitView: UIGestureRecognizerDelegate {
    func gestureRecognizer(_ g: UIGestureRecognizer, shouldRecognizeSimultaneouslyWith other: UIGestureRecognizer) -> Bool { true }
}

/// Files a page hands over (WKDownload), saved to Documents/Downloads, which the Files app shows.
final class Downloads: NSObject, WKDownloadDelegate {
    static let shared = Downloads()
    var announce: ((String) -> Void)?
    private var names: [ObjectIdentifier: URL] = [:]

    func track(_ d: WKDownload) { d.delegate = self }

    func download(_ download: WKDownload, decideDestinationUsing response: URLResponse, suggestedFilename: String, completionHandler: @escaping (URL?) -> Void) {
        let name = suggestedFilename.replacingOccurrences(of: "/", with: "_")
        var dest = Files.downloads.appendingPathComponent(name.isEmpty ? "download" : name)
        let base = dest.deletingPathExtension().lastPathComponent, ext = dest.pathExtension
        var n = 1
        while FileManager.default.fileExists(atPath: dest.path) {
            n += 1
            dest = Files.downloads.appendingPathComponent(ext.isEmpty ? "\(base) (\(n))" : "\(base) (\(n)).\(ext)")
        }
        names[ObjectIdentifier(download)] = dest
        announce?(L("downloading_file", dest.lastPathComponent))
        completionHandler(dest)
    }

    func downloadDidFinish(_ download: WKDownload) {
        guard let dest = names.removeValue(forKey: ObjectIdentifier(download)) else { return }
        announce?(L("blob_saved", dest.lastPathComponent))
    }

    func download(_ download: WKDownload, didFailWithError error: Error, resumeData: Data?) {
        let dest = names.removeValue(forKey: ObjectIdentifier(download))
        announce?(L("blob_failed", dest?.lastPathComponent ?? ""))
    }

    /// Files downloaded so far, newest first.
    func list() -> [URL] {
        let fm = FileManager.default
        let files = (try? fm.contentsOfDirectory(at: Files.downloads, includingPropertiesForKeys: [.contentModificationDateKey])) ?? []
        return files.sorted {
            let a = (try? $0.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
            let b = (try? $1.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
            return a > b
        }
    }
}

/// Site icons for tabs and the home page: the page's own icon link (or /favicon.ico), kept small in Caches/Mica/icons.
enum Favicons {
    private static var memory: [String: UIImage] = [:]
    private static let dir = Files.dir("icons", cache: true)
    private static let script = """
    (()=>{const l=[...document.querySelectorAll('link[rel~="icon"],link[rel="apple-touch-icon"]')];
    let best=null,size=0;for(const e of l){const s=parseInt((e.sizes&&e.sizes.value||'').split('x')[0])||(e.rel.includes('apple')?180:16);
    if(s>=size&&s<=256){best=e.href;size=s}}return best||(location.origin+'/favicon.ico')})()
    """

    private static func file(_ host: String) -> URL { dir.appendingPathComponent(host.replacingOccurrences(of: "/", with: "_") + ".png") }

    static func icon(for host: String) -> UIImage? {
        let h = host.hasPrefix("www.") ? String(host.dropFirst(4)) : host
        if let m = memory[h] { return m }
        guard let d = try? Data(contentsOf: file(h)), let img = UIImage(data: d) else { return nil }
        memory[h] = img
        return img
    }

    static func fetch(for tab: Tab) {
        guard let v = tab.view, !tab.isPrivate, Navigation.isWeb(tab.url) else { return }
        var host = tab.host
        if host.hasPrefix("www.") { host = String(host.dropFirst(4)) }
        if let known = icon(for: host) { tab.icon = known; return }
        v.evaluateJavaScript(script) { raw, _ in
            guard let s = raw as? String, let u = URL(string: s), u.scheme == "https" || u.scheme == "http" else { return }
            URLSession.shared.dataTask(with: u) { data, _, _ in
                guard let data, let img = UIImage(data: data), img.size.width >= 16 else { return }
                let side: CGFloat = 64
                let small = UIGraphicsImageRenderer(size: CGSize(width: side, height: side)).image { _ in
                    img.draw(in: CGRect(x: 0, y: 0, width: side, height: side))
                }
                if let png = small.pngData() { Files.write(png, to: file(host)) }
                DispatchQueue.main.async { memory[host] = small; tab.icon = small }
            }.resume()
        }
    }
}
