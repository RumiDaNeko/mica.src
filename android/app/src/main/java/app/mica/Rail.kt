package app.mica

import android.content.Context
import android.graphics.Color
import android.text.TextUtils
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.GridLayout
import android.widget.LinearLayout
import android.widget.PopupMenu
import android.widget.TextView
import androidx.recyclerview.widget.ItemTouchHelper
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView

/**
 * Desktop's sidebar as a drawer from the left edge: spaces, pinned tabs, the space's tabs, a new tab.
 * Swipe a row to close it; long press for pin, move to space and close.
 */
class Rail(private val activity: MainActivity) {
    private val ctx: Context = activity
    private val browser get() = activity.browser
    val view = LinearLayout(ctx)
    private val spacesRow = LinearLayout(ctx)
    private val spaceTitle = ctx.text("", 20f, bold = true)
    private val pinned = GridLayout(ctx)
    private val pinnedHeader = header(R.string.pinned)
    private val list = RecyclerView(ctx)
    private var items: List<Tab> = emptyList()

    private val palette = PALETTE

    init {
        view.orientation = LinearLayout.VERTICAL
        view.setBackgroundColor(ctx.col(R.color.mica_panel))
        view.isClickable = true
        view.pad(14, 0)

        view.addView(spacesRow, lp(MATCH, WRAP).apply { topMargin = ctx.dp(12) })
        spaceTitle.setOnLongClickListener { editSpace(browser.activeSpace); true }
        view.addView(spaceTitle, lp(MATCH, WRAP).apply { topMargin = ctx.dp(18); marginStart = ctx.dp(4) })
        view.addView(pinnedHeader)
        pinned.columnCount = 3
        view.addView(pinned, lp(MATCH, WRAP))
        view.addView(header(R.string.tabs_header))

        list.layoutManager = LinearLayoutManager(ctx)
        list.adapter = Adapter()
        ItemTouchHelper(object : ItemTouchHelper.SimpleCallback(0, ItemTouchHelper.LEFT or ItemTouchHelper.RIGHT) {
            override fun onMove(rv: RecyclerView, a: RecyclerView.ViewHolder, b: RecyclerView.ViewHolder) = false
            override fun onSwiped(holder: RecyclerView.ViewHolder, direction: Int) {
                items.getOrNull(holder.bindingAdapterPosition)?.let { browser.close(it) }
                refresh()
            }
        }).attachToRecyclerView(list)
        view.addView(list, lp(MATCH, 0, 1f))

        val add = rowItem("+", ctx.getString(R.string.new_tab), ctx.col(R.color.mica_accent)) { activity.closeRail(); activity.newTab() }
        view.addView(add)
        if (browser.privateSupported) view.addView(rowItem("◐", ctx.getString(R.string.new_private_tab), ctx.col(R.color.mica_muted)) { activity.newPrivateTab() })
        val divider = View(ctx).apply { setBackgroundColor(ctx.col(R.color.mica_line)) }
        view.addView(divider, lp(MATCH, ctx.dp(1)).apply { topMargin = ctx.dp(6); bottomMargin = ctx.dp(6) })
        view.addView(rowItem("◆", ctx.getString(R.string.games), ctx.col(R.color.mica_muted)) { activity.closeRail(); activity.navigateActive("mica://games") })
        view.addView(rowItem("▤", ctx.getString(R.string.library), ctx.col(R.color.mica_muted)) { activity.openLibrary() })
        view.addView(rowItem("❏", ctx.getString(R.string.sessions), ctx.col(R.color.mica_muted)) { activity.sessionsMenu() })
        view.addView(rowItem("⚿", ctx.getString(R.string.passwords_sync), ctx.col(R.color.mica_muted)) { activity.closeRail(); activity.openPasswords() })
        view.addView(rowItem("☰", ctx.getString(R.string.settings), ctx.col(R.color.mica_muted)) { activity.closeRail(); activity.openSettings() })
    }

    fun setInsets(top: Int, bottom: Int) {
        view.setPadding(ctx.dp(14), top, ctx.dp(14), bottom + ctx.dp(8))
    }

    private fun header(text: Int): TextView = ctx.text(ctx.getString(text).uppercase(), 11f, ctx.col(R.color.mica_faint)).apply {
        letterSpacing = 0.08f
        layoutParams = lp(MATCH, WRAP).apply { topMargin = ctx.dp(16); bottomMargin = ctx.dp(6); marginStart = ctx.dp(4) }
    }

    private fun rowItem(glyph: String, label: String, glyphColor: Int, run: () -> Unit): View {
        val g = ctx.text(glyph, 16f, glyphColor).apply { gravity = Gravity.CENTER }
        val r = ctx.row(g, ctx.text(label, 15f))
        g.layoutParams = lp(ctx.dp(24), WRAP).apply { marginEnd = ctx.dp(10) }
        r.pad(8, 11)
        r.background = ctx.pressable(0, 12)
        r.setOnClickListener { run() }
        return r
    }

    fun refresh() {
        val space = browser.activeSpace
        spacesRow.removeAllViews()
        for (s in browser.spaces) {
            val on = s.id == space.id
            val chip = ctx.text(browser.spaceName(s).take(1).uppercase(), 15f, if (on) Color.WHITE else ctx.spaceColor(s.color), bold = true).apply {
                gravity = Gravity.CENTER
                background = ctx.pressable(if (on) s.color else ctx.col(R.color.mica_soft), 11, if (on) 0 else ctx.col(R.color.mica_line))
                contentDescription = browser.spaceName(s)
                setOnClickListener { browser.switchSpace(s.id); refresh() }
                setOnLongClickListener { editSpace(s); true }
            }
            spacesRow.addView(chip, lp(ctx.dp(40), ctx.dp(40)).apply { marginEnd = ctx.dp(8) })
        }
        val plus = ctx.text("+", 18f, ctx.col(R.color.mica_muted)).apply {
            gravity = Gravity.CENTER
            background = ctx.pressable(0, 11, ctx.col(R.color.mica_line))
            contentDescription = ctx.getString(R.string.new_space)
            setOnClickListener { editSpace(null) }
        }
        spacesRow.addView(plus, lp(ctx.dp(40), ctx.dp(40)))
        spaceTitle.text = browser.spaceName(space)
        spaceTitle.setTextColor(ctx.spaceColor(space.color))

        val tabs = browser.tabsIn()
        val pins = tabs.filter { it.pinned }.take(6)
        pinned.removeAllViews()
        pinnedHeader.visibility = if (pins.isEmpty()) View.GONE else View.VISIBLE
        pinned.visibility = pinnedHeader.visibility
        val cell = (ctx.resources.displayMetrics.widthPixels.coerceAtMost(ctx.dp(300)) - ctx.dp(28 + 16)) / 3
        for (t in pins) {
            val tile = ctx.text(t.title.ifBlank { Navigation.label(t.url) }, 11f).apply {
                gravity = Gravity.CENTER
                maxLines = 2; ellipsize = TextUtils.TruncateAt.END
                pad(6, 6)
                background = ctx.pressable(ctx.col(if (t === browser.active) R.color.mica_card else R.color.mica_soft), 12, if (t === browser.active) ctx.col(R.color.mica_accent) else 0)
                setOnClickListener { activity.closeRail(); browser.activate(t) }
                setOnLongClickListener { activity.tabMenu(t); true }
            }
            pinned.addView(tile, GridLayout.LayoutParams().apply { width = cell; height = ctx.dp(52); setMargins(0, 0, ctx.dp(8), ctx.dp(8)) })
        }
        items = tabs.filter { !it.pinned }
        @Suppress("NotifyDataSetChanged")
        list.adapter?.notifyDataSetChanged()
    }

    /** New space (null) or rename/recolour/delete an existing one. */
    private fun editSpace(space: Space?) {
        val name = ctx.field(ctx.getString(R.string.space_name), space?.let { browser.spaceName(it) } ?: "")
        var color = space?.color ?: palette[browser.spaces.size % palette.size]
        val swatches = ctx.row()
        fun paint() { for (i in 0 until swatches.childCount) swatches.getChildAt(i).let { it.background = ctx.rounded(palette[i], 15, if (palette[i] == color) ctx.col(R.color.mica_ink) else 0, 2) } }
        palette.forEach { c -> swatches.addView(View(ctx).apply { setOnClickListener { color = c; paint() } }, lp(ctx.dp(30), ctx.dp(30)).apply { marginEnd = ctx.dp(10) }) }
        paint()
        val actions = ArrayList<CardAction>()
        if (space != null && browser.spaces.size > 1) actions.add(CardAction(ctx.getString(R.string.delete)) { browser.deleteSpace(space); refresh() })
        actions.add(CardAction(ctx.getString(R.string.cancel)) {})
        actions.add(CardAction(ctx.getString(R.string.save), true) {
            val n = name.text.toString().trim()
            if (space == null) { if (n.isNotEmpty()) browser.addSpace(n, color) }
            else { if (n.isNotEmpty() && n != browser.spaceName(space)) space.name = n; space.color = color; browser.saveLater() }
            refresh(); activity.onTabsChanged()
        })
        Sheet.show(activity, ctx.getString(if (space == null) R.string.new_space else R.string.rename_space), null, ctx.form(name, swatches), actions)
    }

    private inner class Holder(val row: LinearLayout, val dot: View, val title: TextView, val note: TextView) : RecyclerView.ViewHolder(row)

    private inner class Adapter : RecyclerView.Adapter<Holder>() {
        override fun getItemCount() = items.size
        override fun onCreateViewHolder(parent: ViewGroup, type: Int): Holder {
            val dot = View(ctx)
            val title = ctx.text("", 15f).apply { maxLines = 1; ellipsize = TextUtils.TruncateAt.END }
            val note = ctx.text("", 12f, ctx.col(R.color.mica_accent))
            val row = ctx.row(dot, title, note)
            dot.layoutParams = lp(ctx.dp(8), ctx.dp(8)).apply { marginEnd = ctx.dp(12); marginStart = ctx.dp(4) }
            title.layoutParams = lp(0, WRAP, 1f)
            row.pad(8, 12)
            row.layoutParams = RecyclerView.LayoutParams(RecyclerView.LayoutParams.MATCH_PARENT, RecyclerView.LayoutParams.WRAP_CONTENT)
            val h = Holder(row, dot, title, note)
            row.setOnClickListener { items.getOrNull(h.bindingAdapterPosition)?.let { activity.closeRail(); browser.activate(it) } }
            row.setOnLongClickListener { items.getOrNull(h.bindingAdapterPosition)?.let { activity.tabMenu(it) }; true }
            return h
        }

        override fun onBindViewHolder(h: Holder, position: Int) {
            val t = items[position]
            val on = t === browser.active
            val resting = t.view == null && !t.isHome && !on
            h.title.text = t.title.ifBlank { if (t.isHome) ctx.getString(R.string.new_tab) else Navigation.label(t.url) }
            h.title.setTextColor(ctx.col(if (resting) R.color.mica_faint else R.color.mica_ink))
            h.title.setTypeface(null, if (resting) android.graphics.Typeface.ITALIC else android.graphics.Typeface.NORMAL)
            h.note.text = if (t.playing) "♪" else ""
            h.dot.background = ctx.rounded(if (on) ctx.spaceColor(browser.activeSpace.color) else ctx.col(R.color.mica_line), 4)
            h.row.background = ctx.pressable(if (on) ctx.col(R.color.mica_card) else 0, 12)
        }
    }
}
