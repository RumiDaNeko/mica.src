package app.mica

import android.animation.ValueAnimator
import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.text.TextPaint
import android.text.TextUtils
import android.view.GestureDetector
import android.view.HapticFeedbackConstants
import android.view.MotionEvent
import android.view.View
import android.view.animation.DecelerateInterpolator
import kotlin.math.abs

/**
 * Desktop's status island, as the only chrome while browsing: a frosted pill with the space's dot, the
 * page's host, a dot per tab and the loading progress. Tap for Spotlight, swipe sideways for the next
 * tab, swipe up for the deck, long press for page actions, tap the space dot for the rail. It shrinks to
 * the dot while you read and widens for news ("12 blocked"). Drawn in one onDraw, no child views.
 */
class Island(context: Context, private val actions: Actions) : View(context) {
    interface Actions {
        fun onTap()
        fun onSpaceDot()
        fun onSwipe(dir: Int)
        fun onSwipeUp()
        fun onLongPress()
    }

    var label = ""; set(v) { if (field != v) { field = v; requestLayout(); invalidate() } }
    var spaceColor = 0; set(v) { field = v; invalidate() }
    var tabCount = 1; set(v) { if (field != v) { field = v; requestLayout(); invalidate() } }
    var activeIndex = 0; set(v) { field = v; invalidate() }
    var playing = false; set(v) { field = v; invalidate() }
    /** 0..100; 100 when not loading. */
    var progress = 100; set(v) { field = v; invalidate() }
    /** The page's colour, mixed lightly into the glass; 0 for none. */
    var tint = 0; set(v) { field = v; invalidate() }

    private var news: String? = null
    private val clearNews = Runnable { news = null; requestLayout(); invalidate() }
    private var shrink = 0f
    private var shrinkAnim: ValueAnimator? = null
    var shrunk = false
        private set

    private var fullW = -1f
    private var targetW = -1
    private var widthAnim: ValueAnimator? = null

    // Swiping between tabs: the label follows the finger, then the old one slides out as the new one slides in.
    private var drag = 0f
    private var outgoing: String? = null
    private var outFrom = 0f
    private var slide = 1f
    private var slideDir = 0f
    private var slideAnim: ValueAnimator? = null
    private var downX = 0f
    private var downY = 0f
    private var dragging = false
    private var velocity: android.view.VelocityTracker? = null
    private val slop = android.view.ViewConfiguration.get(context).scaledTouchSlop

    private val glass = context.col(R.color.mica_glass)
    private val line = context.col(R.color.mica_line)
    private val ink = context.col(R.color.mica_ink)
    private val muted = context.col(R.color.mica_faint)
    private val accent = context.col(R.color.mica_accent)
    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val stroke = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE; strokeWidth = context.dp(0.8f).toFloat() }
    private val textPaint = TextPaint(Paint.ANTI_ALIAS_FLAG).apply { textSize = context.dp(14).toFloat(); color = ink }
    private val rect = RectF()
    private val clip = Path()

    private val h = context.dp(44)
    private val padX = context.dp(16)
    private val dotR = context.dp(5).toFloat()
    private val tabDot = context.dp(2.5f).toFloat()
    private val tabGap = context.dp(8)
    private val maxDots = 7

    init {
        elevation = context.dp(6).toFloat()
        isClickable = true
        isFocusable = true
        contentDescription = context.getString(R.string.search_hint)
    }

    fun announce(text: String) {
        news = text
        removeCallbacks(clearNews)
        postDelayed(clearNews, 3000)
        if (shrunk) setShrunk(false)
        requestLayout(); invalidate()
    }

    fun setShrunk(value: Boolean) {
        if (shrunk == value) return
        shrunk = value
        shrinkAnim?.cancel()
        shrinkAnim = ValueAnimator.ofFloat(shrink, if (value) 1f else 0f).apply {
            duration = 220
            interpolator = DecelerateInterpolator()
            addUpdateListener { shrink = it.animatedValue as Float; requestLayout(); invalidate() }
            start()
        }
    }

    private fun shown() = news ?: label
    private fun dotsWidth() = if (tabCount <= 1) 0 else minOf(tabCount, maxDots) * tabGap + context.dp(10)

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val maxW = (MeasureSpec.getSize(widthMeasureSpec) * 0.82f).toInt()
        val textW = textPaint.measureText(shown()).toInt().coerceAtMost(maxW / 2 + context.dp(40))
        val full = (padX + dotR * 2 + context.dp(10) + textW + dotsWidth() + padX).toInt().coerceIn(context.dp(120), maxW)
        // A new label (another tab, a notice) eases the pill to its new width instead of jumping.
        if (fullW < 0f) { fullW = full.toFloat(); targetW = full }
        else if (full != targetW) {
            targetW = full
            widthAnim?.cancel()
            widthAnim = ValueAnimator.ofFloat(fullW, full.toFloat()).apply {
                duration = 280
                interpolator = DecelerateInterpolator(1.8f)
                addUpdateListener { fullW = it.animatedValue as Float; requestLayout() }
                start()
            }
        }
        val w = (fullW + (h - fullW) * shrink).toInt()
        setMeasuredDimension(w, h)
    }

    override fun onDraw(canvas: Canvas) {
        val w = width.toFloat(); val hh = height.toFloat()
        rect.set(0f, 0f, w, hh)
        val r = hh / 2
        fill.color = if (tint != 0) blend(glass, tint, 0.25f) else glass
        canvas.drawRoundRect(rect, r, r, fill)
        // Loading: a soft fill along the pill.
        if (progress in 1..99) {
            clip.reset(); clip.addRoundRect(rect, r, r, Path.Direction.CW)
            canvas.save(); canvas.clipPath(clip)
            fill.color = accent; fill.alpha = 40
            canvas.drawRect(0f, 0f, w * progress / 100f, hh, fill)
            canvas.restore()
            fill.alpha = 255
        }
        stroke.color = line
        canvas.drawRoundRect(rect.apply { inset(stroke.strokeWidth / 2, stroke.strokeWidth / 2) }, r, r, stroke)

        val cy = hh / 2
        val dotX = if (shrink > 0.5f) w / 2 else padX + dotR
        fill.color = context.spaceColor(spaceColor)
        canvas.drawCircle(dotX, cy, dotR * (1f + shrink * 0.3f), fill)
        if (playing) { stroke.color = context.spaceColor(spaceColor); canvas.drawCircle(dotX, cy, dotR * 1.9f, stroke) }
        if (shrink > 0.5f) return

        val alpha = ((1f - shrink * 2f) * 255).toInt().coerceIn(0, 255)
        val textX = padX + dotR * 2 + context.dp(10)
        val dots = dotsWidth()
        val avail = w - textX - padX - dots
        val baseline = cy - (textPaint.descent() + textPaint.ascent()) / 2
        val travel = avail * 0.55f
        canvas.save()
        canvas.clipRect(textX - context.dp(2), 0f, textX + avail + context.dp(2), hh)
        // The label leaving, toward the side it was swiped.
        outgoing?.let { old ->
            textPaint.color = ink
            textPaint.alpha = (alpha * (1f - slide)).toInt().coerceIn(0, 255)
            val x = outFrom + (slideDir * travel - outFrom) * slide
            canvas.drawText(TextUtils.ellipsize(old, textPaint, avail, TextUtils.TruncateAt.MIDDLE).toString(), textX + x, baseline, textPaint)
        }
        // The current label: coming in from the other side, or following the finger.
        val inX = if (outgoing != null) -slideDir * travel * (1f - slide) else drag
        val fade = if (outgoing != null) slide else 1f - (abs(drag) / travel).coerceIn(0f, 0.7f)
        textPaint.color = if (news != null) accent else ink
        textPaint.alpha = (alpha * fade).toInt().coerceIn(0, 255)
        val t = TextUtils.ellipsize(shown(), textPaint, avail, TextUtils.TruncateAt.MIDDLE).toString()
        canvas.drawText(t, textX + inX, baseline, textPaint)
        canvas.restore()

        if (dots > 0) {
            val n = minOf(tabCount, maxDots)
            var x = w - padX - (n - 1) * tabGap - tabDot
            val activeDot = if (tabCount <= maxDots) activeIndex else (activeIndex * (maxDots - 1) / (tabCount - 1).coerceAtLeast(1))
            for (i in 0 until n) {
                fill.color = if (i == activeDot) accent else muted
                fill.alpha = alpha
                canvas.drawCircle(x, cy, if (i == activeDot) tabDot * 1.4f else tabDot, fill)
                x += tabGap
            }
            fill.alpha = 255
        }
    }

    private val gestures = GestureDetector(context, object : GestureDetector.SimpleOnGestureListener() {
        override fun onDown(e: MotionEvent) = true
        override fun onSingleTapUp(e: MotionEvent): Boolean {
            if (shrunk) { setShrunk(false); return true }
            if (e.x < padX + dotR * 2 + context.dp(8)) actions.onSpaceDot() else actions.onTap()
            return true
        }
        override fun onLongPress(e: MotionEvent) { performHapticFeedback(HapticFeedbackConstants.LONG_PRESS); actions.onLongPress() }
        override fun onFling(e1: MotionEvent?, e2: MotionEvent, vx: Float, vy: Float): Boolean {
            val dx = e2.x - (e1?.x ?: e2.x); val dy = e2.y - (e1?.y ?: e2.y)
            if (!dragging && abs(dy) > abs(dx) && dy < -context.dp(24)) { actions.onSwipeUp(); return true }
            return false
        }
    })

    @SuppressLint("ClickableViewAccessibility")
    override fun onTouchEvent(event: MotionEvent): Boolean {
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                downX = event.rawX; downY = event.rawY; dragging = false
                velocity?.recycle(); velocity = android.view.VelocityTracker.obtain()
                slideAnim?.cancel(); finishSlide()
            }
            MotionEvent.ACTION_MOVE -> {
                val dx = event.rawX - downX
                if (!dragging && !shrunk && abs(dx) > slop && abs(dx) > abs(event.rawY - downY)) {
                    dragging = true
                    // A sideways drag is not a tap or a long press.
                    val cancel = MotionEvent.obtain(event).apply { action = MotionEvent.ACTION_CANCEL }
                    gestures.onTouchEvent(cancel); cancel.recycle()
                }
                if (dragging) { drag = dx * 0.6f; invalidate() }
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> if (dragging) {
                velocity?.addMovement(event); velocity?.computeCurrentVelocity(1000)
                val vx = velocity?.xVelocity ?: 0f
                val dx = event.rawX - downX
                dragging = false
                velocity?.recycle(); velocity = null
                if (event.actionMasked == MotionEvent.ACTION_UP && (abs(dx) > context.dp(48) || (abs(dx) > context.dp(16) && abs(vx) > 900))) swiped(if (dx < 0) 1 else -1)
                else springBack()
                return true
            }
        }
        velocity?.addMovement(event)
        return if (dragging) true else gestures.onTouchEvent(event) || super.onTouchEvent(event)
    }

    /** dir 1: next tab (the label goes left), -1: previous. */
    private fun swiped(dir: Int) {
        val oldText = shown(); val oldIndex = activeIndex; val from = drag
        actions.onSwipe(dir)
        if (activeIndex == oldIndex && shown() == oldText) { springBack(); return }
        performHapticFeedback(HapticFeedbackConstants.CLOCK_TICK)
        outgoing = oldText; outFrom = from; slideDir = if (dir > 0) -1f else 1f; drag = 0f
        slideAnim?.cancel()
        slideAnim = ValueAnimator.ofFloat(0f, 1f).apply {
            duration = 280
            interpolator = DecelerateInterpolator(1.8f)
            addUpdateListener { slide = it.animatedValue as Float; invalidate() }
            addListener(object : android.animation.AnimatorListenerAdapter() { override fun onAnimationEnd(a: android.animation.Animator) = finishSlide() })
            start()
        }
    }

    private fun finishSlide() { outgoing = null; slide = 1f; invalidate() }

    /** Not far enough, or no tab that way: the label settles back with a little give. */
    private fun springBack() {
        val from = drag
        slideAnim?.cancel()
        slideAnim = ValueAnimator.ofFloat(1f, 0f).apply {
            duration = 260
            interpolator = android.view.animation.OvershootInterpolator(2.2f)
            addUpdateListener { drag = from * (it.animatedValue as Float); invalidate() }
            start()
        }
    }

    override fun performClick(): Boolean { super.performClick(); actions.onTap(); return true }
}
