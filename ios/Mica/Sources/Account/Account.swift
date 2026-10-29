import UIKit

/// The Mica account on this device, the same service and formats as desktop and Android:
///  - sign-in with Discord (a code shown here, typed on Discord's page), the token sealed in the Keychain;
///  - the password vault: encrypted on the device, opened silently for autofill with this device's key;
///    synced end-to-end encrypted, so a vault from Windows or Android opens here with the same master password;
///  - preferences (plain JSON, newest wins) and tabs sent between devices.
///
/// Vault state is touched on the main thread only; key derivation and network calls run on one worker.
final class Account {
    static let shared = Account()

    struct User { let id: String; let username: String; let avatar: String?; let token: String }

    private let worker = DispatchQueue(label: "mica-sync", qos: .utility)
    private let d = UserDefaults(suiteName: "account") ?? .standard
    private var listeners: [(AnyObject, () -> Void)] = []

    private let userFile = Sealed(name: "account")
    private let deviceKey = Sealed(name: "vault-key")
    private let deviceLogins = Sealed(name: "logins-device")
    let vaultFile = Files.support.appendingPathComponent("vault.enc.json")
    private let pendingFile = Files.support.appendingPathComponent("vault-passphrase-pending")

    private(set) var user: User?
    /// The open vault and the key that seals it (VaultCrypto.isKey); nil while locked.
    private(set) var vault: VaultPayload?
    private var vaultKey: String?
    private(set) var syncing = false
    private(set) var lastMessage: String?

    var signedIn: Bool { user != nil }
    var hasVault: Bool { FileManager.default.fileExists(atPath: vaultFile.path) }
    /// Made on its own when the first login was saved; a master password is still to be chosen.
    var passphrasePending: Bool { FileManager.default.fileExists(atPath: pendingFile.path) }

    private init() {
        // The Keychain outlives the app; a reinstall starts signed out, without the old device key.
        if !d.bool(forKey: "installed") {
            userFile.delete(); deviceKey.delete(); deviceLogins.delete()
            d.set(true, forKey: "installed")
        }
        user = loadUser()
    }

    func listen(_ owner: AnyObject, _ l: @escaping () -> Void) { listeners.append((owner, l)) }
    func unlisten(_ owner: AnyObject) { listeners.removeAll { $0.0 === owner } }
    private func changed() { listeners.map { $0.1 }.forEach { $0() } }

    func bg<T>(_ work: @escaping () throws -> T, done: @escaping (T?, Error?) -> Void) {
        worker.async {
            do { let r = try work(); DispatchQueue.main.async { done(r, nil) } } catch { DispatchQueue.main.async { done(nil, error) } }
        }
    }

    // MARK: Sign-in

    private func loadUser() -> User? {
        guard let s = userFile.read(), let m = (try? JSONSerialization.jsonObject(with: Data(s.utf8))) as? [String: Any],
              let id = m["id"] as? String, let token = m["token"] as? String else { return nil }
        return User(id: id, username: m["username"] as? String ?? "", avatar: m["avatar"] as? String, token: token)
    }

    final class SignIn {
        let code: String, url: String, requestId: String, verifier: String, until: Date
        var cancelled = false
        init(code: String, url: String, requestId: String, verifier: String, until: Date) {
            self.code = code; self.url = url; self.requestId = requestId; self.verifier = verifier; self.until = until
        }
    }

    /// Step 1 (worker): asks the service for a sign-in, checks the Discord address and the code to show.
    private func startSignIn() throws -> SignIn {
        let verifier = VaultCrypto.randomVerifier()
        let r: [String: Any]
        do { r = try Api.call("POST", "/v1/auth/discord/start", body: ["challenge": VaultCrypto.challenge(verifier), "confirm": true]) }
        catch let e as ApiError where e.code == "confirmation_required" { throw Problem("Update Mica to sign in.") }
        let url = r["authorizationUrl"] as? String ?? ""
        let u = URLComponents(string: url)
        guard let requestId = r["requestId"] as? String, let u, u.scheme == "https", u.host == "discord.com", u.port == nil, u.user == nil, u.password == nil,
              u.path == "/oauth2/authorize" || u.path == "/api/oauth2/authorize" else { throw Problem("The service returned an unsafe sign-in address.") }
        let code = r["confirmationCode"] as? String ?? ""
        if code.range(of: "^[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}$", options: .regularExpression) == nil {
            throw Problem("The service didn't send a sign-in code, so Mica won't continue.")
        }
        return SignIn(code: code, url: url, requestId: requestId, verifier: verifier, until: Date().addingTimeInterval(600))
    }

    /// Step 2 (its own thread): polls every 2 s until Discord approves, the user cancels or 10 minutes pass.
    private func finishSignIn(_ s: SignIn) throws -> User? {
        while Date() < s.until && !s.cancelled {
            Thread.sleep(forTimeInterval: 2)
            if s.cancelled { return nil }
            let r: [String: Any]
            do { r = try Api.call("POST", "/v1/auth/discord/poll", body: ["requestId": s.requestId, "verifier": s.verifier]) }
            catch let e as ApiError {
                if e.status == 429 { continue }
                if e.code == "authorization_failed" { throw Problem("Sign-in was cancelled in the browser. Try again.") }
                throw e
            }
            if r["status"] as? String != "ready" { continue }
            let u = r["user"] as? [String: Any]
            guard let token = r["token"] as? String, !token.isEmpty, let id = u?["id"] as? String, !id.isEmpty else { throw Problem("Invalid sign-in response.") }
            let user = User(id: id, username: u?["username"] as? String ?? "", avatar: u?["avatar"] as? String, token: token)
            try userFile.write(JsonOut.write(["id": user.id, "username": user.username, "avatar": user.avatar ?? NSNull(), "token": user.token] as [String: Any]))
            return user
        }
        if !s.cancelled { throw Problem("Sign-in timed out. Please try again.") }
        return nil
    }

    /// The sign-in waiting for Discord, while its code is on screen.
    private(set) var signIn: SignIn?
    var onSignedIn: ((User?, Error?) -> Void)?

    /// Starts a sign-in; [started] gets it (code and Discord address) or the error. Polling runs on its own thread.
    func beginSignIn(_ started: @escaping (SignIn?, Error?) -> Void) {
        signIn?.cancelled = true
        bg({ try self.startSignIn() }) { s, e in
            guard let s else { return started(nil, e) }
            self.signIn = s
            self.changed()
            started(s, nil)
            Thread {
                var user: User?, error: Error?
                do { user = try self.finishSignIn(s) } catch let x { error = x }
                DispatchQueue.main.async {
                    if self.signIn === s { self.signIn = nil }
                    if let user { self.didSignIn(user) } else { self.changed() }
                    if !s.cancelled || user != nil { self.onSignedIn?(user, error) }
                }
            }.start()
        }
    }

    func cancelSignIn() { signIn?.cancelled = true; signIn = nil; changed() }

    /// After finishSignIn: settings and vault follow the account from now on.
    private func didSignIn(_ u: User) {
        user = u
        changed()
        pullSettings()
        checkSentTabs()
        if vault != nil && !passphrasePending { sync() }
    }

    func signOut() {
        guard let previous = user else { return }
        user = nil
        userFile.delete()
        changed()
        worker.async { _ = try? Api.call("POST", "/v1/auth/logout", body: [String: Any](), token: previous.token) }
    }

    private func authed(_ method: String, _ route: String, body: Any? = nil, timeout: TimeInterval = 15) throws -> [String: Any]? {
        guard let u = user else { return nil }
        do { return try Api.call(method, route, body: body, token: u.token, timeout: timeout) }
        catch let e as ApiError {
            if e.status == 401 { DispatchQueue.main.async { if self.user?.token == u.token { self.user = nil; self.userFile.delete(); self.changed() } } }
            throw e
        }
    }

    // MARK: The vault on this device

    private func readEnvelope() throws -> Envelope { try Envelope.parse(Data(contentsOf: vaultFile)) }
    private func writeEnvelope(_ e: Envelope) throws {
        try Data(e.json.utf8).write(to: vaultFile, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
    }

    /// Opens the vault with this device's key, without asking (autofill). Calls back on the main thread.
    func openSilently(_ done: @escaping (Bool) -> Void) {
        if vault != nil { return done(true) }
        guard hasVault, deviceKey.exists else { return done(false) }
        bg({ () -> (String, VaultPayload) in
            guard let key = self.deviceKey.read() else { throw VaultError("No device key.") }
            return (key, try VaultCrypto.decrypt(try self.readEnvelope(), key))
        }) { r, e in
            if let r {
                if self.vault == nil { self.vault = r.1; self.vaultKey = r.0; self.mergeDeviceLogins() }
                self.changed(); done(true)
            } else {
                if e is VaultError { self.deviceKey.delete() }
                done(false)
            }
        }
    }

    /// Opens (or, with no vault yet, creates) the vault with the master password.
    func unlock(_ password: String, done: @escaping (Error?) -> Void) {
        let pending = passphrasePending && hasVault && deviceKey.exists
        if (pending || !hasVault) && password.count < 12 { return done(Problem(L("master_too_short"))) }
        let exists = hasVault
        bg({ () -> (String, VaultPayload) in
            if pending {
                guard let dk = self.deviceKey.read() else { throw VaultError("No device key.") }
                let opened = try VaultCrypto.decrypt(try self.readEnvelope(), dk)
                let key = try VaultCrypto.newKey(password)
                try self.writeEnvelope(try VaultCrypto.encrypt(opened, key))
                try? FileManager.default.removeItem(at: self.pendingFile)
                return (key, opened)
            }
            if exists { let e = try self.readEnvelope(); let key = try VaultCrypto.keyFor(e, password); return (key, try VaultCrypto.decrypt(e, key)) }
            let key = try VaultCrypto.newKey(password)
            let p = VaultPayload()
            try self.writeEnvelope(try VaultCrypto.encrypt(p, key))
            return (key, p)
        }) { r, e in
            guard let r else { return done(e) }
            self.adopt(r.1, key: r.0)
            done(nil)
            if self.user != nil { self.sync() }
        }
    }

    private func adopt(_ p: VaultPayload, key: String) {
        vault = p; vaultKey = key
        try? deviceKey.write(key)
        mergeDeviceLogins()
        changed()
    }

    /// Locking also turns off silent autofill until the next unlock, as on desktop.
    func lock() {
        vault?.entries.removeAll()
        vault = nil; vaultKey = nil
        deviceKey.delete()
        changed()
    }

    /// Re-encrypts and writes the open vault (worker), then syncs a moment later when signed in.
    func save(_ done: ((Error?) -> Void)? = nil) {
        guard let p = vault, let key = vaultKey else { return }
        let copy = p.clone()
        d.set(true, forKey: "dirty")
        bg({ try self.writeEnvelope(try VaultCrypto.encrypt(copy, key)) }) { _, e in
            done?(e); self.changed()
            if e == nil { self.syncLater() }
        }
    }

    func savedLogins(_ origin: String) -> [VaultEntry] {
        var all = vault?.entries ?? []
        for e in loadDeviceLogins() where !all.contains(where: { $0.origin == e.origin && $0.username == e.username }) { all.append(e) }
        return all.filter { $0.origin == origin && !$0.password.isEmpty }
    }

    func savedTotps(_ origin: String) -> [VaultEntry] { vault?.entries.filter { !($0.totp ?? "").isEmpty && $0.origin == origin } ?? [] }

    /// First save ever: the vault is made now with a key kept on this device; the master password comes later.
    private func startDeviceVault() -> Bool {
        let key = VaultCrypto.newDeviceKey()
        do { try deviceKey.write(key); try Data().write(to: pendingFile) } catch { return false }
        vault = VaultPayload(); vaultKey = key
        return true
    }

    /// A login from a page's save card. A locked vault keeps it on the device until the next unlock.
    func storeLogin(_ origin: String, _ username: String, _ password: String, done: @escaping (Bool) -> Void) {
        if vault == nil && !hasVault && !startDeviceVault() { return done(false) }
        guard let v = vault else {
            var list = loadDeviceLogins()
            list.removeAll { $0.origin == origin && $0.username == username }
            if let e = try? VaultEntry.make(origin, username, password) { list.append(e) }
            saveDeviceLogins(list)
            return done(true)
        }
        let entry = v.entries.first { $0.origin == origin && $0.username == username } ?? {
            let e = VaultEntry(id: VaultEntry.newId(), origin: origin, username: username, password: ""); v.entries.append(e); return e
        }()
        entry.password = password
        v.touch(entry)
        save { done($0 == nil) }
    }

    func storeTotp(_ origin: String, _ username: String, _ normalized: String, done: @escaping (Bool) -> Void) {
        if vault == nil && !hasVault && !startDeviceVault() { return done(false) }
        guard let v = vault else { return done(false) }
        let logins = v.entries.filter { $0.origin == origin }
        let entry = logins.first { $0.username == username } ?? (username.isEmpty && logins.count == 1 ? logins[0] : nil) ?? {
            let e = VaultEntry(id: VaultEntry.newId(), origin: origin, username: username, password: ""); v.entries.append(e); return e
        }()
        entry.totp = normalized
        v.touch(entry)
        save { done($0 == nil) }
    }

    /// A login added or edited; [was] is its key before the edit, so a renamed login doesn't come back under its old name.
    func put(_ entry: VaultEntry, was: String? = nil, done: @escaping (Error?) -> Void) {
        guard let v = vault else { return done(Problem(L("vault_locked"))) }
        if !v.entries.contains(where: { $0 === entry }) { v.entries.append(entry) }
        if let was, was != entry.key { v.forgetKey(was) }
        v.touch(entry)
        save(done)
    }

    func remove(_ entry: VaultEntry, done: @escaping (Error?) -> Void) {
        guard let v = vault else { return done(Problem(L("vault_locked"))) }
        v.forget(entry)
        save(done)
    }

    /// Logins from a CSV export; they go into the open vault, or wait on the device while it is locked.
    func importLogins(_ logins: [PasswordCsv.Login], done: @escaping (Int) -> Void) {
        if logins.isEmpty { return done(0) }
        guard let v = vault else {
            var list = loadDeviceLogins()
            for l in logins {
                list.removeAll { $0.origin == l.origin && $0.username == l.username }
                list.append(VaultEntry(id: VaultEntry.newId(), origin: l.origin, username: l.username, password: l.password))
            }
            saveDeviceLogins(list)
            return done(logins.count)
        }
        for l in logins { put(l.origin, l.username, l.password, into: v) }
        save { done($0 == nil ? logins.count : 0) }
    }

    private func put(_ origin: String, _ username: String, _ password: String, into v: VaultPayload) {
        let e = v.entries.first { $0.origin == origin && $0.username == username } ?? {
            let e = VaultEntry(id: VaultEntry.newId(), origin: origin, username: username, password: ""); v.entries.append(e); return e
        }()
        e.password = password
        v.touch(e)
    }

    private func loadDeviceLogins() -> [VaultEntry] {
        guard let s = deviceLogins.read() else { return [] }
        return (try? VaultPayload.parse(Data(s.utf8)).entries) ?? []
    }

    private func saveDeviceLogins(_ list: [VaultEntry]) {
        let p = VaultPayload()
        p.entries = list
        try? deviceLogins.write(p.json)
    }

    private func mergeDeviceLogins() {
        guard let v = vault else { return }
        let waiting = loadDeviceLogins()
        if waiting.isEmpty { return }
        for l in waiting { put(l.origin, l.username, l.password, into: v) }
        save { if $0 == nil { self.deviceLogins.delete() } }
    }

    // MARK: Vault sync

    private var syncTimer: Timer?
    private func syncLater() {
        guard user != nil, !passphrasePending else { return }
        syncTimer?.invalidate()
        syncTimer = Timer.scheduledTimer(withTimeInterval: 3, repeats: false) { [weak self] _ in self?.sync() }
    }

    private func seenRevision(_ account: String) -> Int64 { (d.object(forKey: "seen:" + account) as? NSNumber)?.int64Value ?? 0 }
    private func rememberRevision(_ account: String, _ r: Int64) { if r > seenRevision(account) { d.set(NSNumber(value: r), forKey: "seen:" + account) } }

    enum Outcome {
        case done(String)
        /// The cloud copy uses a different master password: ask for it and call sync(password:).
        case needPassword
        case failed(Error)
    }

    /// Desktop's sync: the cloud copy is fetched, opened and merged into a copy of this device's vault (every login
    /// either has, the newest save of each, minus deletions), and the result goes up if it differs from the cloud's.
    /// Neither copy ever replaces the other. A conflict (another device pushed in between) merges again.
    func sync(password: String? = nil, _ done: ((Outcome) -> Void)? = nil) { sync(password, retries: 0, done) }

    private func sync(_ password: String?, retries: Int, _ done: ((Outcome) -> Void)?) {
        guard let u = user, !syncing else { done?(.failed(Problem(L("sign_in_first")))); return }
        if vault == nil && hasVault && password == nil {
            // Locked: this device's key may still open it.
            if deviceKey.exists { openSilently { ok in if ok { self.sync(nil, retries: retries, done) } else { done?(.needPassword) } } }
            else { done?(.needPassword) }
            return
        }
        syncing = true
        changed()
        let local = vault?.clone()
        let localKey = vaultKey
        let pending = passphrasePending
        let seen = seenRevision(u.id)
        bg({ try self.syncOnWorker(u, local, localKey, pending, seen, password) }) { r, e in
            self.syncing = false
            guard let r else {
                if let a = e as? ApiError, a.status == 409, retries < 2 { self.changed(); return self.sync(password, retries: retries + 1, done) }
                self.lastMessage = nil
                self.changed()
                if let v = e as? VaultError, v.wrongPassword { done?(.needPassword) } else { done?(.failed(e ?? Problem(L("sync_unreachable")))) }
                return
            }
            if r.needPassword { self.changed(); done?(.needPassword); return }
            var savedMeanwhile = false
            if let payload = r.payload, let key = r.key {
                if pending { try? FileManager.default.removeItem(at: self.pendingFile) }
                // Logins saved here while the sync ran fold into the result rather than being lost.
                let merged = payload.clone()
                if let live = self.vault { merged.merge(live); savedMeanwhile = !merged.sameLogins(payload) }
                self.vault = merged; self.vaultKey = key
                try? self.deviceKey.write(key)
            }
            if let rev = r.revision { self.rememberRevision(u.id, rev) }
            if r.pushed { self.d.set(false, forKey: "dirty") }
            if savedMeanwhile { self.save() }
            self.d.set(Date().timeIntervalSince1970, forKey: "lastSync")
            self.lastMessage = L(r.message)
            self.changed()
            done?(.done(L(r.message)))
        }
    }

    private struct Synced { var version: Int; var payload: VaultPayload?; var key: String?; var revision: Int64?; var pushed: Bool; var message: String; var needPassword = false }

    private func syncOnWorker(_ u: User, _ local: VaultPayload?, _ localKey: String?, _ pending: Bool, _ seen: Int64, _ password: String?) throws -> Synced {
        let me = try authed("GET", "/v1/account")?["user"] as? [String: Any]
        if me?["id"] as? String != u.id { throw Problem("Account verification failed. Sign out and sign in again.") }
        guard let remote = try authed("GET", "/v1/vault") else { throw Problem(L("sign_in_first")) }
        guard let version = (remote["version"] as? NSNumber)?.intValue else { throw Problem("Invalid remote vault.") }
        guard let envMap = remote["envelope"] as? [String: Any] else {
            // Nothing in the cloud yet: this device's vault goes up, once it has a real master password.
            guard let local, let localKey, !pending else { return Synced(version: version, payload: nil, key: nil, revision: nil, pushed: false, message: "sync_nothing_yet") }
            return try push(u, local, localKey, version, seen, adopt: nil)
        }
        let env = Envelope.from(envMap)

        // Open the cloud copy: this device's key first, then the password typed for it.
        var key: String?
        var pulled: VaultPayload?
        if let localKey, !pending, let k = try? VaultCrypto.keyFor(env, localKey) { key = k; pulled = try? VaultCrypto.decrypt(env, k) }
        if pulled == nil, let password { let k = try VaultCrypto.keyFor(env, password); key = k; pulled = try VaultCrypto.decrypt(env, k) }
        guard let pulled, let key else { return Synced(version: version, payload: nil, key: nil, revision: nil, pushed: false, message: "", needPassword: true) }
        if let a = pulled.accountId, !a.isEmpty, a != u.id { throw Problem("This vault belongs to another account.") }

        // This device's copy is the base (it alone has what was saved here); a vault still waiting for its master
        // password takes the cloud's instead, with its key, and adds its logins to it.
        let mine = local != nil && localKey != nil && !pending && key == localKey
        let base = (mine ? local! : pulled).clone()
        base.merge(mine ? pulled : (local ?? pulled))
        let localChanged = local == nil || localKey != key || pending || !base.sameLogins(local!)
        if localChanged { backupLocal() }
        if base.sameLogins(pulled) {
            // Nothing the cloud lacks: keep its revision, write the merged copy here only if it changed.
            base.revision = max(pulled.revision, local?.revision ?? 0)
            if localChanged { try writeEnvelope(try VaultCrypto.encrypt(base, key)) }
            let upToDate = local != nil && base.sameLogins(local!)
            return Synced(version: version, payload: base, key: key, revision: pulled.revision, pushed: false, message: upToDate ? "sync_up_to_date" : "sync_downloaded")
        }
        base.revision = max(local?.revision ?? 0, pulled.revision, seen)
        return try push(u, base, key, version, seen, adopt: base)
    }

    private func push(_ u: User, _ payload: VaultPayload, _ key: String, _ version: Int, _ seen: Int64, adopt: VaultPayload?) throws -> Synced {
        if let a = payload.accountId, !a.isEmpty, a != u.id { throw Problem("Local vault belongs to another account. Sign in to that account.") }
        payload.accountId = u.id
        payload.revision = max(payload.revision, seen) + 1
        let env = try VaultCrypto.encrypt(payload, key)
        try writeEnvelope(env)
        guard let r = try authed("PUT", "/v1/vault", body: ["version": version, "envelope": env.map] as [String: Any]) else { throw Problem(L("sign_in_first")) }
        let now = (r["version"] as? NSNumber)?.intValue ?? -1
        if now <= version { throw Problem("Push result could not be verified. Sync again.") }
        return Synced(version: now, payload: adopt ?? payload, key: key, revision: payload.revision, pushed: true, message: "sync_uploaded")
    }

    private func backupLocal() {
        guard hasVault else { return }
        let to = Files.support.appendingPathComponent("vault-before-pull.enc.json")
        try? FileManager.default.removeItem(at: to)
        try? FileManager.default.copyItem(at: vaultFile, to: to)
    }

    // MARK: Preferences

    /// What desktop syncs: dark, ambientYoutube, strictTracking. iOS only uses dark and keeps the rest as received.
    var applyDark: ((Bool) -> Void)?
    var currentDark: (() -> Bool)?

    private func snapshot() -> [String: Any] {
        let others = d.string(forKey: "settingsOthers").flatMap { (try? JSONSerialization.jsonObject(with: Data($0.utf8))) as? [String: Any] } ?? [:]
        return ["dark": currentDark?() ?? false, "ambientYoutube": others["ambientYoutube"] as? Bool ?? false, "strictTracking": others["strictTracking"] as? Bool ?? false]
    }

    private var settingsUpdated: Int64 {
        get { (d.object(forKey: "settingsUpdated") as? NSNumber)?.int64Value ?? 0 }
        set { d.set(NSNumber(value: newValue), forKey: "settingsUpdated") }
    }

    /// The user changed the theme here: newer than the cloud copy, so it goes up.
    func noteSettingsChange() {
        settingsUpdated = VaultPayload.now()
        d.set(true, forKey: "settingsPending")
        pushSettings()
    }

    private func pushSettings() {
        guard user != nil, d.bool(forKey: "settingsPending") else { return }
        let body: [String: Any] = ["settings": snapshot(), "updatedAt": settingsUpdated]
        bg({ try self.authed("PUT", "/v1/settings", body: body, timeout: 10) }) { r, _ in
            if let r = r ?? nil { self.d.set(false, forKey: "settingsPending"); self.receive(r) }
        }
    }

    func pullSettings() {
        guard user != nil else { return }
        bg({ try self.authed("GET", "/v1/settings", timeout: 10) }) { r, _ in
            guard let r = r ?? nil else { return }
            let remoteAt = (r["updatedAt"] as? NSNumber)?.int64Value ?? 0
            if !(r["settings"] is [String: Any]) || remoteAt < self.settingsUpdated {
                if self.settingsUpdated == 0 { self.settingsUpdated = VaultPayload.now() }
                self.d.set(true, forKey: "settingsPending")
                self.pushSettings()
            } else { self.receive(r) }
        }
    }

    private func receive(_ r: [String: Any]) {
        guard let s = r["settings"] as? [String: Any], let at = (r["updatedAt"] as? NSNumber)?.int64Value else { return }
        if at <= settingsUpdated && settingsUpdated != 0 { return }
        settingsUpdated = at
        d.set(JsonOut.write(s.filter { $0.key != "dark" }), forKey: "settingsOthers")
        if let dark = s["dark"] as? Bool, currentDark?() != dark { applyDark?(dark) }
    }

    // MARK: Send tab

    var deviceId: String {
        if let id = d.string(forKey: "deviceId") { return id }
        let id = VaultCrypto.base64Url(VaultCrypto.randomBytes(18))
        d.set(id, forKey: "deviceId")
        return id
    }
    let deviceName: String = {
        let n = UIDevice.current.name
        return n.isEmpty ? UIDevice.current.model : n
    }()

    struct Device { let id: String; let name: String; let seen: Int64; let isThis: Bool }
    struct SentTab { let id: String; let from: String; let url: String; let title: String }

    /// Worker: names this device to the account and returns the account's devices.
    private func listDevices() throws -> [Device]? {
        guard let r = try authed("POST", "/v1/devices", body: ["id": deviceId, "name": deviceName], timeout: 10) else { return nil }
        let me = deviceId
        return (r["devices"] as? [Any] ?? []).compactMap { x in
            guard let m = x as? [String: Any], let id = m["id"] as? String else { return nil }
            return Device(id: id, name: m["name"] as? String ?? "", seen: (m["seen"] as? NSNumber)?.int64Value ?? 0, isThis: id == me)
        }
    }

    func devices(_ done: @escaping ([Device]) -> Void) {
        bg({ try self.listDevices() ?? [] }) { list, _ in done(list ?? []) }
    }

    /// Sends a tab; [done] gets nil, or what went wrong in words.
    func send(url: String, title: String, to device: String, done: @escaping (String?) -> Void) {
        let body: [String: Any] = ["to": device, "from": deviceName, "url": url, "title": String(title.prefix(300))]
        bg({ () -> Bool in
            guard let r = try self.authed("POST", "/v1/tabs", body: body, timeout: 10), r["id"] is String else { throw Problem("No id for the sent tab.") }
            return true
        }) { _, e in done(e.map { Account.sendError($0) }) }
    }

    static func sendError(_ e: Error) -> String {
        if let a = e as? ApiError {
            if a.status == 401 { return L("signed_out_again") }
            if a.code == "unknown_device" { return L("send_unknown_device") }
            if a.code == "too_many_tabs" { return L("send_too_many") }
            return L("send_failed", "\(a.status)" + (a.code.map { " " + $0 } ?? ""))
        }
        return L("sync_unreachable")
    }

    var onTabsArrived: (([SentTab]) -> Void)?
    private var checking = false
    private var lastCheck = Date.distantPast

    /// Picks up tabs sent from other devices: on launch, on return to the app (at most every [gap]) and every 2 minutes while open.
    func checkSentTabs(force: Bool = true, gap: TimeInterval = 110) {
        guard user != nil, !checking, force || Date().timeIntervalSince(lastCheck) >= gap else { return }
        checking = true
        lastCheck = Date()
        let me = deviceId
        bg({ () -> [SentTab] in
            if try self.listDevices() == nil { return [] }
            let q = me.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? me
            guard let r = try self.authed("GET", "/v1/tabs?device=" + q, timeout: 10) else { return [] }
            let tabs = (r["tabs"] as? [Any] ?? []).compactMap { x -> SentTab? in
                guard let m = x as? [String: Any], let id = m["id"] as? String else { return nil }
                return SentTab(id: id, from: m["from"] as? String ?? "", url: m["url"] as? String ?? "", title: m["title"] as? String ?? "")
            }
            for t in tabs { _ = try? self.authed("DELETE", "/v1/tabs/" + (t.id.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? t.id), timeout: 10) }
            return tabs
        }) { tabs, _ in
            self.checking = false
            if let tabs, !tabs.isEmpty { self.onTabsArrived?(tabs) }
        }
    }

    /// Back in the app: tabs waiting, preferences, and the vault if it's been a while.
    func onForeground() {
        guard user != nil else { return }
        // Coming back is when a tab just sent from the computer is wanted.
        checkSentTabs(force: false, gap: 20)
        pushSettings()
        if vault != nil && !passphrasePending && Date().timeIntervalSince1970 - d.double(forKey: "lastSync") > 15 * 60 { sync() }
    }
}
