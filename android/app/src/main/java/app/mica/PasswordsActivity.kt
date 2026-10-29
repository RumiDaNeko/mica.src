package app.mica

import android.content.ClipData
import android.content.ClipDescription
import android.content.ClipboardManager
import android.content.Intent
import android.os.Build
import android.os.Bundle
import android.os.PersistableBundle
import android.text.InputType
import android.view.View
import android.view.WindowManager
import android.widget.LinearLayout
import android.widget.ScrollView
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.view.ViewCompat
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import app.mica.account.Account
import app.mica.account.PasswordCsv
import app.mica.account.Totp
import app.mica.account.VaultCrypto
import app.mica.account.VaultEntry

/**
 * Passwords & sync (desktop's vault window): the Mica account, the master password, saved logins with
 * their 2FA codes, import from another browser. Screenshots are blocked while it's open.
 */
class PasswordsActivity : AppCompatActivity() {
    private val account get() = (application as MicaApp).account
    private lateinit var list: LinearLayout
    private val refresh: () -> Unit = { render() }
    private var filter = ""
    private var ticking = false
    private val tick = object : Runnable { override fun run() { if (ticking) { renderCodes(); list.postDelayed(this, 1000) } } }
    private val codeViews = ArrayList<Pair<android.widget.TextView, String>>()

    private val importCsv = registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri == null) return@registerForActivityResult
        val text = try { contentResolver.openInputStream(uri)?.use { it.readBytes().toString(Charsets.UTF_8) } } catch (e: Exception) { null }
        val logins = text?.let { PasswordCsv.parse(it) } ?: emptyList()
        if (logins.isEmpty()) { toast(getString(R.string.import_none)); return@registerForActivityResult }
        account.import(logins) { n -> toast(resources.getQuantityString(R.plurals.imported_logins, n, n)) }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        WindowCompat.setDecorFitsSystemWindows(window, false)
        list = column().apply { pad(20, 12) }
        val scroll = ScrollView(this).apply { addView(list); setBackgroundColor(col(R.color.mica_canvas)) }
        setContentView(scroll)
        ViewCompat.setOnApplyWindowInsetsListener(scroll) { v, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars() or WindowInsetsCompat.Type.ime())
            v.setPadding(bars.left, bars.top, bars.right, bars.bottom); insets
        }
    }

    override fun onStart() { super.onStart(); account.listen(refresh); render() }
    override fun onStop() { super.onStop(); account.unlisten(refresh); ticking = false }

    private fun toast(s: String) = notice(s)

    private fun render() {
        list.removeAllViews()
        codeViews.clear()
        list.addView(text(getString(R.string.passwords_sync), 26f, col(R.color.mica_accent), bold = true).apply { pad(4, 16) })

        // Account
        section(R.string.account)
        val u = account.user
        if (u == null) {
            note(getString(R.string.account_explain))
            if (account.signIn != null) button(getString(R.string.cancel_sign_in)) { account.cancelSignIn() }
            else button(getString(R.string.sign_in_discord), primary = true) { signIn() }
        } else {
            item(getString(R.string.signed_in_as, u.username), account.lastMessage ?: if (account.syncing) getString(R.string.syncing) else getString(R.string.sync_explain)) {}
            val r = row()
            r.addView(button(getString(if (account.syncing) R.string.syncing else R.string.sync_now), primary = true, add = false) { sync(null) }, lp(0, WRAP, 1f))
            r.addView(button(getString(R.string.sign_out), add = false) { account.signOut() }, lp(0, WRAP, 1f).apply { marginStart = dp(8) })
            list.addView(r, lp(MATCH, WRAP).apply { bottomMargin = dp(8) })
        }

        // Vault
        section(R.string.password_vault)
        val vault = account.vault
        if (vault == null || account.passphrasePending) {
            note(getString(when {
                account.passphrasePending -> R.string.choose_master_pending
                account.hasVault -> R.string.vault_locked
                else -> R.string.vault_new
            }))
            val (box, field) = passwordField(getString(R.string.master_password))
            list.addView(box, lp(MATCH, WRAP).apply { bottomMargin = dp(8) })
            button(getString(if (account.hasVault && !account.passphrasePending) R.string.unlock else R.string.create_vault), primary = true) {
                val pw = field.text.toString(); field.setText("")
                account.unlock(pw) { e -> if (e != null) toast(message(e)) }
            }
            if (u != null && !account.hasVault) note(getString(R.string.vault_from_cloud))
            if (account.passphrasePending && vault != null) entries(vault.entries)
        } else {
            val r = row()
            r.addView(button(getString(R.string.add_login), primary = true, add = false) { edit(null) }, lp(0, WRAP, 1f))
            r.addView(button(getString(R.string.import_csv), add = false) { importCsv.launch(arrayOf("text/csv", "text/comma-separated-values", "text/plain", "application/octet-stream")) }, lp(0, WRAP, 1f).apply { marginStart = dp(8) })
            r.addView(button(getString(R.string.lock), add = false) { account.lock() }, lp(0, WRAP, 1f).apply { marginStart = dp(8) })
            list.addView(r, lp(MATCH, WRAP).apply { bottomMargin = dp(8) })
            if (vault.entries.size > 6) {
                val search = field(getString(R.string.search_logins), filter)
                search.addTextChangedListener(object : android.text.TextWatcher {
                    override fun beforeTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
                    override fun onTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
                    override fun afterTextChanged(s: android.text.Editable?) { filter = s.toString(); entryBox?.let { fillEntries(it, vault.entries) } }
                })
                list.addView(search, lp(MATCH, WRAP).apply { bottomMargin = dp(8) })
            }
            entries(vault.entries)
            if (vault.entries.isEmpty()) note(getString(R.string.no_logins))
        }
        val prefs = (application as MicaApp).prefs
        val s = androidx.appcompat.widget.SwitchCompat(this).apply {
            isChecked = prefs.micaPasswords
            setOnCheckedChangeListener { _, on -> prefs.micaPasswords = on; (application as MicaApp).browser.applyAutofillEverywhere() }
        }
        val label = column(text(getString(R.string.mica_passwords), 16f), text(getString(R.string.mica_passwords_detail), 13f, col(R.color.mica_muted)).apply { setPadding(0, dp(4), 0, 0) })
        val r = row(label, s).apply { pad(14, 12); background = rounded(col(R.color.mica_card), 16, col(R.color.mica_line)); setOnClickListener { s.toggle() } }
        label.layoutParams = lp(0, WRAP, 1f)
        list.addView(r, lp(MATCH, WRAP).apply { topMargin = dp(8); bottomMargin = dp(8) })
        note(getString(R.string.passkeys_note))
        ticking = codeViews.isNotEmpty()
        list.removeCallbacks(tick)
        if (ticking) list.post(tick)
    }

    private var entryBox: LinearLayout? = null

    private fun entries(all: List<VaultEntry>) {
        val box = column()
        entryBox = box
        list.addView(box, lp(MATCH, WRAP))
        fillEntries(box, all)
    }

    private fun fillEntries(box: LinearLayout, all: List<VaultEntry>) {
        box.removeAllViews()
        val q = filter.trim().lowercase()
        for (e in all.sortedWith(compareBy({ it.origin.removePrefix("https://") }, { it.username.lowercase() }))) {
            if (q.isNotEmpty() && !e.origin.contains(q) && !e.username.lowercase().contains(q)) continue
            val title = text(e.origin.removePrefix("https://"), 16f)
            val sub = text(e.username.ifEmpty { getString(R.string.no_username) }, 13f, col(R.color.mica_muted))
            val c = column(title, sub).apply { pad(14, 12); background = pressable(col(R.color.mica_card), 16, col(R.color.mica_line)); setOnClickListener { details(e) } }
            (sub.layoutParams as LinearLayout.LayoutParams).topMargin = dp(4)
            if (!e.totp.isNullOrEmpty()) {
                val code = text("", 13f, col(R.color.mica_accent), bold = true)
                c.addView(code, lp(WRAP, WRAP).apply { topMargin = dp(4) })
                codeViews.add(code to e.totp!!)
            }
            box.addView(c, lp(MATCH, WRAP).apply { bottomMargin = dp(8) })
        }
        renderCodes()
    }

    private fun renderCodes() {
        for ((v, key) in codeViews) {
            val code = Totp.code(key) ?: continue
            v.text = getString(R.string.totp_code_left, code.chunked(3).joinToString(" "), Totp.secondsLeft(key))
        }
    }

    private fun details(e: VaultEntry) {
        val items = ArrayList<Pair<String, () -> Unit>>()
        if (e.username.isNotEmpty()) items.add(getString(R.string.copy_username) to { copy(e.username, false) })
        if (e.password.isNotEmpty()) items.add(getString(R.string.copy_password) to { copy(e.password, true) })
        Totp.code(e.totp)?.let { items.add(getString(R.string.copy_code) to { copy(Totp.code(e.totp) ?: it, true) }) }
        items.add(getString(R.string.open_site) to { startActivity(Intent(Intent.ACTION_VIEW, android.net.Uri.parse(e.origin), this, MainActivity::class.java)) })
        items.add(getString(R.string.edit) to { edit(e) })
        items.add(getString(R.string.delete) to {
            Sheet.show(this, getString(R.string.delete_login_q), e.origin.removePrefix("https://") + "\n" + e.username.ifEmpty { getString(R.string.no_username) }, null, listOf(
                CardAction(getString(R.string.cancel)) {},
                CardAction(getString(R.string.delete), true) { account.remove(e) { render() } }))
        })
        var sheet: Sheet? = null
        sheet = Sheet.show(this, e.origin.removePrefix("https://"), e.username.ifEmpty { null }, choices(items.map { it.first to null }) { i -> sheet?.dismiss(); items[i].second() })
    }

    /** Passwords and codes stay out of clipboard previews and are cleared after 30 seconds, as on desktop. */
    private fun copy(value: String, secret: Boolean) {
        val cm = getSystemService(ClipboardManager::class.java)
        val clip = ClipData.newPlainText(if (secret) "password" else "username", value)
        if (secret && Build.VERSION.SDK_INT >= 24) clip.description.extras = PersistableBundle().apply { putBoolean(if (Build.VERSION.SDK_INT >= 33) ClipDescription.EXTRA_IS_SENSITIVE else "android.content.extra.IS_SENSITIVE", true) }
        cm.setPrimaryClip(clip)
        if (secret) {
            val app = application
            list.rootView.postDelayed({
                val cur = app.getSystemService(ClipboardManager::class.java)
                try { if (cur.primaryClip?.getItemAt(0)?.text == value) { if (Build.VERSION.SDK_INT >= 28) cur.clearPrimaryClip() else cur.setPrimaryClip(ClipData.newPlainText("", "")) } } catch (x: Exception) {}
            }, 30_000)
        }
        toast(getString(if (secret) R.string.copied_secret else R.string.copied))
    }

    private fun edit(e: VaultEntry?) {
        val site = field(getString(R.string.website), e?.origin ?: "https://", InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI)
        val user = field(getString(R.string.username), e?.username ?: "", InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS)
        val (passBox, pass) = passwordField(getString(R.string.password), e?.password ?: "")
        val totp = field(getString(R.string.totp_key), e?.totp ?: "")
        val problem = text("", 13f, 0xFFB3261E.toInt()).apply { visibility = View.GONE }
        var sheet: Sheet? = null
        sheet = Sheet.show(this, getString(if (e == null) R.string.add_login else R.string.edit), null, form(site, user, passBox, totp, problem), listOf(
            CardAction(getString(R.string.cancel)) { sheet?.dismiss() },
            CardAction(getString(R.string.save), true) {
                try {
                    val origin = VaultCrypto.origin(site.text.toString())
                    val key = totp.text.toString().takeIf { it.isNotBlank() }?.let { Totp.normalize(Totp.parse(it)) }
                    val was = e?.key
                    val entry = e ?: VaultEntry.new(origin, "", "")
                    entry.origin = origin; entry.username = user.text.toString(); entry.password = pass.text.toString(); entry.totp = key
                    account.put(entry, was) { x -> toast(if (x != null) message(x) else getString(R.string.login_saved)) }
                    sheet?.dismiss()
                } catch (x: IllegalArgumentException) { problem.text = message(x); problem.visibility = View.VISIBLE }
            }), keepOpen = true)
    }

    private fun signIn() {
        account.beginSignIn { s, e ->
            if (s == null) { toast(message(e)); return@beginSignIn }
            // Discord opens in a Mica tab; the code waits in a card there to be typed (or pasted) on Discord's page.
            startActivity(Intent(this, MainActivity::class.java).setAction(MainActivity.ACTION_SIGN_IN).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP))
        }
    }

    private fun sync(password: String?) {
        account.sync(password) { r ->
            when (r) {
                is Account.Outcome.Done -> toast(getString(r.message))
                is Account.Outcome.NeedPassword -> askCloudPassword()
                is Account.Outcome.Failed -> toast(message(r.error))
            }
        }
    }

    private fun askCloudPassword() {
        val (box, field) = passwordField(getString(R.string.master_password))
        Sheet.show(this, getString(R.string.unlock_cloud), getString(R.string.unlock_cloud_detail), box, listOf(
            CardAction(getString(R.string.cancel)) {},
            CardAction(getString(R.string.unlock), true) { val pw = field.text.toString(); if (pw.isNotEmpty()) sync(pw) }))
        field.requestFocus()
    }

    private fun message(e: Exception?): String = when (e) {
        is app.mica.account.VaultException -> if (e.message?.startsWith("Wrong") == true) getString(R.string.wrong_master) else e.message ?: ""
        is app.mica.account.ApiException -> if (e.status == 401) getString(R.string.signed_out_again) else getString(R.string.sync_unreachable)
        is java.io.IOException -> getString(R.string.sync_unreachable)
        null -> getString(R.string.sync_unreachable)
        else -> e.message ?: e.toString()
    }

    // ---- small building blocks (as in Settings) ----

    private fun section(title: Int) {
        list.addView(text(getString(title).uppercase(), 11f, col(R.color.mica_faint)).apply { letterSpacing = 0.08f; pad(4, 0); layoutParams = lp(MATCH, WRAP).apply { topMargin = dp(22); bottomMargin = dp(6) } })
    }

    private fun note(s: String) { list.addView(text(s, 13f, col(R.color.mica_muted)).apply { pad(4, 4); setLineSpacing(0f, 1.15f) }, lp(MATCH, WRAP).apply { bottomMargin = dp(8) }) }

    private fun item(title: String, value: String, onClick: () -> Unit) {
        val t = text(title, 16f)
        val v = text(value, 13f, col(R.color.mica_muted)).apply { maxLines = 3 }
        val c = column(t, v).apply { pad(14, 12); background = pressable(col(R.color.mica_card), 16, col(R.color.mica_line)); setOnClickListener { onClick() } }
        (v.layoutParams as LinearLayout.LayoutParams).topMargin = dp(4)
        list.addView(c, lp(MATCH, WRAP).apply { bottomMargin = dp(8) })
    }

    private fun button(label: String, primary: Boolean = false, add: Boolean = true, run: () -> Unit): View {
        val b = text(label, 15f, if (primary) col(R.color.mica_canvas) else col(R.color.mica_accent), bold = true).apply {
            gravity = android.view.Gravity.CENTER
            pad(14, 12)
            background = if (primary) pressable(col(R.color.mica_accent), 16) else pressable(col(R.color.mica_card), 16, col(R.color.mica_line))
            setOnClickListener { run() }
        }
        if (add) list.addView(b, lp(MATCH, WRAP).apply { bottomMargin = dp(8) })
        return b
    }
}
