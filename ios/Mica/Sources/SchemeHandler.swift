import WebKit

/// mica:// pages: the built-in games (desktop's src\*.html) and reading mode. Nothing here touches the network.
/// A game page arrives already dark when Mica is dark; a theme change flips open pages in place (themeScript).
final class SchemeHandler: NSObject, WKURLSchemeHandler {
    static let shared = SchemeHandler()
    /// Set by the main view controller from its trait collection.
    static var dark = false

    static let themeScript = "document.documentElement.classList.toggle('dark',%@);dispatchEvent(new Event('micatheme'))"

    private static let gameCsp = "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:"
    private static let readerCsp = "default-src 'none'; img-src https: http: data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'"

    func webView(_ webView: WKWebView, start task: WKURLSchemeTask) {
        guard let url = task.request.url else { return }
        let address = url.absoluteString
        var html: String?
        var csp = Self.gameCsp
        if task.request.httpMethod == nil || task.request.httpMethod == "GET" {
            if Reader.isReader(address) {
                html = Reader.page(address, dark: Self.dark)
                csp = Self.readerCsp
            } else if let page = Self.gamePage(address) {
                html = Self.read(page)
                if Self.dark { html = html?.replacingOccurrences(of: "<html lang=\"en\">", with: "<html lang=\"en\" class=\"dark\">") }
            }
        }
        guard let body = html else {
            let r = HTTPURLResponse(url: url, statusCode: 404, httpVersion: "HTTP/1.1", headerFields: ["Content-Type": "text/plain"])!
            task.didReceive(r)
            task.didReceive(Data())
            task.didFinish()
            return
        }
        let headers = ["Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store", "Content-Security-Policy": csp]
        task.didReceive(HTTPURLResponse(url: url, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: headers)!)
        task.didReceive(Data(body.utf8))
        task.didFinish()
    }

    func webView(_ webView: WKWebView, stop task: WKURLSchemeTask) {}

    private static func gamePage(_ address: String) -> String? {
        var a = address
        if let q = a.firstIndex(where: { $0 == "?" || $0 == "#" }) { a = String(a[..<q]) }
        while a.hasSuffix("/") { a.removeLast() }
        if a == Navigation.tetris { return "Tetris" }
        return Navigation.game(a)?.page
    }

    private static var cache: [String: String] = [:]
    private static func read(_ page: String) -> String? {
        if let c = cache[page] { return c }
        guard let u = Bundle.main.url(forResource: page, withExtension: "html", subdirectory: "Shared/games"),
              var s = try? String(contentsOf: u, encoding: .utf8) else { return nil }
        // The shared pages link to each other by Android's https://*.mica.invalid addresses.
        s = s.replacingOccurrences(of: "https://\(Navigation.tetrisHost)/", with: Navigation.tetris)
        for g in Navigation.games where !g.path.isEmpty {
            s = s.replacingOccurrences(of: "https://\(Navigation.gamesHost)/\(g.path)\"", with: "\(g.address)\"")
        }
        s = s.replacingOccurrences(of: "https://\(Navigation.gamesHost)/\"", with: "mica://games\"")
        s = s.replacingOccurrences(of: "https://\(Navigation.gamesHost)\"", with: "mica://games\"")
        cache[page] = s
        return s
    }
}
