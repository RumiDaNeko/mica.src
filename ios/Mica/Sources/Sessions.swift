import Foundation

/// Named sets of tabs you can bring back later (desktop's BrowserSessions): one file per session in
/// Application Support/Mica/Sessions, as {name, saved, tabs:[{url, title, space}]}, the same as Android.
/// Saving under a name that exists replaces it.
enum Sessions {
    struct Page { let url: String; let title: String; let space: String }
    struct Saved { let name: String; let saved: Double; let tabs: [Page]; let file: URL }

    private static var dir: URL { Files.dir("Sessions") }

    private static func fileName(_ name: String) -> String {
        let safe = String(name.map { $0.isLetter || $0.isNumber || " -_.()".contains($0) ? $0 : "_" }).trimmingCharacters(in: .whitespaces)
        return (safe.isEmpty ? "Session" : safe) + ".json"
    }

    static func save(_ name: String, _ tabs: [Tab]) -> Bool {
        let o: [String: Any] = ["name": name, "saved": Int64(Date().timeIntervalSince1970 * 1000),
                                "tabs": tabs.map { ["url": $0.url, "title": $0.title, "space": $0.space] }]
        guard let data = try? JSONSerialization.data(withJSONObject: o) else { return false }
        do { try data.write(to: dir.appendingPathComponent(fileName(name)), options: .atomic); return true } catch { return false }
    }

    /// Newest first; a file that doesn't read is left out.
    static func list() -> [Saved] {
        let files = (try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil)) ?? []
        return files.filter { $0.pathExtension == "json" }.compactMap { f -> Saved? in
            guard let data = try? Data(contentsOf: f), let o = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let a = o["tabs"] as? [[String: Any]] else { return nil }
            let pages = a.compactMap { t -> Page? in
                guard let u = t["url"] as? String else { return nil }
                return Page(url: u, title: t["title"] as? String ?? "", space: t["space"] as? String ?? "")
            }
            return Saved(name: o["name"] as? String ?? f.deletingPathExtension().lastPathComponent, saved: (o["saved"] as? Double) ?? 0, tabs: pages, file: f)
        }.sorted { $0.saved > $1.saved }
    }

    static func delete(_ s: Saved) { try? FileManager.default.removeItem(at: s.file) }
}
