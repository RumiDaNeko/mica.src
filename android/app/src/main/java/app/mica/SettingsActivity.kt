package app.mica

import android.net.Uri
import android.os.Bundle
import android.widget.LinearLayout
import android.widget.ScrollView
import androidx.appcompat.app.AppCompatActivity
import androidx.appcompat.app.AppCompatDelegate
import androidx.appcompat.widget.SwitchCompat
import androidx.core.os.LocaleListCompat
import androidx.core.view.ViewCompat
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat

/** A short settings page: theme, language, ad blocking, desktop site. */
class SettingsActivity : AppCompatActivity() {
    private val prefs get() = (application as MicaApp).prefs
    private lateinit var list: LinearLayout

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        WindowCompat.setDecorFitsSystemWindows(window, false)
        list = column().apply { pad(20, 12) }
        val scroll = ScrollView(this).apply { addView(list); setBackgroundColor(col(R.color.mica_canvas)) }
        setContentView(scroll)
        ViewCompat.setOnApplyWindowInsetsListener(scroll) { v, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            v.setPadding(bars.left, bars.top, bars.right, bars.bottom); insets
        }
        render()
    }

    override fun onResume() { super.onResume(); render() }

    private fun render() {
        list.removeAllViews()
        list.addView(text(getString(R.string.settings), 26f, col(R.color.mica_accent), bold = true).apply { pad(4, 16) })

        val account = (application as MicaApp).account
        item(getString(R.string.passwords_sync), account.user?.let { getString(R.string.signed_in_as, it.username) } ?: getString(R.string.not_signed_in)) {
            startActivity(android.content.Intent(this, PasswordsActivity::class.java))
        }

        val nightLabels = arrayOf(getString(R.string.system), getString(R.string.light_theme), getString(R.string.dark_theme))
        val nightModes = intArrayOf(AppCompatDelegate.MODE_NIGHT_FOLLOW_SYSTEM, AppCompatDelegate.MODE_NIGHT_NO, AppCompatDelegate.MODE_NIGHT_YES)
        item(getString(R.string.theme), nightLabels[nightModes.indexOf(prefs.nightMode).coerceAtLeast(0)]) {
            pick(getString(R.string.theme), nightLabels, nightModes.indexOf(prefs.nightMode)) { i ->
                prefs.nightMode = nightModes[i]
                (application as MicaApp).account.noteSettingsChange()
                AppCompatDelegate.setDefaultNightMode(nightModes[i])
            }
        }

        val langTags = arrayOf("", "en", "vi")
        val langLabels = arrayOf(getString(R.string.system), getString(R.string.english), getString(R.string.vietnamese))
        val current = AppCompatDelegate.getApplicationLocales().toLanguageTags().substringBefore('-')
        item(getString(R.string.language), langLabels[langTags.indexOf(current).coerceAtLeast(0)]) {
            pick(getString(R.string.language), langLabels, langTags.indexOf(current).coerceAtLeast(0)) { i ->
                AppCompatDelegate.setApplicationLocales(LocaleListCompat.forLanguageTags(langTags[i]))
            }
        }

        section(R.string.ad_blocking)
        toggle(getString(R.string.block_ads), prefs.blockerOn) { prefs.blockerOn = it; Blocker.enabled = it }
        val engine = Blocker.engine
        list.addView(text(if (engine == null) getString(R.string.rules_loading) else getString(R.string.rules_loaded, engine.size, Blocker.lists), 13f, col(R.color.mica_muted)).apply { pad(4, 4) })
        val allowed = prefs.allowedSites.sorted()
        item(getString(R.string.allowed_sites), if (allowed.isEmpty()) getString(R.string.no_allowed_sites) else allowed.joinToString(", ")) {
            if (allowed.isEmpty()) return@item
            // Tap a site to block its ads again.
            var sheet: Sheet? = null
            sheet = Sheet.show(this, getString(R.string.allowed_sites), getString(R.string.allowed_sites_tap), choices(allowed.map { it to null }) { i ->
                sheet?.dismiss()
                val keep = allowed.filterIndexed { j, _ -> j != i }.toSet()
                prefs.allowedSites = keep; Blocker.allowed = keep; render()
            }, listOf(CardAction(getString(R.string.ok), true) {}))
        }

        section(R.string.privacy)
        item(getString(R.string.clear_data), getString(R.string.clear_data_value)) { clearData() }

        section(R.string.site_permissions)
        val decisions = SitePermissions.decisions(prefs)
        item(getString(R.string.site_permissions), if (decisions.isEmpty()) getString(R.string.no_site_permissions) else resources.getQuantityString(R.plurals.site_permission_count, decisions.size, decisions.size)) {
            if (decisions.isEmpty()) return@item
            // Tap one to forget it; the site asks again next time.
            var sheet: Sheet? = null
            sheet = Sheet.show(this, getString(R.string.site_permissions), getString(R.string.site_permissions_tap), choices(decisions.map {
                (Uri.parse(it.origin).host ?: it.origin) to getString(it.kind.label).replaceFirstChar { c -> c.uppercase() } + ": " + getString(if (it.allow) R.string.allowed else R.string.blocked)
            }) { i -> sheet?.dismiss(); SitePermissions.forget(prefs, decisions[i]); render() }, listOf(CardAction(getString(R.string.ok), true) {}))
        }

        section(R.string.desktop_site)
        toggle(getString(R.string.desktop_by_default), prefs.desktopDefault) { prefs.desktopDefault = it }

        list.addView(text("Mica " + packageManager.getPackageInfo(packageName, 0).versionName, 12f, col(R.color.mica_faint)).apply { pad(4, 28) })
    }

    /** What to clear and from when, as on desktop: history, cookies and cache ticked; the last hour first. */
    private fun clearData() {
        val ranges = intArrayOf(R.string.last_hour, R.string.last_day, R.string.last_week, R.string.all_time)
        var range = 0
        val chips = row()
        fun paint() {
            for (i in 0 until chips.childCount) (chips.getChildAt(i) as android.widget.TextView).apply {
                val on = i == range
                setTextColor(col(if (on) R.color.mica_canvas else R.color.mica_ink))
                background = pressable(col(if (on) R.color.mica_accent else R.color.mica_card), 15, if (on) 0 else col(R.color.mica_line))
            }
        }
        ranges.forEachIndexed { i, r ->
            chips.addView(text(getString(r), 13f).apply { pad(12, 7); setOnClickListener { range = i; paint() } }, lp(WRAP, WRAP).apply { marginEnd = dp(6) })
        }
        paint()
        val box = column()
        box.addView(android.widget.HorizontalScrollView(this).apply { isHorizontalScrollBarEnabled = false; addView(chips) }, lp(MATCH, WRAP).apply { bottomMargin = dp(8) })
        fun check(title: Int, detail: Int, on: Boolean): android.widget.CheckBox {
            val c = androidx.appcompat.widget.AppCompatCheckBox(this).apply { isChecked = on }
            val t = column(text(getString(title), 15f), text(getString(detail), 12f, col(R.color.mica_muted)))
            val r = row(c, t).apply { pad(0, 4); setOnClickListener { c.toggle() } }
            t.layoutParams = lp(0, WRAP, 1f)
            box.addView(r, lp(MATCH, WRAP))
            return c
        }
        val history = check(R.string.data_history, R.string.data_history_detail, true)
        val cookies = check(R.string.data_cookies, R.string.data_cookies_detail, true)
        val cache = check(R.string.data_cache, R.string.data_cache_detail, true)
        val forms = check(R.string.data_forms, R.string.data_forms_detail, false)
        Sheet.show(this, getString(R.string.clear_data), getString(R.string.clear_data_detail), box, listOf(
            CardAction(getString(R.string.cancel)) {},
            CardAction(getString(R.string.clear_data_button), true) {
                if (!history.isChecked && !cookies.isChecked && !cache.isChecked && !forms.isChecked) return@CardAction
                val now = System.currentTimeMillis()
                val since = when (range) { 0 -> now - 3_600_000L; 1 -> now - 86_400_000L; 2 -> now - 7 * 86_400_000L; else -> null }
                (application as MicaApp).browser.clearData(since, history.isChecked, cookies.isChecked, cache.isChecked, forms.isChecked)
                notice(getString(R.string.data_cleared))
            }))
    }

    /** One choice from a few, in a Mica sheet; the current one is ticked. */
    private fun pick(title: String, labels: Array<String>, current: Int, chosen: (Int) -> Unit) {
        var sheet: Sheet? = null
        sheet = Sheet.show(this, title, null, choices(labels.mapIndexed { i, l -> (if (i == current) "✓  " else "     ") + l to null }) { i -> sheet?.dismiss(); if (i != current) chosen(i) })
    }

    private fun section(title: Int) {
        list.addView(text(getString(title).uppercase(), 11f, col(R.color.mica_faint)).apply { letterSpacing = 0.08f; pad(4, 0); layoutParams = lp(MATCH, WRAP).apply { topMargin = dp(22); bottomMargin = dp(6) } })
    }

    private fun item(title: String, value: String, onClick: () -> Unit) {
        val t = text(title, 16f)
        val v = text(value, 13f, col(R.color.mica_muted)).apply { maxLines = 2 }
        val c = column(t, v).apply { pad(14, 12); background = pressable(col(R.color.mica_card), 16, col(R.color.mica_line)); setOnClickListener { onClick() } }
        (v.layoutParams as LinearLayout.LayoutParams).topMargin = dp(4)
        list.addView(c, lp(MATCH, WRAP).apply { bottomMargin = dp(8) })
    }

    private fun toggle(title: String, on: Boolean, change: (Boolean) -> Unit) {
        val s = SwitchCompat(this).apply { isChecked = on; setOnCheckedChangeListener { _, v -> change(v) } }
        val t = text(title, 16f)
        val r = row(t, s).apply { pad(14, 10); background = rounded(col(R.color.mica_card), 16, col(R.color.mica_line)) }
        t.layoutParams = lp(0, WRAP, 1f)
        r.setOnClickListener { s.toggle() }
        list.addView(r, lp(MATCH, WRAP).apply { bottomMargin = dp(8) })
    }

}
