import UIKit
import QuickLook

/// The library (android Library): bookmarks, history (by day), read later and recently closed tabs, with one
/// search across the section shown. Tap opens; swipe takes an entry out.
final class LibraryViewController: UITableViewController, UISearchResultsUpdating {
    enum Section: Int, CaseIterable {
        case bookmarks, history, readLater, closed
        var label: String { L(["bookmarks", "history", "read_later", "recently_closed"][rawValue]) }
        var empty: String { L(["no_bookmarks", "no_history", "no_read_later", "no_closed"][rawValue]) }
    }

    private weak var main: MainViewController?
    private var section = Section.bookmarks
    private let picker = UISegmentedControl(items: Section.allCases.map { $0.label })
    private let search = UISearchController(searchResultsController: nil)
    private var groups: [(day: String?, rows: [HistoryEntry])] = []
    private let empty = makeLabel("", 14, Mica.muted, lines: 0)
    private var browser: Browser { Browser.shared }

    init(main: MainViewController, section: Section = .bookmarks) {
        self.main = main
        self.section = section
        super.init(style: .insetGrouped)
    }

    required init?(coder: NSCoder) { fatalError() }

    override func viewDidLoad() {
        super.viewDidLoad()
        title = L("library")
        view.backgroundColor = Mica.canvas
        tableView.backgroundColor = Mica.canvas
        navigationItem.leftBarButtonItem = UIBarButtonItem(systemItem: .close, primaryAction: UIAction { [weak self] _ in self?.dismiss(animated: true) })
        navigationItem.rightBarButtonItem = UIBarButtonItem(title: L("downloads"), primaryAction: UIAction { [weak self] _ in
            self?.navigationController?.pushViewController(DownloadsViewController(), animated: true)
        })
        search.searchResultsUpdater = self
        search.obscuresBackgroundDuringPresentation = false
        search.searchBar.placeholder = L("search_library")
        navigationItem.searchController = search
        navigationItem.hidesSearchBarWhenScrolling = false
        picker.selectedSegmentIndex = section.rawValue
        picker.addAction(UIAction { [weak self] _ in
            guard let self, let s = Section(rawValue: self.picker.selectedSegmentIndex) else { return }
            self.section = s
            self.render()
        }, for: .valueChanged)
        let head = UIView(frame: CGRect(x: 0, y: 0, width: 300, height: 48))
        head.addSubview(picker)
        picker.pin(to: head, insets: UIEdgeInsets(top: 4, left: 16, bottom: 8, right: 16))
        tableView.tableHeaderView = head
        empty.textAlignment = .center
        tableView.backgroundView = empty
        tableView.register(UITableViewCell.self, forCellReuseIdentifier: "row")
        render()
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        if let h = tableView.tableHeaderView, h.frame.width != tableView.bounds.width {
            h.frame.size.width = tableView.bounds.width
            tableView.tableHeaderView = h
        }
    }

    func updateSearchResults(for searchController: UISearchController) { render() }

    private func entries(_ s: Section) -> [HistoryEntry] {
        switch s {
        case .bookmarks: return browser.bookmarks
        case .history: return browser.history
        case .readLater: return browser.readLater
        case .closed: return browser.recentlyClosed
        }
    }

    private func render() {
        let words = (search.searchBar.text ?? "").lowercased().split(separator: " ").map(String.init)
        let found = entries(section).filter { e in let t = (e.title + " " + e.url).lowercased(); return words.allSatisfy { t.contains($0) } }
        // History reads by day; the other lists are short and already newest first.
        groups = []
        if section == .history {
            for e in found {
                let d = dayLabel(e.at)
                if groups.last?.day == d { groups[groups.count - 1].rows.append(e) } else { groups.append((d, [e])) }
            }
        } else if !found.isEmpty { groups = [(nil, found)] }
        empty.text = words.isEmpty ? section.empty : L("nothing_found")
        empty.isHidden = !found.isEmpty
        if section == .history && !browser.history.isEmpty {
            toolbarItems = [UIBarButtonItem(systemItem: .flexibleSpace), UIBarButtonItem(title: L("clear_history"), primaryAction: UIAction { [weak self] _ in self?.confirmClear() })]
            navigationController?.setToolbarHidden(false, animated: true)
        } else { navigationController?.setToolbarHidden(true, animated: true) }
        tableView.reloadData()
    }

    private func dayLabel(_ at: TimeInterval) -> String {
        let d = Date(timeIntervalSince1970: at)
        if Calendar.current.isDateInToday(d) { return L("today") }
        if Calendar.current.isDateInYesterday(d) { return L("yesterday") }
        let f = DateFormatter()
        f.setLocalizedDateFormatFromTemplate("EEE d MMM")
        return f.string(from: d)
    }

    private func confirmClear() {
        let a = UIAlertController(title: L("clear_history_q"), message: L("clear_history_detail"), preferredStyle: .alert)
        a.addAction(UIAlertAction(title: L("cancel"), style: .cancel))
        a.addAction(UIAlertAction(title: L("clear_history"), style: .destructive) { [weak self] _ in
            self?.browser.clearHistory(); self?.render(); self?.main?.announce(L("history_cleared"))
        })
        present(a, animated: true)
    }

    private func remove(_ e: HistoryEntry) {
        switch section {
        case .bookmarks: browser.forgetBookmark(e)
        case .history: browser.forgetHistory(e)
        case .readLater: browser.forgetReadLater(e); Reader.forget(e.url)
        case .closed: browser.recentlyClosed.removeAll { $0 === e }; browser.saveLater()
        }
    }

    private func open(_ e: HistoryEntry) {
        let s = section
        if s == .closed { browser.recentlyClosed.removeAll { $0 === e }; browser.saveLater() }
        dismiss(animated: true) { [weak main] in
            if s == .readLater { main?.openReadLater(e.url) } else { main?.openFromLibrary(e.url) }
        }
    }

    // MARK: Table

    override func numberOfSections(in tableView: UITableView) -> Int { groups.count }
    override func tableView(_ tableView: UITableView, numberOfRowsInSection s: Int) -> Int { groups[s].rows.count }
    override func tableView(_ tableView: UITableView, titleForHeaderInSection s: Int) -> String? { groups[s].day }

    override func tableView(_ tableView: UITableView, cellForRowAt indexPath: IndexPath) -> UITableViewCell {
        let e = groups[indexPath.section].rows[indexPath.row]
        let cell = tableView.dequeueReusableCell(withIdentifier: "row", for: indexPath)
        var c = UIListContentConfiguration.subtitleCell()
        c.text = e.title.isEmpty ? Navigation.label(e.url) : e.title
        c.textProperties.numberOfLines = 1
        c.textProperties.color = Mica.ink
        let date = Date(timeIntervalSince1970: e.at)
        let time: String
        if section == .history { time = DateFormatter.localizedString(from: date, dateStyle: .none, timeStyle: .short) }
        else if Date().timeIntervalSince(date) < 60 { time = L("just_now") }
        else { time = RelativeDateTimeFormatter().localizedString(for: date, relativeTo: Date()) }
        c.secondaryText = Navigation.label(e.url) + "  ·  " + time
        c.secondaryTextProperties.color = Mica.muted
        c.secondaryTextProperties.numberOfLines = 1
        c.image = Favicons.icon(for: Domains.hostOf(e.url)) ?? UIImage(systemName: "globe")
        c.imageProperties.maximumSize = CGSize(width: 22, height: 22)
        c.imageProperties.tintColor = Mica.faint
        cell.contentConfiguration = c
        cell.backgroundColor = Mica.card
        return cell
    }

    override func tableView(_ tableView: UITableView, didSelectRowAt indexPath: IndexPath) {
        open(groups[indexPath.section].rows[indexPath.row])
    }

    override func tableView(_ tableView: UITableView, trailingSwipeActionsConfigurationForRowAt indexPath: IndexPath) -> UISwipeActionsConfiguration? {
        let e = groups[indexPath.section].rows[indexPath.row]
        return UISwipeActionsConfiguration(actions: [UIContextualAction(style: .destructive, title: L("remove")) { [weak self] _, _, done in
            self?.remove(e); self?.render(); done(true)
        }])
    }

    override func tableView(_ tableView: UITableView, contextMenuConfigurationForRowAt indexPath: IndexPath, point: CGPoint) -> UIContextMenuConfiguration? {
        let e = groups[indexPath.section].rows[indexPath.row]
        return UIContextMenuConfiguration(identifier: nil, previewProvider: nil) { [weak self] _ in
            UIMenu(children: [
                UIAction(title: L("open_in_new_tab"), image: UIImage(systemName: "plus.square.on.square")) { _ in
                    self?.dismiss(animated: true) { Browser.shared.open(e.url) }
                },
                UIAction(title: L("copy_link"), image: UIImage(systemName: "doc.on.doc")) { _ in UIPasteboard.general.string = e.url },
                UIAction(title: L("remove"), image: UIImage(systemName: "trash"), attributes: .destructive) { _ in self?.remove(e); self?.render() },
            ])
        }
    }
}

/// What Mica downloaded (Documents/Downloads, also in the Files app under Mica): tap to preview, swipe to delete.
final class DownloadsViewController: UITableViewController, QLPreviewControllerDataSource {
    private var files: [URL] = []
    private var previewing: URL?
    private let empty = makeLabel(L("no_downloads"), 14, Mica.muted, lines: 0)

    init() { super.init(style: .insetGrouped) }
    required init?(coder: NSCoder) { fatalError() }

    override func viewDidLoad() {
        super.viewDidLoad()
        title = L("downloads")
        tableView.backgroundColor = Mica.canvas
        empty.textAlignment = .center
        tableView.backgroundView = empty
        tableView.register(UITableViewCell.self, forCellReuseIdentifier: "file")
        load()
    }

    override func viewWillAppear(_ animated: Bool) {
        super.viewWillAppear(animated)
        navigationController?.setToolbarHidden(true, animated: false)
    }

    private func load() {
        let keys: [URLResourceKey] = [.contentModificationDateKey, .fileSizeKey]
        files = ((try? FileManager.default.contentsOfDirectory(at: Files.downloads, includingPropertiesForKeys: keys, options: .skipsHiddenFiles)) ?? [])
            .sorted { a, b in
                let da = (try? a.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
                let db = (try? b.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
                return da > db
            }
        empty.isHidden = !files.isEmpty
        tableView.reloadData()
    }

    override func tableView(_ tableView: UITableView, numberOfRowsInSection s: Int) -> Int { files.count }

    override func tableView(_ tableView: UITableView, cellForRowAt indexPath: IndexPath) -> UITableViewCell {
        let f = files[indexPath.row]
        let cell = tableView.dequeueReusableCell(withIdentifier: "file", for: indexPath)
        var c = UIListContentConfiguration.subtitleCell()
        c.text = f.lastPathComponent
        c.textProperties.color = Mica.ink
        let values = try? f.resourceValues(forKeys: [.fileSizeKey, .contentModificationDateKey])
        let size = ByteCountFormatter.string(fromByteCount: Int64(values?.fileSize ?? 0), countStyle: .file)
        let date = values?.contentModificationDate.map { DateFormatter.localizedString(from: $0, dateStyle: .medium, timeStyle: .short) } ?? ""
        c.secondaryText = size + "  ·  " + date
        c.secondaryTextProperties.color = Mica.muted
        c.image = UIImage(systemName: "doc")
        c.imageProperties.tintColor = Mica.accent
        cell.contentConfiguration = c
        cell.backgroundColor = Mica.card
        return cell
    }

    override func tableView(_ tableView: UITableView, didSelectRowAt indexPath: IndexPath) {
        tableView.deselectRow(at: indexPath, animated: true)
        previewing = files[indexPath.row]
        let q = QLPreviewController()
        q.dataSource = self
        present(q, animated: true)
    }

    override func tableView(_ tableView: UITableView, trailingSwipeActionsConfigurationForRowAt indexPath: IndexPath) -> UISwipeActionsConfiguration? {
        let f = files[indexPath.row]
        return UISwipeActionsConfiguration(actions: [
            UIContextualAction(style: .destructive, title: L("delete")) { [weak self] _, _, done in
                try? FileManager.default.removeItem(at: f); self?.load(); done(true)
            },
            UIContextualAction(style: .normal, title: L("share")) { [weak self] _, view, done in
                let vc = UIActivityViewController(activityItems: [f], applicationActivities: nil)
                vc.popoverPresentationController?.sourceView = view
                self?.present(vc, animated: true)
                done(true)
            },
        ])
    }

    func numberOfPreviewItems(in controller: QLPreviewController) -> Int { previewing == nil ? 0 : 1 }
    func previewController(_ controller: QLPreviewController, previewItemAt index: Int) -> QLPreviewItem { (previewing ?? Files.downloads) as NSURL }
}
