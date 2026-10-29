package app.mica

import android.content.Context
import android.text.Editable
import android.text.InputType
import android.text.TextWatcher
import android.view.Gravity
import android.view.KeyEvent
import android.view.View
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.HorizontalScrollView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView

/**
 * Desktop's Spotlight as a sheet rising from the island: the field sits at the bottom by your thumb,
 * results grow upward. Empty, it offers page actions, the games and recent tabs.
 */
class Spotlight(private val activity: MainActivity) {
    private val ctx: Context = activity
    private val browser get() = activity.browser
    val view = FrameLayout(ctx)
    private val panel = LinearLayout(ctx)
    private val results = LinearLayout(ctx)
    private val scroll = CappedScroll(ctx)
    private val field = EditText(ctx)
    val showing: Boolean get() = view.visibility == View.VISIBLE && !morph.closing
    private val morph = IslandMorph(panel, activity.island, ctx.dp(26).toFloat(), view) { activity.islandBusy() }

    init {
        view.visibility = View.GONE
        view.setBackgroundColor(ctx.col(R.color.mica_scrim))
        view.setOnClickListener { hide() }

        panel.orientation = LinearLayout.VERTICAL
        panel.background = ctx.rounded(ctx.col(R.color.mica_panel), 26, ctx.col(R.color.mica_line))
        panel.elevation = ctx.dp(12).toFloat()
        panel.isClickable = true
        panel.pad(14, 12)

        results.orientation = LinearLayout.VERTICAL
        scroll.addView(results)
        scroll.isVerticalScrollBarEnabled = false
        panel.addView(scroll, lp(MATCH, WRAP))

        field.hint = ctx.getString(R.string.search_hint)
        field.setSingleLine()
        field.inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI
        field.imeOptions = EditorInfo.IME_ACTION_GO or EditorInfo.IME_FLAG_NO_EXTRACT_UI
        field.setTextColor(ctx.col(R.color.mica_ink))
        field.setHintTextColor(ctx.col(R.color.mica_faint))
        field.textSize = 16f
        field.background = ctx.rounded(ctx.col(R.color.mica_card), 22, ctx.col(R.color.mica_line))
        field.pad(18, 0)
        field.setSelectAllOnFocus(true)
        field.setOnEditorActionListener { _, id, e ->
            if (id == EditorInfo.IME_ACTION_GO || e?.keyCode == KeyEvent.KEYCODE_ENTER) { go(field.text.toString()); true } else false
        }
        field.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
            override fun onTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
            override fun afterTextChanged(s: Editable?) { if (showing) render(s.toString()) }
        })
        panel.addView(field, lp(MATCH, ctx.dp(46)).apply { topMargin = ctx.dp(10) })

        view.addView(panel, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM).apply {
            setMargins(ctx.dp(10), 0, ctx.dp(10), ctx.dp(10))
        })
    }

    /** Space for the system bars and keyboard, from the Activity's insets. */
    fun setInsets(top: Int, bottom: Int) {
        (panel.layoutParams as FrameLayout.LayoutParams).bottomMargin = bottom + ctx.dp(10)
        scroll.maxHeight = (activity.window.decorView.height - top - bottom - ctx.dp(110)).coerceAtLeast(ctx.dp(160))
        panel.requestLayout()
    }

    fun show(prefill: String = "") {
        val tint = browser.active?.tint ?: 0
        panel.background = ctx.rounded(if (tint != 0) blend(ctx.col(R.color.mica_panel), tint, 0.18f) else ctx.col(R.color.mica_panel), 26, ctx.col(R.color.mica_line))
        val opening = view.visibility != View.VISIBLE || morph.closing
        view.visibility = View.VISIBLE
        if (opening) morph.open()
        field.setText(prefill)
        render(prefill)
        // Before the window has focus (Spotlight opening with the app) the keyboard would attach to
        // nothing; MainActivity calls focusField again once the window gets focus.
        if (activity.hasWindowFocus()) focusField()
    }

    fun focusField() {
        if (!showing) return
        field.requestFocus()
        field.selectAll()
        field.post { (ctx.getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager).showSoftInput(field, InputMethodManager.SHOW_IMPLICIT) }
    }

    fun hide() {
        if (!showing) return
        (ctx.getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager).hideSoftInputFromWindow(field.windowToken, 0)
        field.clearFocus()
        morph.close { view.visibility = View.GONE; activity.onPanelClosed() }
        activity.onSpotlightHidden()
    }

    /** Gone at once, without shrinking into the pill. */
    fun cut() { if (view.visibility != View.VISIBLE) return; hide(); morph.cut(); view.visibility = View.GONE; activity.onPanelClosed() }

    private fun go(text: String) {
        val url = Navigation.resolve(text.trim()) ?: return
        val first = (results.getChildAt(0)?.tag as? Navigation.Hit)
        hide()
        if (first != null && first.kind == Navigation.Kind.SHORTCUT) { activity.openHit(first); return }
        activity.navigateActive(url)
    }

    private fun render(query: String) {
        results.removeAllViews()
        val active = browser.active
        val shownUrl = active?.url?.let { Navigation.fromEngine(it) }
        val q = if (query == shownUrl) "" else query
        if (q.isBlank()) { renderEmpty(); return }
        val tabs = browser.tabs.filter { it !== active }.sortedByDescending { it.lastUsed }
            .map { Navigation.Entry(it.url, it.title, it.id, browser.spaceName(browser.spaces.first { s -> s.id == it.space }) + " · " + Navigation.label(it.url)) }
        val history = browser.history.map { Navigation.Entry(it.url, it.title) }
        val marks = browser.bookmarks.map { Navigation.Entry(it.url, it.title, detail = "★ " + Navigation.label(it.url)) }
        val hits = Navigation.search(q, tabs, history, { ctx.getString(R.string.search_web_for, it) }, { ctx.getString(R.string.go_to, it) }, marks)
        hits.forEach { results.addView(hitRow(it)) }
    }

    private fun hitRow(hit: Navigation.Hit): View {
        val dot = View(ctx)
        val dotColor = when (hit.kind) {
            Navigation.Kind.TAB -> browser.tab(hit.tabId)?.let { t -> ctx.spaceColor(browser.spaces.first { it.id == t.space }.color) } ?: ctx.col(R.color.mica_accent)
            Navigation.Kind.HISTORY -> ctx.col(R.color.mica_faint)
            else -> ctx.col(R.color.mica_accent)
        }
        dot.background = ctx.rounded(dotColor, 4)
        val title = ctx.text(hit.title, 15f).apply { maxLines = 1; ellipsize = android.text.TextUtils.TruncateAt.END }
        val detail = ctx.text(hit.detail, 12f, ctx.col(R.color.mica_muted)).apply { maxLines = 1; ellipsize = android.text.TextUtils.TruncateAt.MIDDLE }
        val texts = ctx.column(title, detail).apply { (detail.layoutParams as LinearLayout.LayoutParams).topMargin = ctx.dp(3) }
        val row = ctx.row(dot, texts)
        dot.layoutParams = lp(ctx.dp(8), ctx.dp(8)).apply { marginEnd = ctx.dp(12) }
        texts.layoutParams = lp(0, WRAP, 1f)
        row.pad(10, 10)
        row.background = ctx.pressable(0, 14)
        row.tag = hit
        row.setOnClickListener { hide(); activity.openHit(hit) }
        return row
    }

    private fun renderEmpty() {
        val tab = browser.active
        val web = tab != null && !tab.isHome
        // Page actions.
        val actions = ctx.row()
        fun action(glyph: String, label: Int, enabled: Boolean = true, run: () -> Unit) {
            val g = ctx.text(glyph, 20f, ctx.col(if (enabled) R.color.mica_ink else R.color.mica_faint)).apply { gravity = Gravity.CENTER }
            val l = ctx.text(ctx.getString(label), 11f, ctx.col(R.color.mica_muted)).apply { gravity = Gravity.CENTER; maxLines = 1; ellipsize = android.text.TextUtils.TruncateAt.END }
            val cell = ctx.column(g, l).apply { gravity = Gravity.CENTER; pad(2, 8); background = ctx.pressable(0, 14); contentDescription = ctx.getString(label) }
            (l.layoutParams as LinearLayout.LayoutParams).topMargin = ctx.dp(4)
            cell.isEnabled = enabled
            cell.setOnClickListener { hide(); run() }
            actions.addView(cell, lp(0, WRAP, 1f))
        }
        val v = tab?.view
        action("←", R.string.back, v?.canGoBack() == true) { v?.goBack() }
        action("→", R.string.forward, v?.canGoForward() == true) { v?.goForward() }
        action(if ((tab?.progress ?: 100) in 1..99) "✕" else "↻", if ((tab?.progress ?: 100) in 1..99) R.string.stop else R.string.reload, web) { if ((tab?.progress ?: 100) in 1..99) v?.stopLoading() else v?.reload() }
        action("↗", R.string.share, web) { activity.sharePage() }
        val marked = web && browser.isBookmarked(Navigation.fromEngine(tab!!.url))
        action(if (marked) "★" else "☆", if (marked) R.string.bookmarked_short else R.string.bookmark, web) { activity.toggleSaved(false) }
        action("▤", R.string.library) { activity.openLibrary() }
        action("☰", R.string.settings) { activity.openSettings() }
        results.addView(actions)

        // Games strip.
        results.addView(header(R.string.games))
        val strip = ctx.row()
        val gameColors = intArrayOf(0xFF7A9CFF.toInt(), 0xFF4CBF6A.toInt(), 0xFFF2B179.toInt(), 0xFF888780.toInt(), 0xFFE5534B.toInt(), 0xFF3D6DF2.toInt(),
            0xFFE5534B.toInt(), 0xFFF2C230.toInt(), 0xFF35B9D6.toInt(), 0xFF4CBF6A.toInt(), 0xFFF2C230.toInt())
        val list = listOf(Navigation.TETRIS to "Tetris") + Navigation.games.drop(1).map { it.address to it.title }
        list.forEachIndexed { i, (address, title) ->
            val dot = View(ctx).apply { background = ctx.rounded(gameColors[i % gameColors.size], 5) }
            val chip = ctx.row(dot, ctx.text(title, 13f))
            dot.layoutParams = lp(ctx.dp(10), ctx.dp(10)).apply { marginEnd = ctx.dp(8) }
            chip.pad(12, 9)
            chip.background = ctx.pressable(ctx.col(R.color.mica_card), 16, ctx.col(R.color.mica_line))
            chip.setOnClickListener { hide(); activity.navigateActive(address) }
            strip.addView(chip, lp(WRAP, WRAP).apply { marginEnd = ctx.dp(8) })
        }
        val all = ctx.text("•••", 13f, ctx.col(R.color.mica_accent)).apply { pad(12, 9); background = ctx.pressable(0, 16); contentDescription = ctx.getString(R.string.games) }
        all.setOnClickListener { hide(); activity.navigateActive("mica://games") }
        strip.addView(all)
        val hs = HorizontalScrollView(ctx).apply { isHorizontalScrollBarEnabled = false; addView(strip) }
        results.addView(hs)

        // Recent tabs from every space.
        val recent = browser.tabs.filter { it !== tab && !it.isHome && it.url.isNotEmpty() }.sortedByDescending { it.lastUsed }.take(4)
        if (recent.isNotEmpty()) {
            results.addView(header(R.string.tabs_header))
            recent.forEach { t ->
                results.addView(hitRow(Navigation.Hit(Navigation.Kind.TAB, t.title.ifBlank { Navigation.label(t.url) },
                    browser.spaceName(browser.spaces.first { it.id == t.space }) + " · " + Navigation.label(t.url), t.url, t.id)))
            }
        }
    }

    private fun header(text: Int): TextView = ctx.text(ctx.getString(text).uppercase(), 11f, ctx.col(R.color.mica_faint)).apply {
        letterSpacing = 0.08f
        pad(10, 0)
        layoutParams = lp(MATCH, WRAP).apply { topMargin = ctx.dp(14); bottomMargin = ctx.dp(6) }
    }
}

/** A ScrollView that stops growing at [maxHeight]. */
class CappedScroll(context: Context) : ScrollView(context) {
    var maxHeight = Int.MAX_VALUE
    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        super.onMeasure(widthMeasureSpec, MeasureSpec.makeMeasureSpec(maxHeight, MeasureSpec.AT_MOST))
    }
}
