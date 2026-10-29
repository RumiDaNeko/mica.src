package app.mica

import android.content.Context
import android.graphics.Typeface
import android.view.Gravity
import android.view.View
import android.widget.FrameLayout
import android.widget.HorizontalScrollView
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import java.text.DateFormat
import java.util.Calendar
import java.util.Date

/**
 * The new tab page (desktop's RenderHome): the time, a greeting, the sites you visit most and the tabs you
 * closed last. Tap anywhere else for Spotlight. In private browsing it only says where you are.
 */
class Home(private val activity: MainActivity) {
    private val ctx: Context = activity
    private val browser get() = activity.browser
    val view = FrameLayout(ctx)
    private val column = ctx.column().apply { gravity = Gravity.CENTER_HORIZONTAL }
    private val time = ctx.text("", 56f, ctx.col(R.color.mica_ink)).apply { typeface = Typeface.create(Typeface.SERIF, Typeface.NORMAL); gravity = Gravity.CENTER }
    private val date = ctx.text("", 14f, ctx.col(R.color.mica_muted)).apply { gravity = Gravity.CENTER }
    private val greeting = ctx.text("", 17f, ctx.col(R.color.mica_accent)).apply { gravity = Gravity.CENTER }
    private val tiles = LinearLayout(ctx).apply { orientation = LinearLayout.VERTICAL }
    private val closed = ctx.row()
    private val closedBox = ctx.column()
    private var private = false
    private val tick = Runnable { showTime(); scheduleTick() }

    init {
        view.setOnClickListener { activity.openSpotlight() }
        column.addView(time, lp(MATCH, WRAP))
        column.addView(date, lp(MATCH, WRAP))
        column.addView(greeting, lp(MATCH, WRAP).apply { topMargin = ctx.dp(6) })
        column.addView(tiles, lp(MATCH, WRAP).apply { topMargin = ctx.dp(28) })
        val head = ctx.text(ctx.getString(R.string.recently_closed).uppercase(), 11f, ctx.col(R.color.mica_faint)).apply { letterSpacing = 0.08f }
        closedBox.addView(head, lp(WRAP, WRAP).apply { marginStart = ctx.dp(4) })
        closedBox.addView(HorizontalScrollView(ctx).apply { isHorizontalScrollBarEnabled = false; addView(closed) }, lp(MATCH, WRAP).apply { topMargin = ctx.dp(8) })
        column.addView(closedBox, lp(MATCH, WRAP).apply { topMargin = ctx.dp(24) })
        column.setPadding(ctx.dp(20), 0, ctx.dp(20), 0)
        view.addView(column, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.WRAP_CONTENT, Gravity.CENTER_VERTICAL).apply { bottomMargin = ctx.dp(60) })
        view.addOnAttachStateChangeListener(object : View.OnAttachStateChangeListener {
            override fun onViewAttachedToWindow(v: View) = scheduleTick()
            override fun onViewDetachedFromWindow(v: View) { view.removeCallbacks(tick) }
        })
    }

    /** Fills it in again (on showing a new tab, after a change of space, a visit or a removal). */
    fun render(isPrivate: Boolean) {
        private = isPrivate
        showTime()
        tiles.removeAllViews()
        closed.removeAllViews()
        var delay = 0L
        fun rise(v: View) {
            // Each piece settles in a beat after the one before.
            v.alpha = 0f; v.translationY = ctx.dp(10).toFloat()
            v.animate().alpha(1f).translationY(0f).setStartDelay(delay).setDuration(320).setInterpolator(android.view.animation.PathInterpolator(0.2f, 0.9f, 0.25f, 1f)).start()
            delay += 35
        }
        listOf(time, date, greeting).forEach { rise(it) }
        if (isPrivate) {
            tiles.addView(ctx.text(ctx.getString(R.string.private_top_sites), 14f, ctx.col(R.color.mica_muted)).apply { gravity = Gravity.CENTER }, lp(MATCH, WRAP))
            closedBox.visibility = View.GONE
            return
        }
        val top = browser.topSites(8)
        if (top.isEmpty()) tiles.addView(ctx.text(ctx.getString(R.string.top_sites_empty), 14f, ctx.col(R.color.mica_muted)).apply { gravity = Gravity.CENTER }, lp(MATCH, WRAP))
        val perRow = if (ctx.resources.configuration.screenWidthDp >= 600) 8 else 4
        top.chunked(perRow).forEach { chunk ->
            val r = ctx.row(gravity = Gravity.TOP)
            chunk.forEach { e -> r.addView(tile(e).also { rise(it) }, lp(0, WRAP, 1f)) }
            repeat(perRow - chunk.size) { r.addView(View(ctx), lp(0, 1, 1f)) }
            tiles.addView(r, lp(MATCH, WRAP).apply { bottomMargin = ctx.dp(14) })
        }
        val recent = browser.recentlyClosed.filter { Navigation.isWeb(it.url) }.take(3)
        closedBox.visibility = if (recent.isEmpty()) View.GONE else View.VISIBLE
        recent.forEach { e ->
            val chip = ctx.text(e.title.ifBlank { Navigation.label(e.url) }, 13f).apply {
                maxLines = 1; ellipsize = android.text.TextUtils.TruncateAt.END; maxWidth = ctx.dp(190)
                pad(12, 8); background = ctx.pressable(ctx.col(R.color.mica_card), 16, ctx.col(R.color.mica_line))
                setOnClickListener { activity.navigateActive(e.url) }
            }
            closed.addView(chip, lp(WRAP, WRAP).apply { marginEnd = ctx.dp(8) })
            rise(chip)
        }
    }

    private fun tile(e: HistoryEntry): View {
        val host = Navigation.label(e.url).removePrefix("www.")
        val name = app.mica.blocker.Domains.registrable(host).substringBefore('.').replaceFirstChar { it.uppercase() }
        val icon = browser.tabs.firstOrNull { it.icon != null && it.pageHost?.removePrefix("www.") == host }?.icon
        val color = PALETTE[Math.floorMod(host.hashCode(), PALETTE.size)]
        val face: View = if (icon != null) ImageView(ctx).apply { setImageBitmap(icon); scaleType = ImageView.ScaleType.CENTER_INSIDE; setPadding(ctx.dp(14), ctx.dp(14), ctx.dp(14), ctx.dp(14)) }
            else ctx.text(name.take(1), 20f, color, bold = true).apply { gravity = Gravity.CENTER }
        face.background = ctx.rounded(ctx.col(R.color.mica_card), 16, ctx.col(R.color.mica_line))
        val label = ctx.text(name, 12f, ctx.col(R.color.mica_muted)).apply { gravity = Gravity.CENTER; maxLines = 1; ellipsize = android.text.TextUtils.TruncateAt.END }
        val cell = ctx.column(face, label).apply { gravity = Gravity.CENTER_HORIZONTAL; pad(4, 6); background = ctx.pressable(0, 14); contentDescription = ctx.getString(R.string.open_site_named, host) }
        face.layoutParams = lp(ctx.dp(54), ctx.dp(54))
        (label.layoutParams as LinearLayout.LayoutParams).topMargin = ctx.dp(6)
        cell.setOnClickListener { activity.navigateActive(e.url) }
        cell.setOnLongClickListener {
            var sheet: Sheet? = null
            sheet = Sheet.show(activity, host, e.title, ctx.choices(listOf(ctx.getString(R.string.remove_top_site) to null)) { _ ->
                sheet?.dismiss(); browser.forgetSite(e.url); render(private)
            })
            true
        }
        return cell
    }

    private fun showTime() {
        val now = Date()
        time.text = android.text.format.DateFormat.getTimeFormat(ctx).format(now)
        date.text = DateFormat.getDateInstance(DateFormat.FULL).format(now)
        if (private) { greeting.text = ctx.getString(R.string.private_greeting); return }
        val h = Calendar.getInstance().get(Calendar.HOUR_OF_DAY)
        greeting.text = ctx.getString(when { h < 5 -> R.string.good_night; h < 12 -> R.string.good_morning; h < 18 -> R.string.good_afternoon; else -> R.string.good_evening })
    }

    /** The clock moves on the minute, only while the page is showing. */
    private fun scheduleTick() {
        view.removeCallbacks(tick)
        if (!view.isAttachedToWindow || view.visibility != View.VISIBLE) return
        view.postDelayed(tick, 60_000 - System.currentTimeMillis() % 60_000 + 50)
    }

    fun onShown() = scheduleTick()
}
