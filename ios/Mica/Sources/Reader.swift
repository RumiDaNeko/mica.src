import UIKit
import CryptoKit

/// Reading mode, from desktop's Reader.js and BrowserReader.cs: the article is found on the page, kept as plain blocks,
/// and drawn by Mica as its own page, mica://read/<site>/<id> (SchemeHandler). It opens in the same tab, so Back
/// returns to the page. Articles saved to read later live in Application Support/Mica/ReadLater/<id>.json and read
/// without a connection; one just being read sits in the cache.
enum Reader {
    static let prefix = "mica://read/"
    /// Android's engine host for reader pages; only mapped back, never loaded.
    static let legacyHost = "read.mica.invalid"

    static func isReader(_ url: String) -> Bool { url.hasPrefix(prefix) }
    static func site(_ url: String) -> String { String(url.dropFirst(prefix.count).split(separator: "/").first ?? "") }
    static func id(_ url: String) -> String {
        let parts = url.dropFirst(prefix.count).split(separator: "/")
        guard parts.count >= 2 else { return "" }
        return String(parts[1].split(separator: "?").first ?? "")
    }
    static func address(site: String, id: String) -> String { prefix + site.replacingOccurrences(of: "/", with: "") + "/" + id }

    /// One id per article address, so saving the same page twice updates one copy.
    static func idFor(_ url: String) -> String {
        SHA256.hash(data: Data(url.utf8)).prefix(8).map { String(format: "%02x", $0) }.joined()
    }

    private static var savedDir: URL { Files.dir("ReadLater") }
    private static var cacheDir: URL { Files.dir("reader", cache: true) }

    static let script: String = {
        guard let u = Bundle.main.url(forResource: "Reader", withExtension: "js", subdirectory: "Shared/scripts") else { return "" }
        return (try? String(contentsOf: u, encoding: .utf8)) ?? ""
    }()

    /// The script's answer (a JSON string) as an article, or nil if the page isn't one.
    static func parse(_ raw: Any?) -> [String: Any]? {
        guard let s = raw as? String, let data = s.data(using: .utf8),
              let o = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              o["ok"] as? Bool == true, o["blocks"] is [Any] else { return nil }
        return o
    }

    /// Keeps an article to read now (cache) or for later; returns its reader address.
    @discardableResult static func keep(_ article: [String: Any], saved: Bool) -> String {
        let url = article["url"] as? String ?? ""
        let id = idFor(url)
        if let data = try? JSONSerialization.data(withJSONObject: article) {
            Files.write(data, to: (saved ? savedDir : cacheDir).appendingPathComponent(id + ".json"))
        }
        let site = (article["site"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? Navigation.label(url)
        return address(site: site, id: id)
    }

    static func isSaved(_ url: String) -> Bool { FileManager.default.fileExists(atPath: savedDir.appendingPathComponent(idFor(url) + ".json").path) }
    static func forget(_ url: String) { try? FileManager.default.removeItem(at: savedDir.appendingPathComponent(idFor(url) + ".json")) }

    /// The reader address of a saved copy of [url], or nil.
    static func savedAddress(_ url: String) -> String? {
        guard let a = load(idFor(url)) else { return nil }
        let site = (a["site"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? Navigation.label(url)
        return address(site: site, id: idFor(url))
    }

    static func load(_ id: String) -> [String: Any]? {
        guard id.count == 16, id.allSatisfy({ $0.isHexDigit }) else { return nil }
        for dir in [savedDir, cacheDir] {
            if let data = try? Data(contentsOf: dir.appendingPathComponent(id + ".json")),
               let o = try? JSONSerialization.jsonObject(with: data) as? [String: Any] { return o }
        }
        return nil
    }

    /// Articles read more than two days ago leave the cache.
    static func trimCache() {
        let old = Date().addingTimeInterval(-2 * 24 * 3600)
        let fm = FileManager.default
        for f in (try? fm.contentsOfDirectory(at: cacheDir, includingPropertiesForKeys: [.contentModificationDateKey])) ?? [] {
            if let d = try? f.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate, d < old { try? fm.removeItem(at: f) }
        }
    }

    static func page(_ url: String, dark: Bool) -> String {
        let id = Reader.id(url)
        guard let a = load(id) else {
            return "<!doctype html><meta name=viewport content='width=device-width,initial-scale=1'><title>\(L("reader").html)</title>" +
                "<body style='font:16px -apple-system,sans-serif;padding:32px;color:#888'>\(L("reader_gone").html)</body>"
        }
        let size = Prefs.shared.readerSize
        let serif = Prefs.shared.readerSerif
        let title = a["title"] as? String ?? ""
        let site = a["site"] as? String ?? ""
        let author = a["author"] as? String ?? ""
        let words = a["words"] as? Int ?? 0
        let lang = a["lang"] as? String ?? ""
        let saved = FileManager.default.fileExists(atPath: savedDir.appendingPathComponent(id + ".json").path)
        var s = "<!doctype html><html lang='\(lang.html)'\(dark ? " class='dark'" : "")><head><meta charset='utf-8'>"
        s += "<meta name='viewport' content='width=device-width,initial-scale=1'><title>\(title.html)</title><style>"
        s += ":root{--ink:#24212b;--muted:#6f6a78;--accent:#71608c;--canvas:#faf8f5;--panel:#efebf3;--line:#e3dee8;--size:\(size)px}"
        s += "html.dark{--ink:#ece8f2;--muted:#a7a0b3;--accent:#b6a4d6;--canvas:#1c1a20;--panel:#2a2730;--line:#38343f}"
        s += "html{background:var(--canvas);color:var(--ink);-webkit-text-size-adjust:100%}"
        s += "body{margin:0;padding:72px 22px 120px;font:var(--size)/1.62 \(serif ? "'New York',Georgia,serif" : "-apple-system,system-ui,sans-serif")}"
        s += "main{max-width:40em;margin:0 auto}.kicker{font:600 11px -apple-system,sans-serif;letter-spacing:.08em;color:var(--accent);text-transform:uppercase;margin:0 0 10px}"
        s += "h1{font:600 calc(var(--size)*1.6)/1.22 -apple-system,sans-serif;margin:0 0 12px}.by{font:13px -apple-system,sans-serif;color:var(--muted);margin:0 0 calc(var(--size)*1.6)}"
        s += "h2{font:600 calc(var(--size)*1.3)/1.3 -apple-system,sans-serif;margin:1.2em 0 .5em}h3{font:600 calc(var(--size)*1.12)/1.3 -apple-system,sans-serif;margin:1em 0 .4em}"
        s += "p{margin:0 0 .9em}ul{margin:0 0 .9em;padding-left:1.4em}li{margin:0 0 .35em}"
        s += "blockquote{margin:0 0 .9em;padding:2px 0 2px 1em;border-left:3px solid var(--accent);font-style:italic}"
        s += "pre{font:calc(var(--size)*.8)/1.45 ui-monospace,monospace;background:var(--panel);padding:10px 14px;border-radius:10px;overflow-x:auto;white-space:pre}"
        s += "figure{margin:.4em 0 1.1em}img{display:block;max-width:100%;height:auto;margin:0 auto;border-radius:8px}.cap{font:13px -apple-system,sans-serif;color:var(--muted);margin:0 0 1.2em}"
        s += ".bar{position:fixed;top:0;left:0;right:0;display:flex;gap:4px;justify-content:flex-end;padding:calc(env(safe-area-inset-top) + 10px) 12px 10px;background:var(--canvas);border-bottom:1px solid var(--line);z-index:2}"
        s += ".bar button{font:600 14px -apple-system,sans-serif;color:var(--accent);background:none;border:0;border-radius:14px;padding:8px 12px}.bar button:active{background:var(--panel)}"
        s += ".bar .grow{flex:1}a{color:inherit}mark{color:inherit}</style></head><body>"
        s += "<div class='bar'><button data-a='original'>\(L("reader_original").html)</button><span class='grow'></span>"
        s += "<button data-a='smaller' aria-label='\(L("smaller_text").html)'>A−</button>"
        s += "<button data-a='larger' aria-label='\(L("larger_text").html)'>A+</button>"
        s += "<button data-a='serif' style='font-family:Georgia,serif'>Aa</button>"
        s += "<button data-a='save'>\(L(saved ? "reader_saved" : "read_later").html)</button></div><main>"
        if !site.isEmpty { s += "<p class='kicker'>\(site.html)</p>" }
        s += "<h1>\(title.html)</h1>"
        var by: [String] = []
        if !author.trimmingCharacters(in: .whitespaces).isEmpty { by.append(author) }
        if words > 0 { by.append(L("min_read", max(1, Int((Double(words) / 230).rounded())))) }
        if !by.isEmpty { s += "<p class='by'>\(by.joined(separator: " · ").html)</p>" }
        var inList = false
        for case let b as [String: Any] in a["blocks"] as? [Any] ?? [] {
            let kind = b["t"] as? String ?? ""
            let text = (b["text"] as? String ?? "").html
            if kind != "li" && inList { s += "</ul>"; inList = false }
            switch kind {
            case "h2": s += "<h2>\(text)</h2>"
            case "h3": s += "<h3>\(text)</h3>"
            case "li": if !inList { s += "<ul>"; inList = true }; s += "<li>\(text)</li>"
            case "quote": s += "<blockquote>\(text)</blockquote>"
            case "pre": s += "<pre>\(text)</pre>"
            case "caption": s += "<p class='cap'>\(text)</p>"
            case "img":
                let src = b["src"] as? String ?? ""
                if src.hasPrefix("https://") || src.hasPrefix("http://") { s += "<figure><img loading='lazy' referrerpolicy='no-referrer' src=\"\(src.html)\" alt=\"\(text)\"></figure>" }
            default: s += "<p>\(text)</p>"
            }
        }
        if inList { s += "</ul>" }
        s += "</main><script>(()=>{let size=\(size),serif=\(serif);"
        s += "const post=m=>{try{webkit.messageHandlers.micaReader.postMessage(m)}catch(e){}};"
        s += "document.querySelector('.bar').addEventListener('click',e=>{const b=e.target.closest('button');if(!b)return;const a=b.dataset.a;"
        s += "if(a==='smaller'||a==='larger'){size=Math.max(14,Math.min(30,size+(a==='larger'?1:-1)));document.documentElement.style.setProperty('--size',size+'px');post('size:'+size)}"
        s += "else if(a==='serif'){post('serif:'+!serif)}else post(a)});})()</script></body></html>"
        return s
    }
}

/// Mica's own screen for a page that didn't load (desktop's ErrorPanel), loaded in place of the page with the failed
/// address as its base, so the address, Back and reload stay the page's own.
enum ErrorPage {
    static func html(url: String, code: Int, description: String, offline: Bool, insecure: String?, dark: Bool) -> String {
        let plain = offline ? nil : insecure
        let title = L(offline ? "error_offline_title" : plain != nil ? "no_https_title" : "error_title")
        let text: String
        if offline { text = L("error_offline") }
        else if let plain { text = L("no_https_text", URLComponents(string: plain)?.host ?? plain) }
        else { text = L("error_text", description) }
        let connection = [NSURLErrorCannotFindHost, NSURLErrorCannotConnectToHost, NSURLErrorTimedOut, NSURLErrorNetworkConnectionLost,
                          NSURLErrorNotConnectedToInternet, NSURLErrorDNSLookupFailed].contains(code)
        let games = plain == nil && (offline || connection)
        let canvas = dark ? "#17151B" : "#FAF9F6", ink = dark ? "#F7F4FA" : "#2A2530"
        let muted = dark ? "#A69AAA" : "#716B7A", hover = dark ? "#2A2631" : "#EFEBF3"
        var h = "<!doctype html><meta name=viewport content='width=device-width,initial-scale=1'><title>\(title.html)</title><style>"
        h += "html{color-scheme:\(dark ? "dark" : "light")}body{margin:0;min-height:100vh;display:grid;place-items:center;background:\(canvas);color:\(ink);font:16px/1.5 -apple-system,sans-serif}"
        h += "main{max-width:420px;padding:28px;animation:in .42s cubic-bezier(.2,.9,.25,1) both}h1{font:400 30px/1.2 'New York',Georgia,serif;margin:0 0 14px}p{color:\(muted);margin:0 0 22px;overflow-wrap:anywhere}"
        h += "button{font:500 15px -apple-system,sans-serif;border:0;border-radius:999px;padding:11px 18px;margin:0 8px 8px 0;background:\(hover);color:\(ink);transition:transform .15s}button:active{transform:scale(.96)}"
        h += "button.quiet{background:none;color:\(muted)}@keyframes in{from{opacity:0;transform:translateY(14px) scale(.98)}}</style>"
        h += "<main><h1>\(title.html)</h1><p>\(text.html)</p><button id=r>\(L("try_again").html)</button>"
        if plain != nil { h += "<button class=quiet id=p>\(L("continue_http").html)</button>" }
        if games { h += "<button class=quiet id=g>\(L("error_games").html)</button>" }
        h += "</main><script>(()=>{const go=\(url.js);"
        h += "document.getElementById('r').onclick=()=>location.replace(go);"
        h += "const g=document.getElementById('g');if(g)g.onclick=()=>location.href='mica://games';"
        if let plain { h += "document.getElementById('p').onclick=()=>location.href=\(("https://" + Https.continueHost + "/?u=" + Navigation.encode(plain)).js);" }
        // Back online: try again by itself.
        h += "addEventListener('online',()=>location.replace(go))})()</script>"
        return h
    }
}
