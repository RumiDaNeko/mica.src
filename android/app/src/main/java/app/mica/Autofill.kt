package app.mica

import android.text.InputType
import app.mica.account.Account
import app.mica.account.JsonOut
import app.mica.account.Totp
import app.mica.account.VaultCrypto
import app.mica.account.VaultEntry
import app.mica.blocker.Json

/**
 * Saving and filling passwords and 2FA codes on pages, as desktop does (BrowserAutofill.cs, BrowserTotp.cs).
 * The page script only reports what it sees; filling is chosen in Mica's own bubble, which a page can't
 * press, so a page can't ask for a password and read it back. Pages only ever get a 2FA code, never the key.
 */
class Autofill(private val activity: MainActivity, private val bubble: Bubble) {
    private val account get() = (activity.application as MicaApp).account
    private val prefs = activity.getSharedPreferences("account", android.content.Context.MODE_PRIVATE)
    private val recentUser = HashMap<String, String>()
    private val offeredKeys = HashSet<String>()

    fun onMessage(tab: Tab, origin: String, json: String) {
        val site = VaultCrypto.originOrNull(origin) ?: return
        val m = try { Json.parse(json) as? Map<*, *> } catch (e: Exception) { null } ?: return
        if (m["mica"] == "passkeyFailed") return passkeyFailed(tab, site)
        if (!(activity.application as MicaApp).prefs.micaPasswords) return
        when (m["mica"]) {
            "loginForm" -> offerFill(tab, site)
            "credential" -> {
                val user = (m["username"] as? String ?: "").take(4096)
                val pass = m["password"] as? String ?: return
                if (pass.isEmpty() || pass.length > 65536) return
                if (user.isNotEmpty()) recentUser[site] = user
                offerSave(site, user, pass)
            }
            "otpField" -> fillCode(tab, site)
            "totpKey" -> {
                val uri = m["uri"] as? String ?: ""
                val secret = m["secret"] as? String ?: ""
                if (uri.length <= 1024 && secret.length <= 200) offerTotp(tab, site, uri.ifEmpty { secret }, m["user"] as? String)
            }
        }
    }

    private fun alive(tab: Tab, site: String) = activity.browser.active === tab && tab.view != null && VaultCrypto.originOrNull(tab.url) == site

    private fun host(site: String) = site.removePrefix("https://")

    private fun offerFill(tab: Tab, site: String) {
        account.openSilently {
            val saved = account.savedLogins(site)
            if (saved.isEmpty() || !alive(tab, site)) return@openSilently
            val actions = saved.take(2).mapIndexed { i, e -> CardAction(e.username.ifEmpty { activity.getString(R.string.no_username) }, i == 0) { fill(tab, site, e) } }
            bubble.show(activity.getString(if (saved.size == 1) R.string.sign_in_saved_one else R.string.sign_in_saved_many), host(site), actions)
        }
    }

    private fun fill(tab: Tab, site: String, e: VaultEntry) {
        if (!alive(tab, site)) return
        if (e.username.isNotEmpty()) recentUser[site] = e.username
        tab.view?.evaluateJavascript(fillScript(site, e.username, e.password)) { r ->
            if (r == "\"filled\"") activity.announce(activity.getString(R.string.filled_password))
        }
    }

    private var toldPasskeys = false

    /** Once per session: the page's passkey request was refused by Android, not by you. */
    private fun passkeyFailed(tab: Tab, site: String) {
        if (toldPasskeys || !alive(tab, site)) return
        toldPasskeys = true
        bubble.show(activity.getString(R.string.passkeys_unavailable), activity.getString(R.string.passkeys_unavailable_detail), listOf(CardAction(activity.getString(R.string.ok), true) {}), seconds = 15)
    }

    private fun neverSave(): Set<String> = prefs.getStringSet("never", emptySet())!!

    private fun offerSave(site: String, username: String, password: String) {
        if (site in neverSave()) return
        account.openSilently {
            val same = account.savedLogins(site).firstOrNull { it.username == username }
            if (same != null && same.password == password) return@openSilently
            val user = activity.field(activity.getString(R.string.username), username, InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS)
            val (passBox, pass) = activity.passwordField(activity.getString(R.string.password), password)
            // Opens out of the island and stays until you choose; the details can be corrected first.
            bubble.show(activity.getString(if (same != null) R.string.update_password_q else R.string.save_password_q), host(site), listOf(
                CardAction(activity.getString(R.string.never)) {
                    prefs.edit().putStringSet("never", neverSave() + site).apply()
                    activity.announce(activity.getString(R.string.never_save_done))
                },
                CardAction(activity.getString(R.string.not_now)) {},
                CardAction(activity.getString(if (same != null) R.string.update else R.string.save), true) {
                    val value = pass.text.toString()
                    if (value.isNotEmpty()) account.storeLogin(site, user.text.toString(), value) { ok -> activity.announce(activity.getString(if (ok) R.string.password_saved else R.string.password_not_saved)) }
                }), seconds = 0, extra = activity.form(user, passBox), stays = true)
        }
    }

    private fun fillCode(tab: Tab, site: String) {
        account.openSilently {
            val keys = account.savedTotps(site)
            if (keys.isEmpty() || !alive(tab, site)) return@openSilently
            val entry = keys.firstOrNull { it.username == recentUser[site] } ?: keys[0]
            val left = Totp.secondsLeft(entry.totp)
            // In its last seconds the code is about to change; the next one is a moment away.
            activity.webHostPostDelayed(if (left <= 2) left * 1000L + 300 else 0L) {
                val code = Totp.code(entry.totp) ?: return@webHostPostDelayed
                if (!alive(tab, site)) return@webHostPostDelayed
                tab.view?.evaluateJavascript(codeScript(site, code)) { r -> if (r == "\"filled\"") activity.announce(activity.getString(R.string.filled_code)) }
            }
        }
    }

    private fun offerTotp(tab: Tab, site: String, text: String, userHint: String?) {
        val key = Totp.parseOrNull(text) ?: return
        val normalized = Totp.normalize(key)
        if (!offeredKeys.add("$site $normalized")) return
        account.openSilently {
            if (account.savedTotps(site).any { Totp.parseOrNull(it.totp)?.secret?.contentEquals(key.secret) == true }) return@openSilently
            val guess = userHint?.takeIf { it.isNotEmpty() } ?: key.account?.takeIf { it.isNotEmpty() } ?: recentUser[site] ?: ""
            val field = activity.field(activity.getString(R.string.account_name), guess)
            bubble.show(activity.getString(R.string.save_totp_q), activity.getString(R.string.save_totp_detail, host(site)), listOf(
                CardAction(activity.getString(R.string.not_now)) {},
                CardAction(activity.getString(R.string.save), true) {
                    account.storeTotp(site, field.text.toString().trim(), normalized) { ok ->
                        activity.announce(activity.getString(if (ok) R.string.totp_saved else R.string.password_not_saved))
                        if (ok) fillCode(tab, site)
                    }
                }), seconds = 0, extra = field, stays = true)
        }
    }

    companion object {
        private fun js(s: String) = JsonOut.write(s)

        /** Desktop's FillScript: the password field, the username field before it, React-safe value setting. */
        fun fillScript(origin: String, username: String, password: String) =
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

        fun codeScript(origin: String, code: String) =
            "(()=>{if(window.top!==window||location.origin!==" + js(origin) + "||!window.__micaCodeTarget)return 'origin';const t=window.__micaCodeTarget();if(!t)return 'none';" +
            "const code=" + js(code) + ";const set=(e,v)=>{e.focus();Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(e,v);e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));};" +
            "if(t.length===1)set(t[0],code);else t.forEach((e,i)=>set(e,code[i]||''));return 'filled';})()"
    }
}
