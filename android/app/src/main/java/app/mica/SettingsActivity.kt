package app.mica

import android.content.Intent
import android.graphics.Canvas
import android.graphics.ColorFilter
import android.graphics.Paint
import android.graphics.PixelFormat
import android.graphics.drawable.Drawable
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.os.Bundle
import android.text.TextUtils
import android.view.Gravity
import android.view.View
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.appcompat.app.AppCompatDelegate
import androidx.appcompat.widget.SwitchCompat
import androidx.core.graphics.PathParser
import androidx.core.os.LocaleListCompat
import androidx.core.view.ViewCompat
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat

/**
 * Settings, laid out like Mica's desktop cards: the account on top, then a few calm groups (one rounded card each,
 * rows with an icon tile, a line between them), the theme as a segmented choice right in its row.
 */
class SettingsActivity : AppCompatActivity() {
    private val prefs get() = (application as MicaApp).prefs
    private lateinit var list: LinearLayout

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        WindowCompat.setDecorFitsSystemWindows(window, false)
        list = column().apply { pad(16, 8) }
        val scroll = ScrollView(this).apply { addView(list); setBackgroundColor(col(R.color.mica_canvas)); isVerticalScrollBarEnabled = false }
        setContentView(scroll)
        ViewCompat.setOnApplyWindowInsetsListener(scroll) { v, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            v.setPadding(bars.left, bars.top, bars.right, bars.bottom); insets
        }
        render()
        (application as MicaApp).account.refreshProfile()
    }

    private val refresh: () -> Unit = { if (!isDestroyed) render() }
    override fun onStart() { super.onStart(); (application as MicaApp).account.listen(refresh) }
    override fun onStop() { super.onStop(); (application as MicaApp).account.unlisten(refresh) }
    override fun onResume() { super.onResume(); render() }

    private fun render() {
        list.removeAllViews()
        // Header: back, and the page's name.
        val back = ImageView(this).apply {
            setImageDrawable(Glyph(this@SettingsActivity, G.BACK, col(R.color.mica_ink)))
            pad(10); background = pressable(col(R.color.mica_canvas), 20)
            contentDescription = getString(R.string.cancel)
            setOnClickListener { finish() }
        }
        val title = text(getString(R.string.settings), 28f, col(R.color.mica_ink), bold = true).apply { pad(6, 0) }
        list.addView(row(back, title).apply { pad(0, 8) }, lp(MATCH, WRAP).apply { bottomMargin = dp(10) })
        back.layoutParams = lp(dp(44), dp(44))

        accountCard()

        group(R.string.appearance) {
            add(themeRow())
            val langTags = arrayOf("", "en", "vi")
            val langLabels = arrayOf(getString(R.string.system), getString(R.string.english), getString(R.string.vietnamese))
            val current = AppCompatDelegate.getApplicationLocales().toLanguageTags().substringBefore('-')
            add(entry(G.GLOBE, getString(R.string.language), langLabels[langTags.indexOf(current).coerceAtLeast(0)]) {
                pick(getString(R.string.language), langLabels, langTags.indexOf(current).coerceAtLeast(0)) { i ->
                    AppCompatDelegate.setApplicationLocales(LocaleListCompat.forLanguageTags(langTags[i]))
                }
            })
        }

        group(R.string.browsing) {
            add(switchRow(G.DESKTOP, getString(R.string.desktop_by_default), null, prefs.desktopDefault) { prefs.desktopDefault = it })
        }

        group(R.string.ad_blocking) {
            val engine = Blocker.engine
            add(switchRow(G.SHIELD, getString(R.string.block_ads), if (engine == null) getString(R.string.rules_loading) else getString(R.string.rules_loaded, engine.size, Blocker.lists), prefs.blockerOn) { prefs.blockerOn = it; Blocker.enabled = it })
            val allowed = prefs.allowedSites.sorted()
            add(entry(G.CHECK, getString(R.string.allowed_sites), if (allowed.isEmpty()) getString(R.string.no_allowed_sites) else allowed.joinToString(", "), enabled = allowed.isNotEmpty()) {
                // Tap a site to block its ads again.
                var sheet: Sheet? = null
                sheet = Sheet.show(this@SettingsActivity, getString(R.string.allowed_sites), getString(R.string.allowed_sites_tap), choices(allowed.map { it to null }) { i ->
                    sheet?.dismiss()
                    val keep = allowed.filterIndexed { j, _ -> j != i }.toSet()
                    prefs.allowedSites = keep; Blocker.allowed = keep; render()
                }, listOf(CardAction(getString(R.string.ok), true) {}))
            })
        }

        group(R.string.privacy) {
            val decisions = SitePermissions.decisions(prefs)
            add(entry(G.LOCK, getString(R.string.site_permissions), if (decisions.isEmpty()) getString(R.string.no_site_permissions) else resources.getQuantityString(R.plurals.site_permission_count, decisions.size, decisions.size), enabled = decisions.isNotEmpty()) {
                // Tap one to forget it; the site asks again next time.
                var sheet: Sheet? = null
                sheet = Sheet.show(this@SettingsActivity, getString(R.string.site_permissions), getString(R.string.site_permissions_tap), choices(decisions.map {
                    (Uri.parse(it.origin).host ?: it.origin) to getString(it.kind.label).replaceFirstChar { c -> c.uppercase() } + ": " + getString(if (it.allow) R.string.allowed else R.string.blocked)
                }) { i -> sheet?.dismiss(); SitePermissions.forget(prefs, decisions[i]); render() }, listOf(CardAction(getString(R.string.ok), true) {}))
            })
            add(entry(G.TRASH, getString(R.string.clear_data), getString(R.string.clear_data_value)) { clearData() })
        }

        // Footer: the wordmark and the version, quietly.
        val version = packageManager.getPackageInfo(packageName, 0).versionName
        list.addView(column(
            text("M.", 26f, col(R.color.mica_faint), bold = true).apply { gravity = Gravity.CENTER },
            text("Mica $version", 12f, col(R.color.mica_faint)).apply { gravity = Gravity.CENTER; pad(0, 6) }
        ).apply { gravity = Gravity.CENTER_HORIZONTAL; pad(0, 28) }, lp(MATCH, WRAP))
    }

    // ── The account card, as desktop's: your Discord banner (or its colour, else Mica's accent), the picture in a ring
    // over its lower edge, the name, then one wide button: Passwords & sync, or Sign in with Discord.
    private fun accountCard() {
        val account = (application as MicaApp).account
        val user = account.user
        val accent = col(R.color.mica_accent)
        val tint = user?.accentColor?.let { it or 0xFF000000.toInt() } ?: accent
        val bannerUrl = user?.let { DiscordImages.bannerUrl(it) }
        val tall = dp(if (bannerUrl != null) 96 else 58)
        val card = FrameLayout(this).apply { background = rounded(col(R.color.mica_card), 20, col(R.color.mica_line)); clipToOutline = true }
        val banner = ImageView(this).apply {
            scaleType = ImageView.ScaleType.CENTER_CROP
            background = GradientDrawable(GradientDrawable.Orientation.LEFT_RIGHT, intArrayOf(tint, (tint and 0x00FFFFFF) or 0xCC000000.toInt()))
        }
        card.addView(banner, FrameLayout.LayoutParams(MATCH, tall))
        // The ring: the card's colour around a fixed circle, so the picture can't show square whatever it is.
        val initial = user?.username?.firstOrNull()?.uppercaseChar()?.toString()
        val face = FrameLayout(this).apply { background = GradientDrawable().apply { shape = GradientDrawable.OVAL; setColor(col(R.color.mica_soft)) }; clipToOutline = true }
        face.addView(if (initial != null) text(initial, 24f, tint, bold = true).apply { gravity = Gravity.CENTER }
            else ImageView(this).apply { setImageDrawable(Glyph(this@SettingsActivity, G.PERSON, accent)); pad(16) }, FrameLayout.LayoutParams(MATCH, MATCH))
        val ring = FrameLayout(this).apply {
            background = GradientDrawable().apply { shape = GradientDrawable.OVAL; setColor(col(R.color.mica_card)) }
            setPadding(dp(5), dp(5), dp(5), dp(5)); addView(face, FrameLayout.LayoutParams(MATCH, MATCH))
        }
        card.addView(ring, FrameLayout.LayoutParams(dp(76), dp(76)).apply { leftMargin = dp(14); topMargin = tall - dp(38) })

        val name = text(user?.username ?: getString(R.string.not_signed_in), 19f, col(R.color.mica_ink), bold = true).apply { maxLines = 1; ellipsize = TextUtils.TruncateAt.END }
        val detail = text(if (user != null) "Discord" else getString(R.string.sync_pitch), 13f, col(R.color.mica_muted)).apply { maxLines = 2; pad(0, 3) }
        // One wide button, as on desktop: what the account opens, or how to get one.
        val pending = user == null && account.signIn != null
        val label = getString(when { user != null -> R.string.passwords_sync; pending -> R.string.cancel_sign_in; else -> R.string.sign_in_discord })
        val button = row(
            ImageView(this).apply { setImageDrawable(Glyph(this@SettingsActivity, if (user != null) G.KEY else G.PERSON, if (user == null && !pending) col(R.color.mica_canvas) else accent)) },
            text(label, 14f, if (user == null && !pending) col(R.color.mica_canvas) else accent, bold = true).apply { pad(8, 0) }
        ).apply {
            gravity = Gravity.CENTER; setPadding(dp(14), dp(11), dp(14), dp(11))
            background = pressable(if (user == null && !pending) accent else blend(col(R.color.mica_card), accent, 0.1f), 14)
            setOnClickListener {
                when {
                    user != null -> startActivity(Intent(this@SettingsActivity, PasswordsActivity::class.java))
                    pending -> account.cancelSignIn()
                    else -> account.beginSignIn { s, e ->
                        if (s == null) { notice(e?.message ?: getString(R.string.sync_unreachable)); return@beginSignIn }
                        // Discord opens in a Mica tab; the code waits in a card there to be typed on Discord's page.
                        startActivity(Intent(this@SettingsActivity, MainActivity::class.java).setAction(MainActivity.ACTION_SIGN_IN).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP))
                    }
                }
            }
        }
        (button.getChildAt(0).layoutParams as LinearLayout.LayoutParams).apply { width = dp(16); height = dp(16) }
        val foot = column(name, detail, button).apply { setPadding(dp(16), tall + dp(46), dp(16), dp(16)) }
        (button.layoutParams as LinearLayout.LayoutParams).apply { width = MATCH; topMargin = dp(14) }
        card.addView(foot, FrameLayout.LayoutParams(MATCH, WRAP))
        list.addView(card, lp(MATCH, WRAP).apply { bottomMargin = dp(8) })

        if (user == null) return
        // The pictures arrive later (cached after the first time); the card may be gone by then.
        DiscordImages.load(this, account, DiscordImages.avatarUrl(user), "avatar", dp(66)) { b ->
            if (isDestroyed || !face.isAttachedToWindow) return@load
            face.removeAllViews()
            face.addView(ImageView(this).apply { scaleType = ImageView.ScaleType.CENTER_CROP; setImageBitmap(b) }, FrameLayout.LayoutParams(MATCH, MATCH))
        }
        if (bannerUrl != null) DiscordImages.load(this, account, bannerUrl, "banner", resources.displayMetrics.widthPixels) { b ->
            if (!isDestroyed && banner.isAttachedToWindow) banner.setImageBitmap(b)
        }
    }

    // ── Groups: a small heading, then one card holding the rows, a hairline between them (inset past the icon).
    private class Group(val box: LinearLayout) { val rows = mutableListOf<View>(); fun add(v: View) { rows += v } }

    private fun group(title: Int, fill: Group.() -> Unit) {
        list.addView(text(getString(title).uppercase(), 11f, col(R.color.mica_muted), bold = true).apply {
            letterSpacing = 0.08f; layoutParams = lp(MATCH, WRAP).apply { topMargin = dp(20); bottomMargin = dp(8); marginStart = dp(6) }
        })
        val box = column().apply { background = rounded(col(R.color.mica_card), 20, col(R.color.mica_line)); clipToOutline = true }
        val g = Group(box).apply(fill)
        g.rows.forEachIndexed { i, v ->
            if (i > 0) box.addView(View(this).apply { setBackgroundColor(col(R.color.mica_line)); alpha = 0.6f }, lp(MATCH, maxOf(1, dp(0.75f))).apply { marginStart = dp(62) })
            box.addView(v, lp(MATCH, WRAP))
        }
        list.addView(box, lp(MATCH, WRAP))
    }

    /** The icon tile: a soft rounded square with an accent line icon. */
    private fun tile(glyph: String): View = ImageView(this).apply {
        setImageDrawable(Glyph(this@SettingsActivity, glyph, col(R.color.mica_accent)))
        background = rounded(col(R.color.mica_soft), 11); pad(7)
        layoutParams = lp(dp(34), dp(34)).apply { marginEnd = dp(14) }
    }

    private fun words(title: String, value: String?): LinearLayout {
        val t = text(title, 16f).apply { maxLines = 2 }
        val c = column(t).apply { layoutParams = lp(0, WRAP, 1f) }
        if (!value.isNullOrEmpty()) c.addView(text(value, 13f, col(R.color.mica_muted)).apply { maxLines = 2; ellipsize = TextUtils.TruncateAt.END }, lp(MATCH, WRAP).apply { topMargin = dp(3) })
        return c
    }

    /** A row that opens something: icon, title and its current value, a chevron. */
    private fun entry(glyph: String, title: String, value: String?, enabled: Boolean = true, onClick: () -> Unit): View {
        val chevron = ImageView(this).apply { setImageDrawable(Glyph(this@SettingsActivity, G.CHEVRON, col(R.color.mica_faint))); layoutParams = lp(dp(16), dp(16)).apply { marginStart = dp(8) } }
        return row(tile(glyph), words(title, value), chevron).apply {
            setPadding(dp(14), dp(13), dp(14), dp(13)); minimumHeight = dp(60)
            background = pressable(col(R.color.mica_card), 0)
            if (enabled) setOnClickListener { onClick() } else chevron.visibility = View.INVISIBLE
        }
    }

    private fun switchRow(glyph: String, title: String, value: String?, on: Boolean, change: (Boolean) -> Unit): View {
        val s = SwitchCompat(this).apply { isChecked = on; setOnCheckedChangeListener { _, v -> change(v) } }
        return row(tile(glyph), words(title, value), s).apply {
            setPadding(dp(14), dp(11), dp(10), dp(11)); minimumHeight = dp(60)
            background = pressable(col(R.color.mica_card), 0); setOnClickListener { s.toggle() }
        }
    }

    /** Theme: System, Light or Dark as a segmented choice in the row itself. */
    private fun themeRow(): View {
        val labels = arrayOf(getString(R.string.system), getString(R.string.theme_light), getString(R.string.theme_dark))
        val modes = intArrayOf(AppCompatDelegate.MODE_NIGHT_FOLLOW_SYSTEM, AppCompatDelegate.MODE_NIGHT_NO, AppCompatDelegate.MODE_NIGHT_YES)
        val current = modes.indexOf(prefs.nightMode).coerceAtLeast(0)
        val segments = row().apply { background = rounded(col(R.color.mica_soft), 12); setPadding(dp(3), dp(3), dp(3), dp(3)) }
        labels.forEachIndexed { i, label ->
            val on = i == current
            segments.addView(text(label, 13f, col(if (on) R.color.mica_ink else R.color.mica_muted), bold = on).apply {
                gravity = Gravity.CENTER; pad(4, 8); maxLines = 1
                if (on) { background = rounded(col(R.color.mica_card), 9); elevation = dp(1).toFloat() }
                setOnClickListener {
                    if (i == current) return@setOnClickListener
                    prefs.nightMode = modes[i]
                    (application as MicaApp).account.noteSettingsChange()
                    AppCompatDelegate.setDefaultNightMode(modes[i])
                    render()
                }
            }, lp(0, WRAP, 1f))
        }
        val head = row(tile(G.MOON), words(getString(R.string.theme), null))
        return column(head, segments).apply {
            setPadding(dp(14), dp(13), dp(14), dp(14))
            (segments.layoutParams as LinearLayout.LayoutParams).topMargin = dp(10)
        }
    }

    /** What to clear and from when, as on desktop: history, cookies and cache ticked; the last hour first. */
    private fun clearData() {
        val ranges = intArrayOf(R.string.last_hour, R.string.last_day, R.string.last_week, R.string.all_time)
        var range = 0
        val chips = row()
        fun paint() {
            for (i in 0 until chips.childCount) (chips.getChildAt(i) as TextView).apply {
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

    /** Line icons on a 24-unit grid, the same drawings as the desktop shell's glyphs. */
    private object G {
        const val BACK = "M19 12H5 M11 6l-6 6 6 6"
        const val CHEVRON = "M9 6l6 6-6 6"
        const val PERSON = "M12 4a4 4 0 1 1 0 8a4 4 0 1 1 0-8z M4.5 20.5c1.4-3.6 4.2-5.5 7.5-5.5s6.1 1.9 7.5 5.5"
        const val MOON = "M19.5 14.5A8 8 0 0 1 9.5 4.5a8 8 0 1 0 10 10z"
        const val GLOBE = "M12 3a9 9 0 1 1 0 18a9 9 0 1 1 0-18z M3 12h18 M12 3c3.2 3.2 3.2 14.8 0 18 M12 3c-3.2 3.2-3.2 14.8 0 18"
        const val DESKTOP = "M3.5 5h17v11h-17z M9 20h6 M12 16v4"
        const val SHIELD = "M12 3.5l7 2.5v5.5c0 4.5-3 7.8-7 9-4-1.2-7-4.5-7-9V6z M9 12l2.2 2.2L15.5 10"
        const val CHECK = "M5 12.5l4.5 4.5L19 7.5"
        const val LOCK = "M7.5 11V8a4.5 4.5 0 0 1 9 0v3 M5.5 11h13v9.5h-13z"
        const val TRASH = "M5 7h14 M9.5 7V4.5h5V7 M7 7l1 13h8l1-13"
        const val KEY = "M8 8.5a4 4 0 1 1 0 8a4 4 0 1 1 0-8z M12 12.5h8 M17 12.5v3 M20 12.5v2.5"
    }

    /** A stroked path drawing scaled to its bounds. */
    private class Glyph(context: android.content.Context, data: String, color: Int) : Drawable() {
        private val path = PathParser.createPathFromPathData(data)
        private val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            style = Paint.Style.STROKE; this.color = color; strokeCap = Paint.Cap.ROUND; strokeJoin = Paint.Join.ROUND
        }
        private val stroke = context.resources.displayMetrics.density * 1.6f
        override fun draw(canvas: Canvas) {
            val b = bounds; if (b.isEmpty) return
            val scale = minOf(b.width(), b.height()) / 24f
            canvas.save(); canvas.translate(b.left + (b.width() - 24 * scale) / 2, b.top + (b.height() - 24 * scale) / 2); canvas.scale(scale, scale)
            paint.strokeWidth = stroke / scale; canvas.drawPath(path, paint); canvas.restore()
        }
        override fun setAlpha(alpha: Int) { paint.alpha = alpha }
        override fun setColorFilter(colorFilter: ColorFilter?) { paint.colorFilter = colorFilter }
        @Deprecated("Deprecated in Java") override fun getOpacity() = PixelFormat.TRANSLUCENT
        override fun getIntrinsicWidth() = -1
        override fun getIntrinsicHeight() = -1
    }
}
