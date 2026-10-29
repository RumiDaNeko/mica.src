import UIKit
import UniformTypeIdentifiers

/// Passwords & sync (desktop's vault window): the Mica account, the master password, saved logins with their 2FA
/// codes, import from another browser. The screen goes blank in the app switcher.
final class PasswordsViewController: RowsViewController, UIDocumentPickerDelegate, UISearchResultsUpdating {
    private weak var main: MainViewController?
    private var account: Account { Account.shared }
    private var filter = ""
    private var ticker: Timer?
    private let search = UISearchController(searchResultsController: nil)
    private let master = Sheet.field(L("master_password"), secure: true)
    private var shield: UIView?
    private var shownLogins: [VaultEntry] = []
    private var loginSection: Int?

    init(main: MainViewController) { self.main = main; super.init() }
    required init?(coder: NSCoder) { fatalError() }

    override func viewDidLoad() {
        super.viewDidLoad()
        title = L("passwords_sync")
        master.textContentType = .password
        search.searchResultsUpdater = self
        search.obscuresBackgroundDuringPresentation = false
        search.searchBar.placeholder = L("search_logins")
        NotificationCenter.default.addObserver(self, selector: #selector(resignActive), name: UIApplication.willResignActiveNotification, object: nil)
        NotificationCenter.default.addObserver(self, selector: #selector(becomeActive), name: UIApplication.didBecomeActiveNotification, object: nil)
    }

    override func viewWillAppear(_ animated: Bool) {
        super.viewWillAppear(animated)
        account.listen(self) { [weak self] in self?.render() }
        render()
        ticker = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in self?.tickCodes() }
    }

    override func viewWillDisappear(_ animated: Bool) {
        super.viewWillDisappear(animated)
        account.unlisten(self)
        ticker?.invalidate()
        ticker = nil
    }

    // Logins stay out of the app switcher's snapshot.
    @objc private func resignActive() {
        guard shield == nil, let w = view.window else { return }
        let s = UIView(frame: w.bounds)
        s.backgroundColor = Mica.canvas
        w.addSubview(s)
        shield = s
    }
    @objc private func becomeActive() { shield?.removeFromSuperview(); shield = nil }

    func updateSearchResults(for searchController: UISearchController) {
        filter = (searchController.searchBar.text ?? "").trimmingCharacters(in: .whitespaces).lowercased()
        render()
    }

    override func render() {
        var out: [Section] = []
        // Account
        var rows: [Row] = []
        if let u = account.user {
            rows.append(Row(title: L("signed_in_as", u.username), detail: account.lastMessage ?? L(account.syncing ? "syncing" : "sync_explain")))
            rows.append(Row(title: L(account.syncing ? "syncing" : "sync_now"), style: .primary) { [weak self] _ in self?.sync(nil) })
            rows.append(Row(title: L("sign_out"), style: .destructive) { [weak self] _ in self?.account.signOut() })
        } else {
            rows.append(Row(title: L("account_explain"), style: .note))
            if account.signIn != nil { rows.append(Row(title: L("cancel_sign_in"), style: .button) { [weak self] _ in self?.account.cancelSignIn() }) }
            else { rows.append(Row(title: L("sign_in_discord"), style: .primary) { [weak self] _ in self?.signIn() }) }
        }
        out.append(Section(title: L("account"), rows: rows))

        // Vault
        rows = []
        var logins: [VaultEntry] = []
        let vault = account.vault
        if vault == nil || account.passphrasePending {
            rows.append(Row(title: L(account.passphrasePending ? "choose_master_pending" : account.hasVault ? "vault_locked" : "vault_new"), style: .note))
            rows.append(Row(title: "", custom: master))
            rows.append(Row(title: L(account.hasVault && !account.passphrasePending ? "unlock" : "create_vault"), style: .primary) { [weak self] _ in
                guard let self else { return }
                let pw = self.master.text ?? ""
                self.master.text = ""
                self.view.endEditing(true)
                self.account.unlock(pw) { e in if let e { self.notice(self.message(e)) } }
            })
            if account.user != nil && !account.hasVault { rows.append(Row(title: L("vault_from_cloud"), style: .note)) }
            if account.passphrasePending, let vault { logins = vault.entries }
            navigationItem.searchController = nil
        } else if let vault {
            rows.append(Row(title: L("add_login"), style: .primary) { [weak self] _ in self?.edit(nil) })
            rows.append(Row(title: L("import_csv"), style: .button) { [weak self] _ in self?.importCsv() })
            rows.append(Row(title: L("lock"), style: .button) { [weak self] _ in self?.account.lock() })
            logins = vault.entries
            if vault.entries.isEmpty { rows.append(Row(title: L("no_logins"), style: .note)) }
            navigationItem.searchController = vault.entries.count > 6 ? search : nil
        }
        out.append(Section(title: L("password_vault"), rows: rows))

        let shown = logins.sorted { ($0.origin, $0.username.lowercased()) < ($1.origin, $1.username.lowercased()) }
            .filter { filter.isEmpty || $0.origin.contains(filter) || $0.username.lowercased().contains(filter) }
        shownLogins = shown
        loginSection = shown.isEmpty ? nil : out.count
        if !shown.isEmpty {
            out.append(Section(rows: shown.map { e in
                Row(title: host(e.origin), detail: detail(e)) { [weak self] _ in self?.details(e) }
            }))
        }
        out.append(Section(footer: L("passkeys_note"), rows: [
            Row(title: L("mica_passwords"), detail: L("mica_passwords_detail"), toggle: Prefs.shared.micaPasswords) { on in Prefs.shared.micaPasswords = on },
        ]))
        sections = out
        super.render()
    }

    private func host(_ origin: String) -> String { origin.hasPrefix("https://") ? String(origin.dropFirst(8)) : origin }

    private func detail(_ e: VaultEntry) -> String {
        var s = e.username.isEmpty ? L("no_username") : e.username
        if let code = Totp.code(e.totp) {
            s += "\n" + L("totp_code_left", String(code.prefix(3)) + " " + String(code.dropFirst(3)), Totp.secondsLeft(e.totp))
        }
        return s
    }

    /// The 2FA codes count down in place.
    private func tickCodes() {
        guard let section = loginSection else { return }
        for cell in tableView.visibleCells {
            guard let path = tableView.indexPath(for: cell), path.section == section, path.row < shownLogins.count,
                  !(shownLogins[path.row].totp ?? "").isEmpty, var c = cell.contentConfiguration as? UIListContentConfiguration else { continue }
            c.secondaryText = detail(shownLogins[path.row])
            cell.contentConfiguration = c
        }
    }

    private func details(_ e: VaultEntry) {
        let a = UIAlertController(title: host(e.origin), message: e.username.isEmpty ? nil : e.username, preferredStyle: .actionSheet)
        if !e.username.isEmpty { a.addAction(UIAlertAction(title: L("copy_username"), style: .default) { [weak self] _ in self?.copy(e.username, secret: false) }) }
        if !e.password.isEmpty { a.addAction(UIAlertAction(title: L("copy_password"), style: .default) { [weak self] _ in self?.copy(e.password, secret: true) }) }
        if Totp.code(e.totp) != nil {
            a.addAction(UIAlertAction(title: L("copy_code"), style: .default) { [weak self] _ in if let c = Totp.code(e.totp) { self?.copy(c, secret: true) } })
        }
        a.addAction(UIAlertAction(title: L("open_site"), style: .default) { [weak self] _ in
            guard let main = self?.main else { return }
            self?.dismiss(animated: true) { main.openFromLibrary(e.origin) }
        })
        a.addAction(UIAlertAction(title: L("edit"), style: .default) { [weak self] _ in self?.edit(e) })
        a.addAction(UIAlertAction(title: L("delete"), style: .destructive) { [weak self] _ in
            guard let self else { return }
            self.confirm(L("delete_login_q"), self.host(e.origin) + "\n" + (e.username.isEmpty ? L("no_username") : e.username), action: L("delete")) {
                self.account.remove(e) { _ in self.render() }
            }
        })
        a.addAction(UIAlertAction(title: L("cancel"), style: .cancel))
        a.popoverPresentationController?.sourceView = view
        a.popoverPresentationController?.sourceRect = CGRect(x: view.bounds.midX, y: view.bounds.midY, width: 1, height: 1)
        present(a, animated: true)
    }

    /// Passwords and codes stay local to this device and are cleared after 30 seconds, as on desktop.
    private func copy(_ value: String, secret: Bool) {
        if secret {
            UIPasteboard.general.setItems([[UTType.utf8PlainText.identifier: value]],
                                          options: [.localOnly: true, .expirationDate: Date().addingTimeInterval(30)])
        } else { UIPasteboard.general.string = value }
        notice(L(secret ? "copied_secret" : "copied"))
    }

    private func edit(_ e: VaultEntry?) {
        let a = UIAlertController(title: L(e == nil ? "add_login" : "edit"), message: nil, preferredStyle: .alert)
        a.addTextField { f in f.placeholder = L("website"); f.text = e?.origin ?? "https://"; f.keyboardType = .URL; f.autocapitalizationType = .none; f.autocorrectionType = .no }
        a.addTextField { f in f.placeholder = L("username"); f.text = e?.username; f.textContentType = .username; f.autocapitalizationType = .none; f.autocorrectionType = .no }
        a.addTextField { f in f.placeholder = L("password"); f.text = e?.password; f.isSecureTextEntry = true; f.textContentType = .password }
        a.addTextField { f in f.placeholder = L("totp_key"); f.text = e?.totp; f.autocapitalizationType = .none; f.autocorrectionType = .no }
        a.addAction(UIAlertAction(title: L("cancel"), style: .cancel))
        a.addAction(UIAlertAction(title: L("save"), style: .default) { [weak self, weak a] _ in
            guard let self, let f = a?.textFields else { return }
            do {
                let origin = try VaultCrypto.origin(f[0].text ?? "")
                let raw = (f[3].text ?? "").trimmingCharacters(in: .whitespaces)
                let key = raw.isEmpty ? nil : Totp.normalize(try Totp.parse(raw))
                let was = e?.key
                let entry = e ?? VaultEntry(id: VaultEntry.newId(), origin: origin, username: "", password: "")
                entry.origin = origin; entry.username = f[1].text ?? ""; entry.password = f[2].text ?? ""; entry.totp = key
                self.account.put(entry, was: was) { x in self.notice(x.map { self.message($0) } ?? L("login_saved")) }
            } catch {
                self.notice(self.message(error))
            }
        })
        present(a, animated: true)
    }

    private func importCsv() {
        let picker = UIDocumentPickerViewController(forOpeningContentTypes: [.commaSeparatedText, .plainText, .data], asCopy: true)
        picker.delegate = self
        present(picker, animated: true)
    }

    func documentPicker(_ controller: UIDocumentPickerViewController, didPickDocumentsAt urls: [URL]) {
        guard let u = urls.first else { return }
        let text = (try? Data(contentsOf: u)).flatMap { String(data: $0, encoding: .utf8) }
        try? FileManager.default.removeItem(at: u)
        let logins = text.map { PasswordCsv.parse($0) } ?? []
        if logins.isEmpty { notice(L("import_none")); return }
        account.importLogins(logins) { [weak self] n in self?.notice(L("imported_logins", n)) }
    }

    private func signIn() {
        account.beginSignIn { [weak self] s, e in
            guard let self else { return }
            guard s != nil else { self.notice(self.message(e)); return }
            // Discord opens in a Mica tab; the code waits in a card there to be typed (or pasted) on Discord's page.
            let main = self.main
            self.dismiss(animated: true) { main?.showSignInCode() }
        }
    }

    private func sync(_ password: String?) {
        account.sync(password: password) { [weak self] r in
            guard let self else { return }
            switch r {
            case .done(let m): self.notice(m)
            case .needPassword: self.askCloudPassword()
            case .failed(let e): self.notice(self.message(e))
            }
        }
    }

    private func askCloudPassword() {
        let a = UIAlertController(title: L("unlock_cloud"), message: L("unlock_cloud_detail"), preferredStyle: .alert)
        a.addTextField { f in f.placeholder = L("master_password"); f.isSecureTextEntry = true; f.textContentType = .password }
        a.addAction(UIAlertAction(title: L("cancel"), style: .cancel))
        a.addAction(UIAlertAction(title: L("unlock"), style: .default) { [weak self, weak a] _ in
            let pw = a?.textFields?.first?.text ?? ""
            if !pw.isEmpty { self?.sync(pw) }
        })
        present(a, animated: true)
    }

    private func message(_ e: Error?) -> String {
        switch e {
        case let v as VaultError: return v.wrongPassword ? L("wrong_master") : v.message
        case let a as ApiError: return a.status == 401 ? L("signed_out_again") : L("sync_unreachable")
        case let p as Problem: return p.message
        case is URLError, nil: return L("sync_unreachable")
        default: return e!.localizedDescription
        }
    }
}
