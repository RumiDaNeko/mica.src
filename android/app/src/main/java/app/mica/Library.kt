package app.mica

import android.content.Context
import android.text.Editable
import android.text.TextUtils
import android.text.TextWatcher
import android.text.format.DateUtils
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.HorizontalScrollView
import android.widget.LinearLayout
import android.widget.TextView
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView

/**
 * The library, grown out of the island like the deck: bookmarks, history (by day), read later and recently
 * closed tabs, with one search across the section shown. Tap opens; ✕ takes an entry out.
 */
class Library(private val activity: MainActivity) {
    enum class Section(val label: Int, val empty: Int) {
        BOOKMARKS(R.string.bookmarks, R.string.no_bookmarks),
        HISTORY(R.string.history, R.string.no_history),
        READ_LATER(R.string.read_later, R.string.no_read_later),
        CLOSED(R.string.recently_closed, R.string.no_closed),
        HIGHLIGHTS(R.string.highlights, R.string.no_highlights),
    }

    private val ctx: Context = activity
    private val browser get() = activity.browser
    val view = FrameLayout(ctx)
    private val chips = LinearLayout(ctx)
    private val search = ctx.field(ctx.getString(R.string.search_library))
    private val clear = ctx.text(ctx.getString(R.string.clear_history), 13f, ctx.col(R.color.mica_accent), bold = true)
    private val empty = ctx.text("", 14f, ctx.col(R.color.mica_muted)).apply { gravity = Gravity.CENTER; pad(24, 40) }
    private val list = RecyclerView(ctx)
    private var section = Section.BOOKMARKS
    private var rows: List<Any> = emptyList()
    private lateinit var morph: IslandMorph
    val showing: Boolean get() = view.visibility == View.VISIBLE && !morph.closing

    init {
        view.visibility = View.GONE
        view.setBackgroundColor(ctx.col(R.color.mica_canvas))
        view.isClickable = true

        val title = ctx.text(ctx.getString(R.string.library), 22f, bold = true)
        val downloads = ctx.text(ctx.getString(R.string.downloads), 13f, ctx.col(R.color.mica_accent), bold = true).apply {
            pad(12, 8); background = ctx.pressable(0, 14); setOnClickListener { activity.openDownloads() }
        }
        val head = ctx.row(title, downloads)
        title.layoutParams = lp(0, WRAP, 1f)

        val hs = HorizontalScrollView(ctx).apply { isHorizontalScrollBarEnabled = false; addView(chips) }
        search.addTextChangedListener(object : TextWatcher {
            override fun beforeTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
            override fun onTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
            override fun afterTextChanged(s: Editable?) { if (view.visibility == View.VISIBLE) render() }
        })
        clear.pad(12, 8)
        clear.background = ctx.pressable(0, 14)
        clear.setOnClickListener { confirmClear() }
        val tools = ctx.row(search, clear)
        search.layoutParams = lp(0, WRAP, 1f)
        (clear.layoutParams as LinearLayout.LayoutParams).marginStart = ctx.dp(6)

        list.layoutManager = LinearLayoutManager(ctx)
        list.adapter = Adapter()
        list.clipToPadding = false
        list.setPadding(ctx.dp(10), ctx.dp(4), ctx.dp(10), ctx.dp(24))

        val column = ctx.column()
        column.addView(head, lp(MATCH, WRAP).apply { setMargins(ctx.dp(20), ctx.dp(14), ctx.dp(12), 0) })
        column.addView(hs, lp(MATCH, WRAP).apply { setMargins(ctx.dp(16), ctx.dp(12), ctx.dp(16), 0) })
        column.addView(tools, lp(MATCH, WRAP).apply { setMargins(ctx.dp(16), ctx.dp(12), ctx.dp(12), ctx.dp(6)) })
        val body = FrameLayout(ctx)
        body.addView(list, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
        body.addView(empty, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.WRAP_CONTENT, Gravity.TOP))
        column.addView(body, lp(MATCH, 0, 1f))
        view.addView(column, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
        morph = IslandMorph(view, activity.island, 0f) { activity.islandBusy() }
    }

    fun setInsets(top: Int, bottom: Int) {
        view.setPadding(0, top, 0, 0)
        list.setPadding(list.paddingLeft, list.paddingTop, list.paddingRight, bottom + ctx.dp(24))
    }

    fun show(at: Section = section) {
        section = at
        search.setText("")
        if (!showing) { view.visibility = View.VISIBLE; morph.open() }
        render()
        list.scrollToPosition(0)
    }

    fun hide() {
        if (!showing) return
        view.hideKeyboard(); search.clearFocus()
        morph.close { view.visibility = View.GONE; activity.onPanelClosed() }
    }

    fun cut() { if (view.visibility == View.VISIBLE) { morph.cut(); view.visibility = View.GONE; activity.onPanelClosed() } }

    // Highlights show as entries too: the marked text as the title, the page it's on below.
    private val marks = java.util.IdentityHashMap<HistoryEntry, Highlights.Item>()

    private fun entries(s: Section): MutableList<HistoryEntry> = when (s) {
        Section.BOOKMARKS -> browser.bookmarks
        Section.HISTORY -> browser.history
        Section.READ_LATER -> browser.readLater
        Section.CLOSED -> browser.recentlyClosed
        Section.HIGHLIGHTS -> {
            marks.clear()
            browser.highlights.all().map { item -> HistoryEntry(item.url, "“" + item.mark.text + "”", item.mark.at).also { marks[it] = item } }.toMutableList()
        }
    }

    private fun remove(e: HistoryEntry) {
        if (section == Section.HIGHLIGHTS) marks[e]?.let { activity.removeHighlight(it) }
        else browser.forget(entries(section), e)
        if (section == Section.READ_LATER) Reader.forget(ctx, e.url)
    }

    private fun render() {
        chips.removeAllViews()
        for (s in Section.values()) {
            val on = s == section
            val chip = ctx.text(ctx.getString(s.label), 14f, ctx.col(if (on) R.color.mica_ink else R.color.mica_muted), bold = on).apply {
                pad(14, 9)
                background = ctx.pressable(ctx.col(if (on) R.color.mica_soft else R.color.mica_canvas), 18, if (on) ctx.col(R.color.mica_line) else 0)
                setOnClickListener { if (section != s) { section = s; render(); list.scrollToPosition(0) } }
            }
            chips.addView(chip, lp(WRAP, WRAP).apply { marginEnd = ctx.dp(8) })
        }
        val words = search.text.toString().lowercase().split(' ').filter { it.isNotEmpty() }
        val found = entries(section).filter { e -> val t = (e.title + " " + e.url).lowercase(); words.all { t.contains(it) } }
        // History reads by day; the other lists are short and already newest first.
        val out = ArrayList<Any>()
        var day = ""
        for (e in found) {
            if (section == Section.HISTORY) {
                val d = dayLabel(e.at)
                if (d != day) { out.add(d); day = d }
            }
            out.add(e)
        }
        rows = out
        clear.visibility = if (section == Section.HISTORY && browser.history.isNotEmpty()) View.VISIBLE else View.GONE
        empty.text = ctx.getString(if (words.isEmpty()) section.empty else R.string.nothing_found)
        empty.visibility = if (out.isEmpty()) View.VISIBLE else View.GONE
        @Suppress("NotifyDataSetChanged")
        list.adapter?.notifyDataSetChanged()
    }

    private fun dayLabel(at: Long): String = when {
        DateUtils.isToday(at) -> ctx.getString(R.string.today)
        DateUtils.isToday(at + DateUtils.DAY_IN_MILLIS) -> ctx.getString(R.string.yesterday)
        else -> DateUtils.formatDateTime(ctx, at, DateUtils.FORMAT_SHOW_DATE or DateUtils.FORMAT_SHOW_WEEKDAY or DateUtils.FORMAT_ABBREV_ALL)
    }

    private fun confirmClear() {
        activity.bubble.show(ctx.getString(R.string.clear_history_q), ctx.getString(R.string.clear_history_detail), listOf(
            CardAction(ctx.getString(R.string.cancel)) {},
            CardAction(ctx.getString(R.string.clear_history), true) { browser.clearHistory(); render(); activity.island.announce(ctx.getString(R.string.history_cleared)) }), 0)
    }

    private fun open(e: HistoryEntry) {
        hide()
        if (section == Section.CLOSED) browser.forget(browser.recentlyClosed, e)
        if (section == Section.READ_LATER) activity.openReadLater(e.url) else activity.openFromLibrary(e.url)
    }

    private inner class Holder(v: View, val title: TextView?, val detail: TextView?, val remove: TextView?) : RecyclerView.ViewHolder(v)

    private inner class Adapter : RecyclerView.Adapter<Holder>() {
        override fun getItemCount() = rows.size
        override fun getItemViewType(position: Int) = if (rows[position] is String) 0 else 1

        override fun onCreateViewHolder(parent: ViewGroup, type: Int): Holder {
            if (type == 0) {
                val h = ctx.text("", 11f, ctx.col(R.color.mica_faint)).apply { letterSpacing = 0.08f; pad(12, 0) }
                h.layoutParams = RecyclerView.LayoutParams(RecyclerView.LayoutParams.MATCH_PARENT, RecyclerView.LayoutParams.WRAP_CONTENT).apply { topMargin = ctx.dp(16); bottomMargin = ctx.dp(6) }
                return Holder(h, h, null, null)
            }
            val title = ctx.text("", 15f).apply { maxLines = 1; ellipsize = TextUtils.TruncateAt.END }
            val detail = ctx.text("", 12f, ctx.col(R.color.mica_muted)).apply { maxLines = 1; ellipsize = TextUtils.TruncateAt.MIDDLE }
            val texts = ctx.column(title, detail)
            (detail.layoutParams as LinearLayout.LayoutParams).topMargin = ctx.dp(3)
            val remove = ctx.text("✕", 15f, ctx.col(R.color.mica_faint)).apply { pad(10, 6); background = ctx.pressable(0, 14); contentDescription = ctx.getString(R.string.remove) }
            val row = ctx.row(texts, remove)
            texts.layoutParams = lp(0, WRAP, 1f)
            row.pad(12, 10)
            row.background = ctx.pressable(0, 14)
            row.layoutParams = RecyclerView.LayoutParams(RecyclerView.LayoutParams.MATCH_PARENT, RecyclerView.LayoutParams.WRAP_CONTENT)
            val h = Holder(row, title, detail, remove)
            row.setOnClickListener { (rows.getOrNull(h.bindingAdapterPosition) as? HistoryEntry)?.let { open(it) } }
            remove.setOnClickListener { (rows.getOrNull(h.bindingAdapterPosition) as? HistoryEntry)?.let { remove(it); render() } }
            return h
        }

        override fun onBindViewHolder(h: Holder, position: Int) {
            val item = rows[position]
            if (item is String) { h.title?.text = item.uppercase(); return }
            val e = item as HistoryEntry
            h.title?.text = e.title.ifBlank { Navigation.label(e.url) }
            val time = if (section == Section.HISTORY) DateUtils.formatDateTime(ctx, e.at, DateUtils.FORMAT_SHOW_TIME)
                else if (System.currentTimeMillis() - e.at < DateUtils.MINUTE_IN_MILLIS) ctx.getString(R.string.just_now)
                else DateUtils.getRelativeTimeSpanString(e.at, System.currentTimeMillis(), DateUtils.MINUTE_IN_MILLIS).toString()
            h.detail?.text = Navigation.label(e.url) + "  ·  " + time
        }
    }
}
