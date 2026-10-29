import UIKit
import WebKit

/// A screen of grouped rows (settings, passwords): each row a title with a value, a switch, or a button.
class RowsViewController: UITableViewController {
    struct Row {
        var title: String
        var detail: String? = nil
        var toggle: Bool? = nil
        var style: Style = .plain
        var custom: UIView? = nil
        var run: ((Bool) -> Void)? = nil
        enum Style { case plain, button, primary, destructive, note }
    }
    struct Section { var title: String? = nil; var footer: String? = nil; var rows: [Row] }

    var sections: [Section] = []

    init() { super.init(style: .insetGrouped) }
    required init?(coder: NSCoder) { fatalError() }

    override func viewDidLoad() {
        super.viewDidLoad()
        tableView.backgroundColor = Mica.canvas
        tableView.keyboardDismissMode = .interactive
        if navigationController?.viewControllers.first === self {
            navigationItem.leftBarButtonItem = UIBarButtonItem(systemItem: .close, primaryAction: UIAction { [weak self] _ in self?.dismiss(animated: true) })
        }
    }

    /// Builds the rows again (subclasses fill [sections]).
    func render() { tableView.reloadData() }

    override func numberOfSections(in tableView: UITableView) -> Int { sections.count }
    override func tableView(_ tableView: UITableView, numberOfRowsInSection s: Int) -> Int { sections[s].rows.count }
    override func tableView(_ tableView: UITableView, titleForHeaderInSection s: Int) -> String? { sections[s].title }
    override func tableView(_ tableView: UITableView, titleForFooterInSection s: Int) -> String? { sections[s].footer }

    override func tableView(_ tableView: UITableView, cellForRowAt indexPath: IndexPath) -> UITableViewCell {
        let row = sections[indexPath.section].rows[indexPath.row]
        let cell = UITableViewCell(style: .default, reuseIdentifier: nil)
        cell.backgroundColor = row.style == .note ? .clear : Mica.card
        if let custom = row.custom {
            cell.contentView.addSubview(custom)
            custom.pin(to: cell.contentView, insets: UIEdgeInsets(top: 6, left: 12, bottom: 6, right: 12))
            cell.selectionStyle = .none
            return cell
        }
        var c = row.detail == nil ? UIListContentConfiguration.cell() : UIListContentConfiguration.subtitleCell()
        c.text = row.title
        c.secondaryText = row.detail
        c.secondaryTextProperties.color = Mica.muted
        c.secondaryTextProperties.numberOfLines = 4
        c.textProperties.color = Mica.ink
        switch row.style {
        case .button: c.textProperties.color = Mica.accent; c.textProperties.font = Mica.font(16, .semibold)
        case .primary: c.textProperties.color = Mica.accent; c.textProperties.font = Mica.font(16, .bold)
        case .destructive: c.textProperties.color = .systemRed
        case .note: c.textProperties.color = Mica.muted; c.textProperties.font = Mica.font(13); c.directionalLayoutMargins.leading = 4
        case .plain: break
        }
        cell.contentConfiguration = c
        if let on = row.toggle {
            let s = UISwitch()
            s.isOn = on
            s.onTintColor = Mica.accent
            s.addAction(UIAction { [weak s] _ in row.run?(s?.isOn ?? false) }, for: .valueChanged)
            cell.accessoryView = s
            cell.selectionStyle = .none
        } else if row.run == nil {
            cell.selectionStyle = .none
        } else if row.style == .plain {
            cell.accessoryType = .disclosureIndicator
        }
        return cell
    }

    override func tableView(_ tableView: UITableView, didSelectRowAt indexPath: IndexPath) {
        tableView.deselectRow(at: indexPath, animated: true)
        let row = sections[indexPath.section].rows[indexPath.row]
        if let on = row.toggle {
            if let s = tableView.cellForRow(at: indexPath)?.accessoryView as? UISwitch { s.setOn(!on, animated: true) }
            row.run?(!on)
            return
        }
        row.run?(true)
    }

    /// One choice from a few; the current one is ticked.
    func pick(_ title: String, _ labels: [String], current: Int, chosen: @escaping (Int) -> Void) {
        let a = UIAlertController(title: title, message: nil, preferredStyle: .actionSheet)
        for (i, l) in labels.enumerated() {
            let action = UIAlertAction(title: l, style: .default) { _ in if i != current { chosen(i) } }
            action.setValue(i == current, forKey: "checked")
            a.addAction(action)
        }
        a.addAction(UIAlertAction(title: L("cancel"), style: .cancel))
        a.popoverPresentationController?.sourceView = view
        a.popoverPresentationController?.sourceRect = CGRect(x: view.bounds.midX, y: view.bounds.midY, width: 1, height: 1)
        present(a, animated: true)
    }

    func notice(_ text: String) {
        let a = UIAlertController(title: nil, message: text, preferredStyle: .alert)
        present(a, animated: true)
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.6) { [weak a] in a?.dismiss(animated: true) }
    }

    func confirm(_ title: String, _ detail: String?, action: String, destructive: Bool = true, run: @escaping () -> Void) {
        let a = UIAlertController(title: title, message: detail, preferredStyle: .alert)
        a.addAction(UIAlertAction(title: L("cancel"), style: .cancel))
        a.addAction(UIAlertAction(title: action, style: destructive ? .destructive : .default) { _ in run() })
        present(a, animated: true)
    }
}

/// A short settings page (android SettingsActivity): theme, language, ad blocking, privacy, desktop site.
final class SettingsViewController: RowsViewController {
    private weak var main: MainViewController?
    private var prefs: Prefs { Prefs.shared }

    init(main: MainViewController) { self.main = main; super.init() }
    required init?(coder: NSCoder) { fatalError() }

    override func viewDidLoad() {
        super.viewDidLoad()
        title = L("settings")
        render()
    }

    override func viewWillAppear(_ animated: Bool) { super.viewWillAppear(animated); render() }

    override func render() {
        let account = Account.shared
        let themes = [L("system"), L("light_theme"), L("dark_theme")]
        let lang = Bundle.main.preferredLocalizations.first ?? "en"
        let blocker = Blocker.shared
        let allowed = prefs.allowedSites.sorted()
        let decisions = prefs.sitePermissions.sorted { $0.key < $1.key }
        sections = [
            Section(rows: [
                Row(title: L("passwords_sync"), detail: account.user.map { L("signed_in_as", $0.username) } ?? L("not_signed_in")) { [weak self] _ in
                    guard let self, let main = self.main else { return }
                    self.navigationController?.pushViewController(PasswordsViewController(main: main), animated: true)
                },
                Row(title: L("theme"), detail: themes[prefs.theme.rawValue]) { [weak self] _ in
                    guard let self else { return }
                    self.pick(L("theme"), themes, current: self.prefs.theme.rawValue) { i in
                        self.prefs.theme = Prefs.Theme(rawValue: i) ?? .system
                        Account.shared.noteSettingsChange()
                        self.view.window?.overrideUserInterfaceStyle = self.prefs.theme.style
                        self.render()
                    }
                },
                // iOS keeps each app's language in the Settings app.
                Row(title: L("language"), detail: lang.hasPrefix("vi") ? L("vietnamese") : L("english")) { _ in
                    if let u = URL(string: UIApplication.openSettingsURLString) { UIApplication.shared.open(u) }
                },
            ]),
            Section(title: L("ad_blocking"), footer: blocker.list == nil ? L("rules_loading") : L("rules_loaded", blocker.meta.rules, blocker.meta.lists), rows: [
                Row(title: L("block_ads"), toggle: prefs.blockerOn) { [weak self] on in
                    self?.prefs.blockerOn = on
                    Browser.shared.applyBlockerEverywhere()
                },
                Row(title: L("allowed_sites"), detail: allowed.isEmpty ? L("no_allowed_sites") : allowed.joined(separator: ", ")) { [weak self] _ in
                    if !allowed.isEmpty { self?.navigationController?.pushViewController(AllowedSitesViewController(), animated: true) }
                },
            ]),
            Section(title: L("privacy"), rows: [
                Row(title: L("clear_data"), detail: L("clear_data_value")) { [weak self] _ in self?.navigationController?.pushViewController(ClearDataViewController(main: self?.main), animated: true) },
                Row(title: L("site_permissions"), detail: decisions.isEmpty ? L("no_site_permissions") : L("site_permission_count", decisions.count)) { [weak self] _ in
                    if !decisions.isEmpty { self?.navigationController?.pushViewController(SitePermissionsViewController(), animated: true) }
                },
                Row(title: L("cookie_jars"), detail: L("cookie_jars_detail")) { [weak self] _ in
                    self?.dismiss(animated: true) { self?.main?.manageJars() }
                },
            ]),
            Section(title: L("desktop_site"), footer: "Mica " + (Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? ""), rows: [
                Row(title: L("desktop_by_default"), toggle: prefs.desktopDefault) { [weak self] on in self?.prefs.desktopDefault = on },
            ]),
        ]
        super.render()
    }
}

/// Sites where ads are allowed; tap or swipe one to block its ads again.
final class AllowedSitesViewController: RowsViewController {
    override func viewDidLoad() { super.viewDidLoad(); title = L("allowed_sites"); render() }

    override func render() {
        let sites = Prefs.shared.allowedSites.sorted()
        sections = [Section(footer: L("allowed_sites_tap"), rows: sites.map { site in
            Row(title: site, style: .plain) { [weak self] _ in
                var all = Prefs.shared.allowedSites
                all.remove(site)
                Prefs.shared.allowedSites = all
                Browser.shared.applyBlockerEverywhere()
                self?.render()
            }
        })]
        super.render()
    }
}

/// Camera and microphone answers per site; tap one to forget it, and the site asks again next time.
final class SitePermissionsViewController: RowsViewController {
    override func viewDidLoad() { super.viewDidLoad(); title = L("site_permissions"); render() }

    override func render() {
        let all = Prefs.shared.sitePermissions.sorted { $0.key < $1.key }
        sections = [Section(footer: L("site_permissions_tap"), rows: all.map { key, allowed in
            let parts = key.split(separator: "\t").map(String.init)
            let what = L("perm_" + (parts.last ?? ""))
            return Row(title: Domains.hostOf(parts.first ?? key), detail: what.prefix(1).uppercased() + String(what.dropFirst()) + ": " + L(allowed ? "allowed" : "blocked")) { [weak self] _ in
                var p = Prefs.shared.sitePermissions
                p.removeValue(forKey: key)
                Prefs.shared.sitePermissions = p
                self?.render()
            }
        })]
        super.render()
    }
}

/// What to clear and from when, as on desktop: history, cookies and cache ticked; the last hour first.
final class ClearDataViewController: RowsViewController {
    private weak var main: MainViewController?
    private var range = 0
    private var history = true, cookies = true, cache = true

    init(main: MainViewController?) { self.main = main; super.init() }
    required init?(coder: NSCoder) { fatalError() }

    override func viewDidLoad() { super.viewDidLoad(); title = L("clear_data"); render() }

    override func render() {
        let ranges = ["last_hour", "last_day", "last_week", "all_time"].map { L($0) }
        let picker = UISegmentedControl(items: ranges)
        picker.selectedSegmentIndex = range
        picker.addAction(UIAction { [weak self, weak picker] _ in self?.range = picker?.selectedSegmentIndex ?? 0 }, for: .valueChanged)
        sections = [
            Section(footer: L("clear_data_detail"), rows: [Row(title: "", custom: picker)]),
            Section(rows: [
                Row(title: L("data_history"), detail: L("data_history_detail"), toggle: history) { [weak self] on in self?.history = on },
                Row(title: L("data_cookies"), detail: L("data_cookies_detail"), toggle: cookies) { [weak self] on in self?.cookies = on },
                Row(title: L("data_cache"), detail: L("data_cache_detail"), toggle: cache) { [weak self] on in self?.cache = on },
            ]),
            Section(rows: [Row(title: L("clear_data_button"), style: .destructive) { [weak self] _ in self?.clear() }]),
        ]
        super.render()
    }

    private func clear() {
        guard history || cookies || cache else { return }
        let hours: [Double?] = [1, 24, 24 * 7, nil]
        let since = hours[range].map { Date().addingTimeInterval(-$0 * 3600) }
        Browser.shared.clearData(since: since, history: history, cookies: cookies, cache: cache)
        notice(L("data_cleared"))
    }
}
