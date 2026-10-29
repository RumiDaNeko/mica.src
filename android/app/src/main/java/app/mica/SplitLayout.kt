package app.mica

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.animation.ValueAnimator
import android.view.animation.DecelerateInterpolator
import android.view.animation.PathInterpolator
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup

/**
 * Split view's two panes (desktop's BrowserSplit, two tabs at most on a phone): stacked in portrait, side by
 * side in landscape, with a divider you drag. A touch in a pane makes it the active tab, outlined in accent.
 */
@SuppressLint("ViewConstructor")
class SplitLayout(context: Context, private val touched: (Int) -> Unit) : ViewGroup(context) {
    /** Share of the space the first pane gets. */
    var ratio = 0.5f
        set(v) { field = v.coerceIn(0.2f, 0.8f); requestLayout() }
    var activeIndex = 0
        set(v) {
            if (field == v) return
            field = v
            // The outline moves over with a quick fade rather than jumping.
            ValueAnimator.ofFloat(0f, 1f).apply { duration = 180; addUpdateListener { ringAlpha = it.animatedValue as Float; invalidate() } }.start()
        }
    private var ringAlpha = 1f

    /** 0: [keep]'s pane has all the room; 1: split as [ratio] says. Animated on the way in and out. */
    private var reveal = 1f
        set(v) { field = v; requestLayout(); invalidate() }
    private var keep = 0
    private var anim: ValueAnimator? = null

    /** The second pane slides in beside the first. */
    fun enter() {
        keep = 0
        reveal = 0f
        run(1f, 380, PathInterpolator(0.2f, 0.9f, 0.25f, 1f)) {}
    }

    /** The pane at [kept] grows over the other, then [end]. */
    fun leave(kept: Int, end: () -> Unit) {
        keep = kept
        run(0f, 260, DecelerateInterpolator(1.6f), end)
    }

    private fun run(to: Float, ms: Long, curve: android.animation.TimeInterpolator, end: () -> Unit) {
        anim?.cancel()
        anim = ValueAnimator.ofFloat(reveal, to).apply {
            duration = ms
            interpolator = curve
            addUpdateListener { reveal = it.animatedValue as Float }
            addListener(object : android.animation.AnimatorListenerAdapter() {
                var cancelled = false
                override fun onAnimationCancel(a: android.animation.Animator) { cancelled = true }
                override fun onAnimationEnd(a: android.animation.Animator) { if (!cancelled) end() }
            })
            start()
        }
    }

    private val gap = context.dp(10)
    private val grab = context.dp(28)
    private val handle = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = context.col(R.color.mica_faint) }
    private val ring = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeWidth = context.dp(2).toFloat(); color = context.col(R.color.mica_accent) }
    private var dragging = false
    private val vertical get() = height >= width

    init {
        setWillNotDraw(false)
        setBackgroundColor(context.col(R.color.mica_canvas))
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val w = MeasureSpec.getSize(widthMeasureSpec)
        val h = MeasureSpec.getSize(heightMeasureSpec)
        setMeasuredDimension(w, h)
        val inner = (if (h >= w) h - paddingTop - paddingBottom else w - paddingLeft - paddingRight) - gapNow
        val share = if (keep == 0) ratio + (1f - ratio) * (1f - reveal) else ratio * reveal
        val first = (inner * share).toInt().coerceIn(0, inner)
        for (i in 0 until childCount) {
            val size = if (i == 0) first else inner - first
            val c = getChildAt(i)
            if (h >= w) c.measure(MeasureSpec.makeMeasureSpec(w - paddingLeft - paddingRight, MeasureSpec.EXACTLY), MeasureSpec.makeMeasureSpec(size, MeasureSpec.EXACTLY))
            else c.measure(MeasureSpec.makeMeasureSpec(size, MeasureSpec.EXACTLY), MeasureSpec.makeMeasureSpec(h - paddingTop - paddingBottom, MeasureSpec.EXACTLY))
        }
    }

    override fun onLayout(changed: Boolean, l: Int, t: Int, r: Int, b: Int) {
        var x = paddingLeft
        var y = paddingTop
        for (i in 0 until childCount) {
            val c = getChildAt(i)
            c.layout(x, y, x + c.measuredWidth, y + c.measuredHeight)
            if (vertical) y += c.measuredHeight + gapNow else x += c.measuredWidth + gapNow
        }
    }

    private val gapNow get() = (gap * reveal).toInt()

    /** Where the divider's middle is, along the split. */
    private fun dividerAt(): Float {
        val c = getChildAt(0) ?: return 0f
        return if (vertical) c.bottom + gap / 2f else c.right + gap / 2f
    }

    override fun dispatchDraw(canvas: Canvas) {
        super.dispatchDraw(canvas)
        if (childCount < 2) return
        val d = dividerAt()
        val r = context.dp(2).toFloat()
        val shown = (reveal * 255).toInt().coerceIn(0, 255)
        handle.alpha = shown
        ring.alpha = (shown * ringAlpha).toInt()
        if (vertical) canvas.drawRoundRect(RectF(width / 2f - context.dp(18), d - r, width / 2f + context.dp(18), d + r), r, r, handle)
        else canvas.drawRoundRect(RectF(d - r, height / 2f - context.dp(18), d + r, height / 2f + context.dp(18)), r, r, handle)
        getChildAt(activeIndex)?.let { c ->
            val o = ring.strokeWidth / 2
            canvas.drawRoundRect(RectF(c.left + o, c.top + o, c.right - o, c.bottom - o), context.dp(6).toFloat(), context.dp(6).toFloat(), ring)
        }
    }

    override fun onInterceptTouchEvent(ev: MotionEvent): Boolean {
        if (ev.actionMasked == MotionEvent.ACTION_DOWN && childCount == 2 && reveal == 1f) {
            val along = if (vertical) ev.y else ev.x
            if (kotlin.math.abs(along - dividerAt()) < grab / 2) { dragging = true; return true }
            val first = getChildAt(0)
            val index = if (if (vertical) ev.y < first.bottom else ev.x < first.right) 0 else 1
            if (index != activeIndex) touched(index)
        }
        return false
    }

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(ev: MotionEvent): Boolean {
        if (!dragging) return false
        when (ev.actionMasked) {
            MotionEvent.ACTION_MOVE -> {
                val inner = (if (vertical) height - paddingTop - paddingBottom else width - paddingLeft - paddingRight) - gap
                ratio = ((if (vertical) ev.y - paddingTop else ev.x - paddingLeft) - gap / 2f) / inner
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> dragging = false
        }
        return true
    }

    fun put(first: View, second: View) {
        if (getChildAt(0) === first && getChildAt(1) === second) return
        removeAllViews()
        for (v in listOf(first, second)) { (v.parent as? ViewGroup)?.removeView(v); addView(v) }
    }
}
