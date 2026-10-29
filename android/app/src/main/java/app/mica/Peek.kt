package app.mica

import android.content.Context
import android.graphics.drawable.ColorDrawable
import android.view.View
import android.widget.FrameLayout
import android.webkit.WebView

/**
 * A quick look at a link without leaving the page (desktop's BrowserPeek): a card grown out of the island
 * over a dimmed page. Back or a tap outside puts it away; "Open as tab" keeps it. The page inside can't
 * ask for permissions, download, open windows or apps, and only goes elsewhere when you tap.
 */
class Peek(private val activity: MainActivity) {
    private val ctx: Context = activity
    val view = FrameLayout(ctx)
    private val panel = FrameLayout(ctx)
    private val title = ctx.text("", 14f, bold = true).apply { setSingleLine(); ellipsize = android.text.TextUtils.TruncateAt.END }
    private val address = ctx.text("", 12f, ctx.col(R.color.mica_muted)).apply { setSingleLine(); ellipsize = android.text.TextUtils.TruncateAt.MIDDLE }
    private val body = FrameLayout(ctx)
    private var page: WebView? = null
    private var source: Tab? = null
    private lateinit var morph: IslandMorph
    val showing: Boolean get() = view.visibility == View.VISIBLE && !morph.closing

    init {
        view.visibility = View.GONE
        view.background = ColorDrawable(ctx.col(R.color.mica_scrim))
        view.setOnClickListener { hide() }
        panel.background = ctx.rounded(ctx.col(R.color.mica_canvas), 20)
        panel.elevation = ctx.dp(12).toFloat()
        panel.isClickable = true

        val open = ctx.text(ctx.getString(R.string.open_as_tab), 13f, ctx.col(R.color.mica_accent), bold = true).apply {
            pad(12, 8); background = ctx.pressable(0, 14); setOnClickListener { openAsTab() }
        }
        val close = ctx.text("✕", 16f, ctx.col(R.color.mica_muted)).apply {
            pad(12, 8); background = ctx.pressable(0, 14); contentDescription = ctx.getString(R.string.close); setOnClickListener { hide() }
        }
        val labels = ctx.column(title, address)
        val head = ctx.row(labels, open, close)
        labels.layoutParams = lp(0, WRAP, 1f)
        val column = ctx.column()
        column.addView(head, lp(MATCH, WRAP).apply { setMargins(ctx.dp(18), ctx.dp(8), ctx.dp(6), ctx.dp(6)) })
        column.addView(body, lp(MATCH, 0, 1f))
        panel.addView(column, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
        view.addView(panel, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
        morph = IslandMorph(panel, activity.island, ctx.dp(20).toFloat(), view) { activity.islandBusy() }
    }

    fun setInsets(top: Int, bottom: Int) {
        (panel.layoutParams as FrameLayout.LayoutParams).setMargins(ctx.dp(10), top + ctx.dp(28), ctx.dp(10), bottom + ctx.dp(10))
        panel.requestLayout()
    }

    fun show(from: Tab, url: String) {
        drop()
        source = from
        title.text = Navigation.label(url)
        address.text = url
        val v = activity.browser.peekView(from) { w ->
            val at = Navigation.fromEngine(w.url ?: url)
            title.text = w.title?.takeIf { it.isNotBlank() && !it.startsWith("http") } ?: Navigation.label(at)
            address.text = at
        }
        page = v
        body.addView(v, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
        v.loadUrl(Navigation.engineAddress(url))
        if (!showing) { view.visibility = View.VISIBLE; morph.open() }
    }

    /** Back: a step back inside the peek first, then away. */
    fun back() { val v = page; if (v != null && v.canGoBack()) v.goBack() else hide() }

    fun hide() {
        if (!showing) return
        morph.close { view.visibility = View.GONE; drop(); activity.onPanelClosed() }
    }

    fun cut() { if (view.visibility == View.VISIBLE) { morph.cut(); view.visibility = View.GONE; drop(); activity.onPanelClosed() } }

    fun pause() { page?.onPause() }
    fun resume() { page?.onResume() }

    private fun openAsTab() {
        val url = page?.url?.let { Navigation.fromEngine(it) } ?: return
        val from = source
        val browser = activity.browser
        cut()
        browser.open(url, parent = from?.takeIf { browser.tab(it.id) != null }, spaceId = from?.space ?: browser.activeSpaceId)
    }

    private fun drop() {
        page?.let { body.removeView(it); it.stopLoading(); it.destroy() }
        page = null
        source = null
    }
}
