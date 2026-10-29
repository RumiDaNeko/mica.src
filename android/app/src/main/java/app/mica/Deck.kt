package app.mica

import android.content.Context
import android.graphics.Rect
import android.text.TextUtils
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.HorizontalScrollView
import android.widget.ImageView
import android.widget.LinearLayout
import androidx.recyclerview.widget.ItemTouchHelper
import androidx.recyclerview.widget.LinearLayoutManager
import androidx.recyclerview.widget.RecyclerView

/**
 * The tab switcher: a space's tabs as a fanned stack of cards (no live thumbnails, to save memory). Tap a
 * card to open it, swipe it sideways to close it, hold it for its menu; the chips on top change space.
 * Tab groups sit under a header (tap folds them away, hold for the group's menu).
 */
class Deck(private val activity: MainActivity) {
    private val ctx: Context = activity
    private val browser get() = activity.browser
    val view = FrameLayout(ctx)
    private val chips = LinearLayout(ctx)
    private val count = ctx.text("", 13f, ctx.col(R.color.mica_muted))
    private val list = RecyclerView(ctx)
    private var items: List<Tab> = emptyList()
    /** What the list shows: group headers (TabGroup) and cards (Tab). */
    private var rows: List<Any> = emptyList()
    private val buttons = LinearLayout(ctx)
    val showing: Boolean get() = view.visibility == View.VISIBLE && !morph.closing
    private lateinit var morph: IslandMorph
    private val cardH = ctx.dp(190)
    private val peek = ctx.dp(78)

    init {
        view.visibility = View.GONE
        view.setBackgroundColor(ctx.col(R.color.mica_canvas))
        val top = ctx.column()
        val hs = HorizontalScrollView(ctx).apply { isHorizontalScrollBarEnabled = false; addView(chips) }
        top.addView(hs, lp(MATCH, WRAP))
        top.addView(count, lp(WRAP, WRAP).apply { topMargin = ctx.dp(10); marginStart = ctx.dp(4) })
        top.pad(16, 12)

        list.layoutManager = LinearLayoutManager(ctx)
        list.adapter = Adapter()
        list.clipToPadding = false
        list.setPadding(ctx.dp(16), ctx.dp(8), ctx.dp(16), ctx.dp(96))
        list.addItemDecoration(object : RecyclerView.ItemDecoration() {
            override fun getItemOffsets(out: Rect, v: View, parent: RecyclerView, state: RecyclerView.State) {
                // Cards overlap the one above; a group header doesn't, nor does the first card under it.
                val p = parent.getChildAdapterPosition(v)
                if (p > 0 && rows.getOrNull(p) is Tab && rows.getOrNull(p - 1) is Tab) out.top = -(cardH - peek)
            }
        })
        ItemTouchHelper(object : ItemTouchHelper.SimpleCallback(0, ItemTouchHelper.LEFT or ItemTouchHelper.RIGHT) {
            override fun onMove(rv: RecyclerView, a: RecyclerView.ViewHolder, b: RecyclerView.ViewHolder) = false
            override fun getSwipeDirs(rv: RecyclerView, holder: RecyclerView.ViewHolder) = if (holder is Holder) super.getSwipeDirs(rv, holder) else 0
            override fun onSwiped(holder: RecyclerView.ViewHolder, direction: Int) {
                val tab = rows.getOrNull(holder.bindingAdapterPosition) as? Tab ?: return
                browser.close(tab)
                refresh()
            }
        }).attachToRecyclerView(list)

        val column = ctx.column()
        column.addView(top, lp(MATCH, WRAP))
        column.addView(list, lp(MATCH, 0, 1f))
        view.addView(column, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))

        buttons.orientation = LinearLayout.HORIZONTAL
        buttons.gravity = Gravity.CENTER
        view.addView(buttons, FrameLayout.LayoutParams(FrameLayout.LayoutParams.WRAP_CONTENT, FrameLayout.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM or Gravity.CENTER_HORIZONTAL).apply { bottomMargin = ctx.dp(24) })
        view.isClickable = true
        morph = IslandMorph(view, activity.island, 0f) { activity.islandBusy() }
    }

    fun setInsets(top: Int, bottom: Int) {
        view.setPadding(0, top, 0, 0)
        (view.getChildAt(1).layoutParams as FrameLayout.LayoutParams).bottomMargin = bottom + ctx.dp(20)
    }

    fun show() { if (!showing) { view.visibility = View.VISIBLE; morph.open() }; refresh(); rows.indexOfFirst { it === browser.active }.takeIf { it >= 0 }?.let { list.scrollToPosition(it) } }
    fun hide() { if (showing) morph.close { view.visibility = View.GONE; activity.onPanelClosed() } }
    fun cut() { if (view.visibility == View.VISIBLE) { morph.cut(); view.visibility = View.GONE; activity.onPanelClosed() } }

    fun refresh() {
        if (!showing) return
        items = browser.tabsIn()
        count.text = ctx.getString(R.string.tabs_count, items.size)
        val out = ArrayList<Any>()
        var current: String? = null
        for (t in items) {
            val g = browser.group(t.group)
            if (g != null && g.id != current) out.add(g)
            current = g?.id
            if (g == null || !g.collapsed) out.add(t)
        }
        rows = out
        renderButtons()
        chips.removeAllViews()
        for (space in browser.spaces) {
            val on = space.id == browser.activeSpaceId
            val dot = View(ctx).apply { background = ctx.rounded(ctx.spaceColor(space.color), 5) }
            val chip = ctx.row(dot, ctx.text(browser.spaceName(space), 14f, ctx.col(if (on) R.color.mica_ink else R.color.mica_muted), bold = on))
            dot.layoutParams = lp(ctx.dp(10), ctx.dp(10)).apply { marginEnd = ctx.dp(8) }
            chip.pad(14, 9)
            chip.background = ctx.pressable(ctx.col(if (on) R.color.mica_soft else R.color.mica_canvas), 18, if (on) ctx.col(R.color.mica_line) else 0)
            chip.setOnClickListener { browser.switchSpace(space.id); refresh() }
            chips.addView(chip, lp(WRAP, WRAP).apply { marginEnd = ctx.dp(8) })
        }
        @Suppress("NotifyDataSetChanged")
        list.adapter?.notifyDataSetChanged()
    }

    private fun button(label: String, filled: Boolean, run: () -> Unit) = ctx.text(label, 15f, ctx.col(if (filled) R.color.mica_canvas else R.color.mica_accent)).apply {
        gravity = Gravity.CENTER
        pad(22, 13)
        background = ctx.pressable(ctx.col(if (filled) R.color.mica_accent else R.color.mica_card), 24, if (filled) 0 else ctx.col(R.color.mica_line))
        elevation = ctx.dp(6).toFloat()
        setOnClickListener { run() }
    }

    private fun renderButtons() {
        buttons.removeAllViews()
        val inPrivate = browser.activeSpaceId == Browser.PRIVATE
        buttons.addView(button("+  " + ctx.getString(if (inPrivate) R.string.new_private_tab else R.string.new_tab), true) { hide(); activity.newTab() })
        val second = when {
            inPrivate -> button(ctx.getString(R.string.close_private_tabs), false) { hide(); browser.closePrivate() }
            browser.privateSupported -> button("◐  " + ctx.getString(R.string.private_tabs), false) { hide(); activity.newPrivateTab() }
            else -> null
        }
        second?.let { buttons.addView(it, lp(WRAP, WRAP).apply { marginStart = ctx.dp(10) }) }
    }

    private inner class HeaderHolder(val box: LinearLayout, val dot: View, val name: android.widget.TextView, val info: android.widget.TextView) : RecyclerView.ViewHolder(box)

    private inner class Holder(val card: LinearLayout, val icon: ImageView, val title: android.widget.TextView, val host: android.widget.TextView,
                               val status: android.widget.TextView, val swatch: View) : RecyclerView.ViewHolder(card)

    private inner class Adapter : RecyclerView.Adapter<RecyclerView.ViewHolder>() {
        override fun getItemCount() = rows.size
        override fun getItemViewType(position: Int) = if (rows[position] is TabGroup) 0 else 1
        override fun onCreateViewHolder(parent: ViewGroup, type: Int): RecyclerView.ViewHolder {
            if (type == 0) {
                val dot = View(ctx)
                val name = ctx.text("", 15f, bold = true)
                val info = ctx.text("", 13f, ctx.col(R.color.mica_muted))
                val box = ctx.row(dot, name, info)
                dot.layoutParams = lp(ctx.dp(12), ctx.dp(12)).apply { marginEnd = ctx.dp(10) }
                name.layoutParams = lp(0, WRAP, 1f)
                box.pad(14, 12)
                box.background = ctx.pressable(0, 16)
                box.layoutParams = RecyclerView.LayoutParams(RecyclerView.LayoutParams.MATCH_PARENT, RecyclerView.LayoutParams.WRAP_CONTENT).apply { topMargin = ctx.dp(10); bottomMargin = ctx.dp(4) }
                val h = HeaderHolder(box, dot, name, info)
                box.setOnClickListener { (rows.getOrNull(h.bindingAdapterPosition) as? TabGroup)?.let { browser.setCollapsed(it, !it.collapsed); refresh() } }
                box.setOnLongClickListener { (rows.getOrNull(h.bindingAdapterPosition) as? TabGroup)?.let { activity.groupMenu(it) }; true }
                return h
            }
            val icon = ImageView(ctx)
            val title = ctx.text("", 15f, bold = true).apply { maxLines = 1; ellipsize = TextUtils.TruncateAt.END }
            val close = ctx.text("✕", 15f, ctx.col(R.color.mica_faint)).apply { pad(8, 4); contentDescription = ctx.getString(R.string.close_tab) }
            val head = ctx.row(icon, title, close)
            icon.layoutParams = lp(ctx.dp(18), ctx.dp(18)).apply { marginEnd = ctx.dp(10) }
            title.layoutParams = lp(0, WRAP, 1f)
            val host = ctx.text("", 12f, ctx.col(R.color.mica_muted)).apply { maxLines = 1 }
            val status = ctx.text("", 12f, ctx.col(R.color.mica_accent))
            val swatch = View(ctx)
            val card = ctx.column(head, host, status, swatch)
            (host.layoutParams as LinearLayout.LayoutParams).apply { topMargin = ctx.dp(4); marginStart = ctx.dp(28) }
            (status.layoutParams as LinearLayout.LayoutParams).apply { topMargin = ctx.dp(2); marginStart = ctx.dp(28) }
            swatch.layoutParams = lp(MATCH, 0, 1f).apply { topMargin = ctx.dp(12) }
            card.pad(14, 12)
            card.layoutParams = RecyclerView.LayoutParams(RecyclerView.LayoutParams.MATCH_PARENT, cardH)
            card.elevation = ctx.dp(3).toFloat()
            val h = Holder(card, icon, title, host, status, swatch)
            card.setOnClickListener { (rows.getOrNull(h.bindingAdapterPosition) as? Tab)?.let { hide(); browser.activate(it) } }
            card.setOnLongClickListener { (rows.getOrNull(h.bindingAdapterPosition) as? Tab)?.let { activity.tabMenu(it) }; true }
            close.setOnClickListener { (rows.getOrNull(h.bindingAdapterPosition) as? Tab)?.let { browser.close(it); refresh() } }
            return h
        }

        override fun onBindViewHolder(holder: RecyclerView.ViewHolder, position: Int) {
            if (holder is HeaderHolder) {
                val g = rows[position] as TabGroup
                holder.dot.background = ctx.rounded(ctx.spaceColor(g.color), 6)
                holder.name.text = g.name.ifBlank { ctx.getString(R.string.tab_group) }
                holder.info.text = ctx.resources.getQuantityString(R.plurals.group_tabs, browser.members(g).size, browser.members(g).size) + if (g.collapsed) "  ▸" else "  ▾"
                return
            }
            val h = holder as Holder
            val tab = rows[position] as Tab
            val activeTab = tab === browser.active
            // A tab in a group is edged in the group's colour.
            val group = browser.group(tab.group)
            val edge = when { activeTab -> ctx.col(R.color.mica_accent); group != null -> ctx.spaceColor(group.color); else -> ctx.col(R.color.mica_line) }
            h.card.background = ctx.rounded(ctx.col(R.color.mica_card), 20, edge, if (activeTab || group != null) 1.5f else 0.75f)
            h.title.text = tab.title.ifBlank { if (tab.isHome) ctx.getString(R.string.new_tab) else Navigation.label(tab.url) }
            h.host.text = if (tab.isHome) "" else Navigation.label(tab.url)
            h.status.text = when {
                tab.playing -> "♪ " + ctx.getString(R.string.playing)
                tab.view == null && !tab.isHome -> ctx.getString(R.string.resting).lowercase().replaceFirstChar { it.uppercase() }
                tab.pinned -> ctx.getString(R.string.pinned).lowercase().replaceFirstChar { it.uppercase() }
                else -> ""
            }
            h.icon.setImageBitmap(tab.icon)
            h.icon.background = if (tab.icon == null) ctx.rounded(ctx.spaceColor(browser.activeSpace.color), 9) else null
            h.swatch.background = ctx.rounded(if (tab.tint != 0) blend(ctx.col(R.color.mica_soft), tab.tint, 0.55f) else ctx.col(R.color.mica_soft), 12)
            // The front three cards fan out a little.
            val fromEnd = rows.size - 1 - position
            h.card.pivotX = h.card.resources.displayMetrics.widthPixels / 2f
            h.card.pivotY = cardH.toFloat()
            h.card.rotation = when (fromEnd) { 1 -> 1.6f; 2 -> -1.8f; else -> 0f }
        }
    }
}
