import UIKit

/// Saving and filling passwords and 2FA codes on pages, as desktop and Android do. The page script only reports
/// what it sees; filling is chosen in Mica's own card, which a page can't press, so a page can't ask for a password
/// and read it back. Pages only ever get a 2FA code, never the key.
final class Autofill {
    static let shared = Autofill()

    private var account: Account { Account.shared }
    private let d = UserDefaults(suiteName: "account") ?? .standard
    private var recentUser: [String: String] = [:]
    private var offeredKeys = Set<String>()
    private var toldPasskeys = false
    private weak var main: MainViewController?

    func received(_ tab: Tab, origin: String, json: String, main: MainViewController) {
        self.main = main
        guard let site = VaultCrypto.originOrNil(origin),
              let m = (try? JSONSerialization.jsonObject(with: Data(json.utf8))) as? [String: Any] else { return }
        let kind = m["mica"] as? String
        if kind == "passkeyFailed" { return passkeyFailed(tab, site) }
        guard Prefs.shared.micaPasswords else { return }
        switch kind {
        case "loginForm": offerFill(tab, site)
        case "credential":
            let user = String((m["username"] as? String ?? "").prefix(4096))
            guard let pass = m["password"] as? String, !pass.isEmpty, pass.count <= 65536 else { return }
            if !user.isEmpty { recentUser[site] = user }
            offerSave(site, user, pass)
        case "otpField": fillCode(tab, site)
        case "totpKey":
            let uri = m["uri"] as? String ?? "", secret = m["secret"] as? String ?? ""
            if uri.count <= 1024 && secret.count <= 200 { offerTotp(tab, site, uri.isEmpty ? secret : uri, m["user"] as? String) }
        default: break
        }
    }

    private func alive(_ tab: Tab, _ site: String) -> Bool {
        Browser.shared.active === tab && tab.view != nil && VaultCrypto.originOrNil(tab.url) == site
    }

    private func host(_ site: String) -> String { site.hasPrefix("https://") ? String(site.dropFirst(8)) : site }

    private func offerFill(_ tab: Tab, _ site: String) {
        account.openSilently { [weak self] _ in
            guard let self, let main = self.main else { return }
            let saved = self.account.savedLogins(site)
            if saved.isEmpty || !self.alive(tab, site) || Sheet.isShowing { return }
            let buttons = saved.prefix(2).enumerated().map { i, e in
                Sheet.Button(title: e.username.isEmpty ? L("no_username") : e.username, primary: i == 0) { [weak self] in self?.fill(tab, site, e); return true }
            }
            Sheet.form(L(saved.count == 1 ? "sign_in_saved_one" : "sign_in_saved_many"), detail: self.host(site), content: [], buttons: Array(buttons), in: main.view)
        }
    }

    private func fill(_ tab: Tab, _ site: String, _ e: VaultEntry) {
        guard alive(tab, site) else { return }
        if !e.username.isEmpty { recentUser[site] = e.username }
        tab.view?.evaluateJavaScript(Autofill.fillScript(site, e.username, e.password)) { [weak self] r, _ in
            if r as? String == "filled" { self?.main?.announce(L("filled_password")) }
        }
    }

    /// Once per session: the page's passkey request was refused by the system, not by you.
    private func passkeyFailed(_ tab: Tab, _ site: String) {
        guard !toldPasskeys, alive(tab, site), let main else { return }
        toldPasskeys = true
        Sheet.form(L("passkeys_unavailable"), detail: L("passkeys_unavailable_detail"), content: [], buttons: [Sheet.Button(title: L("ok"), primary: true) { true }], in: main.view)
    }

    private var neverSave: Set<String> { Set(d.stringArray(forKey: "never") ?? []) }

    private func offerSave(_ site: String, _ username: String, _ password: String) {
        if neverSave.contains(site) { return }
        account.openSilently { [weak self] _ in
            guard let self, let main = self.main else { return }
            let same = self.account.savedLogins(site).first { $0.username == username }
            if let same, same.password == password { return }
            let user = Sheet.field(L("username"), text: username)
            user.textContentType = .username
            let pass = Sheet.field(L("password"), text: password, secure: true)
            // Stays until you choose; the details can be corrected first.
            Sheet.form(L(same != nil ? "update_password_q" : "save_password_q"), detail: self.host(site), content: [user, pass], buttons: [
                Sheet.Button(title: L("never")) { [weak self] in
                    guard let self else { return true }
                    self.d.set(Array(self.neverSave.union([site])), forKey: "never")
                    main.announce(L("never_save_done"))
                    return true
                },
                Sheet.Button(title: L("not_now")) { true },
                Sheet.Button(title: L(same != nil ? "update" : "save"), primary: true) { [weak self] in
                    let value = pass.text ?? ""
                    if !value.isEmpty {
                        self?.account.storeLogin(site, user.text ?? "", value) { ok in main.announce(L(ok ? "password_saved" : "password_not_saved")) }
                    }
                    return true
                },
            ], in: main.view, closable: false)
        }
    }

    private func fillCode(_ tab: Tab, _ site: String) {
        account.openSilently { [weak self] _ in
            guard let self else { return }
            let keys = self.account.savedTotps(site)
            if keys.isEmpty || !self.alive(tab, site) { return }
            let entry = keys.first { $0.username == self.recentUser[site] } ?? keys[0]
            let left = Totp.secondsLeft(entry.totp)
            // In its last seconds the code is about to change; the next one is a moment away.
            DispatchQueue.main.asyncAfter(deadline: .now() + (left <= 2 ? Double(left) + 0.3 : 0)) { [weak self] in
                guard let self, let code = Totp.code(entry.totp), self.alive(tab, site) else { return }
                tab.view?.evaluateJavaScript(Autofill.codeScript(site, code)) { [weak self] r, _ in
                    if r as? String == "filled" { self?.main?.announce(L("filled_code")) }
                }
            }
        }
    }

    private func offerTotp(_ tab: Tab, _ site: String, _ text: String, _ userHint: String?) {
        guard let key = Totp.parseOrNil(text) else { return }
        let normalized = Totp.normalize(key)
        guard offeredKeys.insert(site + " " + normalized).inserted else { return }
        account.openSilently { [weak self] _ in
            guard let self, let main = self.main else { return }
            if self.account.savedTotps(site).contains(where: { Totp.parseOrNil($0.totp)?.secret == key.secret }) { return }
            let guess = [userHint, key.account, self.recentUser[site]].compactMap { $0 }.first { !$0.isEmpty } ?? ""
            let field = Sheet.field(L("account_name"), text: guess)
            Sheet.form(L("save_totp_q"), detail: L("save_totp_detail", self.host(site)), content: [field], buttons: [
                Sheet.Button(title: L("not_now")) { true },
                Sheet.Button(title: L("save"), primary: true) { [weak self] in
                    let name = (field.text ?? "").trimmingCharacters(in: .whitespaces)
                    self?.account.storeTotp(site, name, normalized) { ok in
                        main.announce(L(ok ? "totp_saved" : "password_not_saved"))
                        if ok { self?.fillCode(tab, site) }
                    }
                    return true
                },
            ], in: main.view, closable: false)
        }
    }

    // MARK: Scripts (desktop's FillScript and code filler, the same as Android)

    private static func js(_ s: String) -> String { JsonOut.write(s) }

    static func fillScript(_ origin: String, _ username: String, _ password: String) -> String {
        "(()=>{if(window.top!==window||location.origin!==" + js(origin) + ")return 'origin';" +
        "const shown=e=>e&&!e.disabled&&(e.offsetWidth>0||e.offsetHeight>0||e.getClientRects().length>0);" +
        "const set=(e,v)=>{Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(e,v);e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));};" +
        "const paymenty=e=>/^(cc-|transaction|one-time-code)/.test(e.autocomplete||'')||/card|cvv|cvc|csc|security.?code|expir|\\bpin\\b|otp|one.?time|verif|thẻ/i.test([e.name,e.id,e.placeholder,e.getAttribute('aria-label')].join(' '));" +
        "const p=[...document.querySelectorAll('input[type=password]')].find(e=>shown(e)&&!paymenty(e));const root=(p&&p.form)||document;" +
        "const searchy=e=>e.type==='search'||!!(e.closest&&e.closest('[role=search],[role=combobox],[role=searchbox]'))||/search|find|query|filter|tìm/i.test([e.name,e.id,e.placeholder,e.getAttribute('aria-label'),e.getAttribute('role')].join(' '));" +
        "const codey=e=>/one-time-code/.test(e.autocomplete||'')||/otp|2fa|mfa|one.?time|verif|auth.?code|security.?code|\\bcode\\b|\\bpin\\b|mã/i.test([e.name,e.id,e.placeholder,e.getAttribute('aria-label')].join(' '))||(e.maxLength>=4&&e.maxLength<=8&&/numeric|decimal|\\[0-9\\]|\\\\d/.test((e.inputMode||'')+' '+(e.getAttribute('pattern')||'')));" +
        "const all=[...root.querySelectorAll('input')].filter(i=>i.type!=='hidden'&&shown(i)&&!searchy(i)&&!codey(i));let u=all.find(i=>/username|email/.test(i.autocomplete||''));" +
        "if(!u&&p){const at=all.indexOf(p);for(let i=at-1;i>=0;i--)if(['text','email','tel',''].includes(all[i].type)){u=all[i];break;}}" +
        "if(!u&&!p)u=all.find(i=>['text','email',''].includes(i.type)&&/^(identifier|email|username|login|user)$/i.test(i.name||i.id||''))||null;" +
        "if(u&&!p&&u.value)return 'typed';" +
        "if(!p&&!u)return 'none';if(p&&p.value)return 'typed';if(u&&" + js(username) + ")set(u," + js(username) + ");if(p)set(p," + js(password) + ");return 'filled';})()"
    }

    static func codeScript(_ origin: String, _ code: String) -> String {
        "(()=>{if(window.top!==window||location.origin!==" + js(origin) + "||!window.__micaCodeTarget)return 'origin';const t=window.__micaCodeTarget();if(!t)return 'none';" +
        "const code=" + js(code) + ";const set=(e,v)=>{e.focus();Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(e,v);e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));};" +
        "if(t.length===1)set(t[0],code);else t.forEach((e,i)=>set(e,code[i]||''));return 'filled';})()"
    }
}
