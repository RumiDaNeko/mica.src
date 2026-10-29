package app.mica.account

import android.content.Context
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import app.mica.blocker.Json
import java.io.File
import java.util.concurrent.Executors

/**
 * The Mica account on this device, the same service and formats as desktop:
 *  - sign-in with Discord (a code shown here, typed on Discord's page), the token sealed in the Keystore;
 *  - the password vault: encrypted on the device, opened silently for autofill with this device's key;
 *    synced end-to-end encrypted, so a vault from Windows opens here with the same master password;
 *  - preferences (plain JSON, newest wins) and tabs sent between devices.
 *
 * Vault state is touched on the main thread only; key derivation and network calls run on one worker.
 */
class Account(private val context: Context) {
    class User(val id: String, val username: String, val avatar: String?, val token: String)

    private val dir = context.filesDir
    private val worker = Executors.newSingleThreadExecutor { r -> Thread(r, "mica-sync").apply { priority = Thread.NORM_PRIORITY - 1 } }
    private val main = Handler(Looper.getMainLooper())
    private val prefs = context.getSharedPreferences("account", Context.MODE_PRIVATE)
    private val listeners = ArrayList<() -> Unit>()

    private val userFile = Sealed(File(dir, "account.bin"), "mica.account")
    private val deviceKey = Sealed(File(dir, "vault-key.bin"), "mica.vaultkey")
    private val deviceLogins = Sealed(File(dir, "logins-device.bin"), "mica.logins")
    val vaultFile = File(dir, "vault.enc.json")
    private val pendingFile = File(dir, "vault-passphrase-pending")

    var user: User? = loadUser(); private set
    /** The open vault and the key that seals it (VaultCrypto.isKey); null while locked. */
    var vault: VaultPayload? = null; private set
    private var vaultKey: String? = null
    var syncing = false; private set
    var lastMessage: String? = null; private set

    val hasVault get() = vaultFile.exists()
    /** Made on its own when the first login was saved; a master password is still to be chosen. */
    val passphrasePending get() = pendingFile.exists()

    fun listen(l: () -> Unit) { listeners.add(l) }
    fun unlisten(l: () -> Unit) { listeners.remove(l) }
    private fun changed() { listeners.toList().forEach { it() } }

    fun <T> bg(work: () -> T, done: (T?, Exception?) -> Unit) {
        worker.execute {
            val r = try { Result.success(work()) } catch (e: Exception) { Result.failure(e) }
            main.post { done(r.getOrNull(), r.exceptionOrNull() as? Exception) }
        }
    }

    // ---- Sign-in ----

    private fun loadUser(): User? = userFile.read()?.let { s ->
        try { val m = Json.parse(s) as Map<*, *>; User(m["id"] as String, m["username"] as? String ?: "", m["avatar"] as? String, m["token"] as String) } catch (e: Exception) { null }
    }

    class SignIn(val code: String, val url: String, val requestId: String, val verifier: String, val until: Long) { @Volatile var cancelled = false }

    /** Step 1 (worker): asks the service for a sign-in, checks the Discord address and the code to show. */
    fun startSignIn(): SignIn {
        val verifier = VaultCrypto.randomVerifier()
        val r = try { Api.call("POST", "/v1/auth/discord/start", mapOf("challenge" to VaultCrypto.challenge(verifier), "confirm" to true)) }
            catch (e: ApiException) { if (e.code == "confirmation_required") throw IllegalStateException("Update Mica to sign in."); throw e }
        val url = r["authorizationUrl"] as? String ?: ""
        val u = try { java.net.URI(url) } catch (e: Exception) { null }
        if (r["requestId"] !is String || u == null || u.scheme != "https" || u.host != "discord.com" || u.port != -1 || u.rawUserInfo != null || (u.path != "/oauth2/authorize" && u.path != "/api/oauth2/authorize"))
            throw IllegalStateException("The service returned an unsafe sign-in address.")
        val code = r["confirmationCode"] as? String ?: ""
        if (!Regex("^[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}$").matches(code)) throw IllegalStateException("The service didn't send a sign-in code, so Mica won't continue.")
        return SignIn(code, url, r["requestId"] as String, verifier, System.currentTimeMillis() + 10 * 60_000)
    }

    /** Step 2 (worker): polls every 2 s until Discord approves, the user cancels or 10 minutes pass. */
    fun finishSignIn(s: SignIn): User? {
        while (System.currentTimeMillis() < s.until && !s.cancelled) {
            Thread.sleep(2000)
            if (s.cancelled) return null
            val r = try { Api.call("POST", "/v1/auth/discord/poll", mapOf("requestId" to s.requestId, "verifier" to s.verifier)) }
                catch (e: ApiException) {
                    if (e.status == 429) continue
                    if (e.code == "authorization_failed") throw IllegalStateException("Sign-in was cancelled in the browser. Try again.")
                    throw e
                }
            if (r["status"] != "ready") continue
            val u = r["user"] as? Map<*, *>
            val token = r["token"] as? String
            val id = u?.get("id") as? String
            if (token.isNullOrEmpty() || id.isNullOrEmpty()) throw IllegalStateException("Invalid sign-in response.")
            val user = User(id, u["username"] as? String ?: "", u["avatar"] as? String, token)
            userFile.write(JsonOut.write(mapOf("id" to user.id, "username" to user.username, "avatar" to user.avatar, "token" to user.token)))
            return user
        }
        if (!s.cancelled) throw IllegalStateException("Sign-in timed out. Please try again.")
        return null
    }

    /** The sign-in waiting for Discord, while its code is on screen. */
    var signIn: SignIn? = null; private set
    var onSignedIn: ((User?, Exception?) -> Unit)? = null

    /** Starts a sign-in; [started] gets it (code and Discord address) or the error. Polling runs on its own thread. */
    fun beginSignIn(started: (SignIn?, Exception?) -> Unit) {
        signIn?.cancelled = true
        bg({ startSignIn() }) { s, e ->
            if (s == null) return@bg started(null, e)
            signIn = s; changed(); started(s, null)
            Thread({
                val r = try { Result.success(finishSignIn(s)) } catch (x: Exception) { Result.failure(x) }
                main.post {
                    if (signIn === s) signIn = null
                    val u = r.getOrNull()
                    if (u != null) signedIn(u) else changed()
                    if (!s.cancelled || u != null) onSignedIn?.invoke(u, r.exceptionOrNull() as? Exception)
                }
            }, "mica-signin").start()
        }
    }

    fun cancelSignIn() { signIn?.cancelled = true; signIn = null; changed() }

    /** Main thread, after finishSignIn: settings and vault follow the account from now on. */
    fun signedIn(u: User) {
        user = u
        remoteVersion = -1
        changed()
        pullSettings()
        checkSentTabs()
        if (vault != null && !passphrasePending) sync()
    }

    fun signOut() {
        val previous = user ?: return
        user = null; remoteVersion = -1
        userFile.delete()
        changed()
        worker.execute { try { Api.call("POST", "/v1/auth/logout", emptyMap<String, Any>(), previous.token) } catch (e: Exception) {} }
    }

    private fun authed(method: String, route: String, body: Any? = null, timeoutMs: Int = 15000): Map<*, *>? {
        val u = user ?: return null
        return try { Api.call(method, route, body, u.token, timeoutMs) }
        catch (e: ApiException) { if (e.status == 401) main.post { if (user === u) { user = null; userFile.delete(); changed() } }; throw e }
    }

    // ---- The vault on this device ----

    private fun readEnvelope() = Envelope.parse(vaultFile.readText())
    private fun writeEnvelope(e: Envelope) {
        val af = android.util.AtomicFile(vaultFile)
        val out = af.startWrite()
        try { out.write(e.toJson().toByteArray(Charsets.UTF_8)); af.finishWrite(out) } catch (x: Exception) { af.failWrite(out); throw x }
    }

    /** Opens the vault with this device's key, without asking (autofill). Calls back on the main thread. */
    fun openSilently(done: (Boolean) -> Unit) {
        if (vault != null) return done(true)
        if (!vaultFile.exists() || !deviceKey.exists) return done(false)
        bg({ val key = deviceKey.read() ?: throw VaultException("No device key."); key to VaultCrypto.decrypt(readEnvelope(), key) }) { r, e ->
            if (r != null) { if (vault == null) { vault = r.second; vaultKey = r.first; mergeDeviceLogins() }; changed(); done(true) }
            else { if (e is VaultException) deviceKey.delete(); done(false) }
        }
    }

    /** Opens (or, with no vault yet, creates) the vault with the master password. */
    fun unlock(password: String, done: (Exception?) -> Unit) {
        val pending = passphrasePending && vaultFile.exists() && deviceKey.exists
        if ((pending || !vaultFile.exists()) && password.length < 12) return done(IllegalArgumentException(context.getString(app.mica.R.string.master_too_short)))
        bg({
            when {
                pending -> {
                    val opened = VaultCrypto.decrypt(readEnvelope(), deviceKey.read() ?: throw VaultException("No device key."))
                    val key = VaultCrypto.newKey(password)
                    writeEnvelope(VaultCrypto.encrypt(opened, key)); pendingFile.delete()
                    key to opened
                }
                vaultFile.exists() -> { val e = readEnvelope(); val key = VaultCrypto.keyFor(e, password); key to VaultCrypto.decrypt(e, key) }
                else -> { val key = VaultCrypto.newKey(password); val p = VaultPayload(); writeEnvelope(VaultCrypto.encrypt(p, key)); key to p }
            }
        }) { r, e ->
            if (r == null) return@bg done(e)
            adopt(r.second, r.first)
            done(null)
            if (user != null) sync()
        }
    }

    private fun adopt(p: VaultPayload, key: String) {
        vault = p; vaultKey = key
        try { deviceKey.write(key) } catch (e: Exception) {}
        mergeDeviceLogins()
        changed()
    }

    /** Locking also turns off silent autofill until the next unlock, as on desktop. */
    fun lock() {
        vault?.entries?.clear()
        vault = null; vaultKey = null
        deviceKey.delete()
        changed()
    }

    /** Re-encrypts and writes the open vault (worker), then syncs a moment later when signed in. */
    fun save(done: ((Exception?) -> Unit)? = null) {
        val p = vault ?: return
        val key = vaultKey ?: return
        val copy = VaultPayload.parse(p.toJson())
        prefs.edit().putBoolean("dirty", true).apply()
        bg({ writeEnvelope(VaultCrypto.encrypt(copy, key)) }) { _, e -> done?.invoke(e); changed(); if (e == null) syncLater() }
    }

    fun savedLogins(origin: String): List<VaultEntry> {
        val all = ArrayList<VaultEntry>()
        vault?.entries?.let { all.addAll(it) }
        for (e in loadDeviceLogins()) if (all.none { it.origin == e.origin && it.username == e.username }) all.add(e)
        return all.filter { it.origin == origin && it.password.isNotEmpty() }
    }

    fun savedTotps(origin: String) = vault?.entries?.filter { !it.totp.isNullOrEmpty() && it.origin == origin } ?: emptyList()

    /** A login from a page's save card. A locked vault keeps it on the device until the next unlock. */
    fun storeLogin(origin: String, username: String, password: String, done: (Boolean) -> Unit) {
        val p = vault
        if (p == null && !vaultFile.exists()) {
            // First save ever: make the vault now with a key kept on this device; the master password comes later.
            val key = VaultCrypto.newDeviceKey()
            try { deviceKey.write(key); pendingFile.writeText("") } catch (e: Exception) { return done(false) }
            vault = VaultPayload(); vaultKey = key
        }
        val v = vault
        if (v == null) {
            val list = loadDeviceLogins().toMutableList()
            list.removeAll { it.origin == origin && it.username == username }
            list.add(VaultEntry.new(origin, username, password))
            saveDeviceLogins(list)
            return done(true)
        }
        val entry = v.entries.firstOrNull { it.origin == origin && it.username == username } ?: VaultEntry.new(origin, username, "").also { v.entries.add(it) }
        entry.password = password
        v.touch(entry)
        save { done(it == null) }
    }

    fun storeTotp(origin: String, username: String, normalized: String, done: (Boolean) -> Unit) {
        if (vault == null && !vaultFile.exists()) {
            val key = VaultCrypto.newDeviceKey()
            try { deviceKey.write(key); pendingFile.writeText("") } catch (e: Exception) { return done(false) }
            vault = VaultPayload(); vaultKey = key
        }
        val v = vault ?: return done(false)
        val logins = v.entries.filter { it.origin == origin }
        val entry = logins.firstOrNull { it.username == username } ?: (if (username.isEmpty() && logins.size == 1) logins[0] else null)
            ?: VaultEntry.new(origin, username, "").also { v.entries.add(it) }
        entry.totp = normalized
        v.touch(entry)
        save { done(it == null) }
    }

    /** A login added or edited; [was] is its key before the edit, so a renamed login doesn't come back under its old name. */
    fun put(entry: VaultEntry, was: String? = null, done: (Exception?) -> Unit) {
        val v = vault ?: return done(IllegalStateException())
        if (v.entries.none { it === entry }) v.entries.add(entry)
        if (was != null && was != entry.key) v.forgetKey(was)
        v.touch(entry)
        save(done)
    }

    fun remove(entry: VaultEntry, done: (Exception?) -> Unit) {
        val v = vault ?: return done(IllegalStateException())
        v.forget(entry)
        save(done)
    }

    /** Logins from a CSV export; they go into the open vault, or wait on the device while it is locked. */
    fun import(logins: List<PasswordCsv.Login>, done: (Int) -> Unit) {
        if (logins.isEmpty()) return done(0)
        val v = vault
        if (v == null) {
            val list = loadDeviceLogins().toMutableList()
            for (l in logins) { list.removeAll { it.origin == l.origin && it.username == l.username }; list.add(VaultEntry.new(l.origin, l.username, l.password)) }
            saveDeviceLogins(list)
            return done(logins.size)
        }
        for (l in logins) (v.entries.firstOrNull { it.origin == l.origin && it.username == l.username } ?: VaultEntry.new(l.origin, l.username, "").also { v.entries.add(it) }).let { it.password = l.password; v.touch(it) }
        save { done(if (it == null) logins.size else 0) }
    }

    private fun loadDeviceLogins(): List<VaultEntry> = deviceLogins.read()?.let { s ->
        try { VaultPayload.parse(s).entries } catch (e: Exception) { null }
    } ?: emptyList()

    private fun saveDeviceLogins(list: List<VaultEntry>) {
        val p = VaultPayload(); p.entries.addAll(list)
        deviceLogins.write(p.toJson())
    }

    private fun mergeDeviceLogins() {
        val v = vault ?: return
        val waiting = loadDeviceLogins()
        if (waiting.isEmpty()) return
        for (l in waiting) (v.entries.firstOrNull { it.origin == l.origin && it.username == l.username } ?: VaultEntry.new(l.origin, l.username, "").also { v.entries.add(it) }).let { it.password = l.password; v.touch(it) }
        save { if (it == null) deviceLogins.delete() }
    }

    // ---- Vault sync ----

    private var remoteVersion = -1
    private val syncSoon = Runnable { sync() }
    private fun syncLater() { if (user != null && !passphrasePending) { main.removeCallbacks(syncSoon); main.postDelayed(syncSoon, 3000) } }

    private fun seenRevision(account: String) = prefs.getLong("seen:$account", 0)
    private fun rememberRevision(account: String, r: Long) { if (r > seenRevision(account)) prefs.edit().putLong("seen:$account", r).apply() }

    sealed class Outcome {
        class Done(val message: Int) : Outcome()
        /** The cloud copy uses a different master password: ask for it and call sync(password). */
        object NeedPassword : Outcome()
        class Failed(val error: Exception) : Outcome()
    }

    /**
     * Desktop's sync: the cloud copy is fetched, opened and merged into a copy of this device's vault (every
     * login either has, the newest save of each, minus deletions), and the result goes up if it differs from the
     * cloud's. Neither copy ever replaces the other. A conflict (another device pushed in between) merges again.
     */
    fun sync(password: String? = null, done: ((Outcome) -> Unit)? = null) = sync(password, 0, done)

    private fun sync(password: String?, retries: Int, done: ((Outcome) -> Unit)?) {
        val u = user
        if (u == null || syncing) return done?.invoke(Outcome.Failed(IllegalStateException(context.getString(app.mica.R.string.sign_in_first)))) ?: Unit
        if (vault == null && vaultFile.exists() && password == null) {
            // Locked: this device's key may still open it.
            if (deviceKey.exists) return openSilently { if (it) sync(null, retries, done) else done?.invoke(Outcome.NeedPassword) }
            return done?.invoke(Outcome.NeedPassword) ?: Unit
        }
        syncing = true; changed()
        val local = vault?.let { VaultPayload.parse(it.toJson()) }
        val localKey = vaultKey
        val pending = passphrasePending
        val dirty = prefs.getBoolean("dirty", false)
        val seen = seenRevision(u.id)
        bg({ syncOnWorker(u, local, localKey, pending, dirty, seen, password) }) { r, e ->
            syncing = false
            if (r == null) {
                if (e is ApiException && e.status == 409 && retries < 2) { remoteVersion = -1; changed(); return@bg sync(password, retries + 1, done) }
                lastMessage = null; changed()
                return@bg done?.invoke(if (e is VaultException && e.message?.startsWith("Wrong") == true) Outcome.NeedPassword else Outcome.Failed(e ?: Exception())) ?: Unit
            }
            if (r.needPassword) { changed(); return@bg done?.invoke(Outcome.NeedPassword) ?: Unit }
            remoteVersion = r.version
            var savedMeanwhile = false
            if (r.payload != null && r.key != null) {
                if (pending) pendingFile.delete()
                // Logins saved here while the sync ran fold into the result rather than being lost.
                val live = vault
                val merged = VaultPayload.parse(r.payload.toJson())
                if (live != null) { merged.merge(live); savedMeanwhile = !merged.sameLogins(r.payload) }
                vault = merged; vaultKey = r.key
                try { deviceKey.write(r.key) } catch (x: Exception) {}
            }
            r.revision?.let { rememberRevision(u.id, it) }
            if (r.pushed) prefs.edit().putBoolean("dirty", false).apply()
            if (savedMeanwhile) save()
            prefs.edit().putLong("lastSync", System.currentTimeMillis()).apply()
            lastMessage = context.getString(r.message)
            changed()
            done?.invoke(Outcome.Done(r.message))
        }
    }

    private class Synced(val version: Int, val payload: VaultPayload?, val key: String?, val revision: Long?, val pushed: Boolean, val message: Int, val needPassword: Boolean = false)

    private fun syncOnWorker(u: User, local: VaultPayload?, localKey: String?, pending: Boolean, dirty: Boolean, seen: Long, password: String?): Synced {
        val me = authed("GET", "/v1/account")?.get("user") as? Map<*, *>
        if (me?.get("id") != u.id) throw IllegalStateException("Account verification failed. Sign out and sign in again.")
        val remote = authed("GET", "/v1/vault") ?: throw IllegalStateException()
        val version = (remote["version"] as? Number)?.toInt() ?: throw IllegalStateException("Invalid remote vault.")
        val env = (remote["envelope"] as? Map<*, *>)?.let { Envelope.from(it) }

        if (env == null) {
            // Nothing in the cloud yet: this device's vault goes up, once it has a real master password.
            if (local == null || localKey == null || pending) return Synced(version, null, null, null, false, app.mica.R.string.sync_nothing_yet)
            return push(u, local, localKey, version, seen, null)
        }

        // Open the cloud copy: this device's key first, then the password typed for it.
        var key: String? = null
        var pulled: VaultPayload? = null
        if (localKey != null && !pending) try { key = VaultCrypto.keyFor(env, localKey); pulled = VaultCrypto.decrypt(env, key) } catch (e: VaultException) {}
        if (pulled == null && password != null) { key = VaultCrypto.keyFor(env, password); pulled = VaultCrypto.decrypt(env, key) }
        if (pulled == null || key == null) return Synced(version, null, null, null, false, 0, needPassword = true)
        if (!pulled.accountId.isNullOrEmpty() && pulled.accountId != u.id) throw IllegalStateException("This vault belongs to another account.")

        // This device's copy is the base (it alone has what was saved here); a vault still waiting for its master
        // password takes the cloud's instead, with its key, and adds its logins to it.
        val mine = local != null && localKey != null && !pending && key == localKey
        val base = VaultPayload.parse((if (mine) local!! else pulled).toJson())
        base.merge(if (mine) pulled else local ?: pulled)
        if (local == null || localKey != key || pending || !base.sameLogins(local)) backupLocal()
        if (base.sameLogins(pulled)) {
            // Nothing the cloud lacks: keep its revision, write the merged copy here only if it changed.
            base.revision = maxOf(pulled.revision, local?.revision ?: 0)
            if (local == null || localKey != key || pending || !base.sameLogins(local)) writeEnvelope(VaultCrypto.encrypt(base, key))
            return Synced(version, base, key, pulled.revision, false, if (local != null && base.sameLogins(local)) app.mica.R.string.sync_up_to_date else app.mica.R.string.sync_downloaded)
        }
        base.revision = maxOf(local?.revision ?: 0, pulled.revision, seen)
        return push(u, base, key, version, seen, base)
    }

    private fun push(u: User, payload: VaultPayload, key: String, version: Int, seen: Long, adopt: VaultPayload?): Synced {
        if (!payload.accountId.isNullOrEmpty() && payload.accountId != u.id) throw IllegalStateException("Local vault belongs to another account. Sign in to that account.")
        payload.accountId = u.id
        payload.revision = maxOf(payload.revision, seen) + 1
        val env = VaultCrypto.encrypt(payload, key)
        writeEnvelope(env)
        val r = authed("PUT", "/v1/vault", mapOf("version" to version, "envelope" to env.toMap())) ?: throw IllegalStateException()
        val now = (r["version"] as? Number)?.toInt() ?: -1
        if (now <= version) throw IllegalStateException("Push result could not be verified. Sync again.")
        return Synced(now, adopt ?: payload, key, payload.revision, true, app.mica.R.string.sync_uploaded)
    }

    private fun backupLocal() {
        if (!vaultFile.exists()) return
        try { vaultFile.copyTo(File(dir, "vault-before-pull.enc.json"), overwrite = true) } catch (e: Exception) {}
    }

    // ---- Preferences ----

    /** What desktop syncs: dark, ambientYoutube, strictTracking. Android only uses dark and keeps the rest as received. */
    var applyDark: ((Boolean) -> Unit)? = null
    var currentDark: (() -> Boolean)? = null

    private fun snapshot(): Map<String, Any?> {
        val others = prefs.getString("settingsOthers", null)?.let { try { Json.parse(it) as? Map<*, *> } catch (e: Exception) { null } } ?: emptyMap<String, Any?>()
        return linkedMapOf("dark" to (currentDark?.invoke() ?: false), "ambientYoutube" to (others["ambientYoutube"] as? Boolean ?: false), "strictTracking" to (others["strictTracking"] as? Boolean ?: false))
    }

    private var settingsUpdated: Long
        get() = prefs.getLong("settingsUpdated", 0)
        set(v) { prefs.edit().putLong("settingsUpdated", v).apply() }

    /** The user changed the theme here: newer than the cloud copy, so it goes up. */
    fun noteSettingsChange() {
        settingsUpdated = System.currentTimeMillis()
        prefs.edit().putBoolean("settingsPending", true).apply()
        pushSettings()
    }

    private fun pushSettings() {
        if (user == null || !prefs.getBoolean("settingsPending", false)) return
        val body = mapOf("settings" to snapshot(), "updatedAt" to settingsUpdated)
        bg({ authed("PUT", "/v1/settings", body, 10000) }) { r, _ -> if (r != null) { prefs.edit().putBoolean("settingsPending", false).apply(); receive(r) } }
    }

    fun pullSettings() {
        if (user == null) return
        bg({ authed("GET", "/v1/settings", null, 10000) }) { r, _ ->
            if (r == null) return@bg
            val remoteAt = (r["updatedAt"] as? Number)?.toLong() ?: 0
            if (r["settings"] !is Map<*, *> || remoteAt < settingsUpdated) {
                if (settingsUpdated == 0L) settingsUpdated = System.currentTimeMillis()
                prefs.edit().putBoolean("settingsPending", true).apply(); pushSettings()
            } else receive(r)
        }
    }

    private fun receive(r: Map<*, *>) {
        val s = r["settings"] as? Map<*, *> ?: return
        val at = (r["updatedAt"] as? Number)?.toLong() ?: return
        if (at <= settingsUpdated && settingsUpdated != 0L) return
        settingsUpdated = at
        prefs.edit().putString("settingsOthers", JsonOut.write(s.filterKeys { it != "dark" })).apply()
        (s["dark"] as? Boolean)?.let { d -> if (currentDark?.invoke() != d) applyDark?.invoke(d) }
    }

    // ---- Send tab ----

    val deviceId: String get() = prefs.getString("deviceId", null) ?: VaultCrypto.base64Url(VaultCrypto.randomBytes(18)).also { prefs.edit().putString("deviceId", it).apply() }
    val deviceName: String get() = (try { Settings.Global.getString(context.contentResolver, "device_name") } catch (e: Exception) { null })?.takeIf { it.isNotBlank() } ?: Build.MODEL

    class Device(val id: String, val name: String, val seen: Long)
    class SentTab(val id: String, val from: String, val url: String, val title: String)

    /** Worker: names this device to the account and returns the account's devices. */
    fun devices(): List<Device>? {
        val r = authed("POST", "/v1/devices", mapOf("id" to deviceId, "name" to deviceName), 10000) ?: return null
        return (r["devices"] as? List<*>)?.mapNotNull { d -> (d as? Map<*, *>)?.let { Device(it["id"] as? String ?: return@mapNotNull null, it["name"] as? String ?: "", (it["seen"] as? Number)?.toLong() ?: 0) } } ?: emptyList()
    }

    fun sendTab(to: Device, url: String, title: String, done: (Exception?) -> Unit) {
        val body = mapOf("to" to to.id, "from" to deviceName, "url" to url, "title" to title.take(300))
        bg({ authed("POST", "/v1/tabs", body, 10000)?.takeIf { it["id"] is String } ?: throw IllegalStateException("No id for the sent tab.") }) { _, e -> done(e) }
    }

    var onTabsArrived: ((List<SentTab>) -> Unit)? = null
    private var checking = false
    private var lastCheck = 0L

    /** Picks up tabs sent from other devices: on launch, on return to the app (at most every [gapMs]) and every 2 minutes while open. */
    fun checkSentTabs(force: Boolean = true, gapMs: Long = 110_000) {
        if (user == null || checking || (!force && System.currentTimeMillis() - lastCheck < gapMs)) return
        checking = true; lastCheck = System.currentTimeMillis()
        bg({
            if (devices() == null) return@bg emptyList<SentTab>()
            val r = authed("GET", "/v1/tabs?device=" + java.net.URLEncoder.encode(deviceId, "UTF-8"), null, 10000) ?: return@bg emptyList<SentTab>()
            val tabs = (r["tabs"] as? List<*>)?.mapNotNull { t -> (t as? Map<*, *>)?.let { SentTab(it["id"] as? String ?: return@mapNotNull null, it["from"] as? String ?: "", it["url"] as? String ?: "", it["title"] as? String ?: "") } } ?: emptyList()
            for (t in tabs) try { authed("DELETE", "/v1/tabs/" + java.net.URLEncoder.encode(t.id, "UTF-8"), null, 10000) } catch (e: Exception) {}
            tabs
        }) { tabs, _ -> checking = false; if (!tabs.isNullOrEmpty()) onTabsArrived?.invoke(tabs) }
    }

    /** Back in the app: tabs waiting, preferences, and the vault if it's been a while. */
    fun onForeground() {
        if (user == null) return
        // Coming back is when a tab just sent from the computer is wanted.
        checkSentTabs(force = false, gapMs = 20_000)
        pushSettings()
        if (vault != null && !passphrasePending && System.currentTimeMillis() - prefs.getLong("lastSync", 0) > 15 * 60_000) sync()
    }
}
