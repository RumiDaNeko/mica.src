import UIKit
import WebKit

/// A quick look at a link without leaving the page (desktop's BrowserPeek): a card over a dimmed page. A tap
/// outside puts it away; "Open as tab" keeps it. The page inside can't open windows or apps, and only goes
/// elsewhere when you tap.
final class Peek: NSObject, WKNavigationDelegate, WKUIDelegate {
    private weak var main: MainViewController?
    private let overlay = UIControl()
    private let panel = UIView()
    private let titleLabel = makeLabel("", 14, weight: .semibold)
    private let address = makeLabel("", 12, Mica.muted)
    private let body = UIView()
    private var page: WKWebView?
    private var source: Tab?
    private var observers: [NSKeyValueObservation] = []
    private(set) var showing = false

    init(main: MainViewController) {
        self.main = main
        super.init()
    }

    private func build(in host: UIView) {
        guard overlay.superview == nil else { return }
        overlay.backgroundColor = Mica.scrim
        overlay.addTarget(self, action: #selector(hide), for: .touchUpInside)
        host.addSubview(overlay)
        overlay.pin(to: host)
        panel.backgroundColor = Mica.canvas
        panel.round(20)
        panel.clipsToBounds = true
        overlay.addSubview(panel)
        panel.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            panel.leadingAnchor.constraint(equalTo: overlay.safeAreaLayoutGuide.leadingAnchor, constant: 10),
            panel.trailingAnchor.constraint(equalTo: overlay.safeAreaLayoutGuide.trailingAnchor, constant: -10),
            panel.topAnchor.constraint(equalTo: overlay.safeAreaLayoutGuide.topAnchor, constant: 20),
            panel.bottomAnchor.constraint(equalTo: overlay.safeAreaLayoutGuide.bottomAnchor, constant: -6),
        ])
        titleLabel.lineBreakMode = .byTruncatingTail
        address.lineBreakMode = .byTruncatingMiddle
        let labels = UIStackView(arrangedSubviews: [titleLabel, address])
        labels.axis = .vertical
        labels.spacing = 2
        let open = UIButton(type: .system)
        open.setTitle(L("open_as_tab"), for: .normal)
        open.titleLabel?.font = Mica.font(13, .semibold)
        open.tintColor = Mica.accent
        open.addAction(UIAction { [weak self] _ in self?.openAsTab() }, for: .touchUpInside)
        let close = UIButton(type: .system)
        close.setImage(UIImage(systemName: "xmark"), for: .normal)
        close.tintColor = Mica.muted
        close.accessibilityLabel = L("close")
        close.addAction(UIAction { [weak self] _ in self?.hide() }, for: .touchUpInside)
        close.widthAnchor.constraint(equalToConstant: 40).isActive = true
        let head = UIStackView(arrangedSubviews: [labels, open, close])
        head.spacing = 8
        head.alignment = .center
        head.isLayoutMarginsRelativeArrangement = true
        head.directionalLayoutMargins = NSDirectionalEdgeInsets(top: 10, leading: 18, bottom: 8, trailing: 6)
        let column = UIStackView(arrangedSubviews: [head, body])
        column.axis = .vertical
        panel.addSubview(column)
        column.pin(to: panel)
        overlay.isHidden = true
    }

    func show(from: Tab, url: String) {
        guard let main else { return }
        build(in: main.view)
        drop()
        source = from
        titleLabel.text = Navigation.label(url)
        address.text = url
        let v = main.browser.peekView(source: from)
        v.navigationDelegate = self
        v.uiDelegate = self
        page = v
        body.addSubview(v)
        v.pin(to: body)
        observers = [
            v.observe(\.title) { [weak self] _, _ in self?.refresh() },
            v.observe(\.url) { [weak self] _, _ in self?.refresh() },
        ]
        if let u = URL(string: url) { v.load(URLRequest(url: u)) }
        overlay.superview?.bringSubviewToFront(overlay)
        if !showing {
            showing = true
            overlay.isHidden = false
            overlay.alpha = 0
            panel.transform = CGAffineTransform(translationX: 0, y: 60).scaledBy(x: 0.94, y: 0.94)
            UIView.animate(withDuration: 0.34, delay: 0, usingSpringWithDamping: 0.88, initialSpringVelocity: 0) {
                self.overlay.alpha = 1
                self.panel.transform = .identity
            }
        }
    }

    func refresh() {
        guard let v = page else { return }
        let at = Navigation.fromEngine(v.url?.absoluteString ?? "")
        if let t = v.title, !t.isEmpty, !t.hasPrefix("http") { titleLabel.text = t } else if !at.isEmpty { titleLabel.text = Navigation.label(at) }
        if !at.isEmpty { address.text = at }
    }

    @objc func hide() {
        guard showing else { return }
        showing = false
        UIView.animate(withDuration: 0.22, animations: {
            self.overlay.alpha = 0
            self.panel.transform = CGAffineTransform(translationX: 0, y: 40).scaledBy(x: 0.96, y: 0.96)
        }, completion: { _ in
            if !self.showing { self.overlay.isHidden = true; self.panel.transform = .identity; self.drop() }
        })
    }

    private func openAsTab() {
        guard let main, let u = page?.url?.absoluteString else { return }
        let url = Navigation.fromEngine(u)
        let from = source.flatMap { main.browser.tab($0.id) }
        showing = false
        overlay.isHidden = true
        drop()
        main.browser.open(url, parent: from, spaceId: from?.space ?? main.browser.activeSpace.id)
    }

    private func drop() {
        observers.removeAll()
        page?.stopLoading()
        page?.removeFromSuperview()
        page = nil
        source = nil
    }

    // MARK: Delegates

    func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        guard let url = action.request.url, let scheme = url.scheme?.lowercased() else { return decisionHandler(.cancel) }
        // Frames load freely; the page itself moves only on a tap, and never into another app.
        guard action.targetFrame?.isMainFrame != false else { return decisionHandler(scheme == "http" || scheme == "https" || scheme == "about" || scheme == "data" || scheme == "blob" ? .allow : .cancel) }
        guard scheme == "http" || scheme == "https" else { return decisionHandler(.cancel) }
        let first = webView.backForwardList.currentItem == nil && webView.url == nil
        decisionHandler(first || action.navigationType == .linkActivated || action.navigationType == .backForward || action.navigationType == .other ? .allow : .cancel)
    }

    func webView(_ webView: WKWebView, decidePolicyFor response: WKNavigationResponse, decisionHandler: @escaping (WKNavigationResponsePolicy) -> Void) {
        decisionHandler(response.canShowMIMEType ? .allow : .cancel)
    }

    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration, for action: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        // A link meant for a new window opens here, in the peek, when you tapped it.
        if action.navigationType == .linkActivated { webView.load(action.request) }
        return nil
    }

    func webView(_ webView: WKWebView, requestMediaCapturePermissionFor origin: WKSecurityOrigin, initiatedByFrame frame: WKFrameInfo, type: WKMediaCaptureType, decisionHandler: @escaping (WKPermissionDecision) -> Void) {
        decisionHandler(.deny)
    }

    func webView(_ webView: WKWebView, runJavaScriptAlertPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping () -> Void) { completionHandler() }
    func webView(_ webView: WKWebView, runJavaScriptConfirmPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping (Bool) -> Void) { completionHandler(false) }
    func webView(_ webView: WKWebView, runJavaScriptTextInputPanelWithPrompt prompt: String, defaultText: String?, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping (String?) -> Void) { completionHandler(nil) }
}
