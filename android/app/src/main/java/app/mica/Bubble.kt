package app.mica

import android.animation.ValueAnimator
import android.content.Context
import android.graphics.Canvas
import android.graphics.Outline
import android.view.Gravity
import android.view.View
import android.view.ViewOutlineProvider
import android.view.animation.DecelerateInterpolator
import android.view.animation.PathInterpolator
import android.widget.FrameLayout

/**
 * The island opening into a card (desktop's choice bubble): sign in with a saved login, save a password or
 * a 2FA key, a sign-in code, tabs that arrived. The pill grows into the card and shrinks back into the
 * pill when it's done. It's Mica's own view, so a page can't press its buttons. One at a time.
 */
class Bubble(private val context: Context, private val island: Island) {
    // Above the island in z, so touches reach the card first; no outline of its own, so only the card casts a shadow.
    val view = FrameLayout(context).apply {
        visibility = View.GONE; translationZ = context.dp(12).toFloat(); outlineProvider = null
        // Room around the card for its shadow, which would otherwise be cut square at the card's edges.
        val room = context.dp(SHADOW_ROOM); setPadding(room, room, room, room); clipToPadding = false
    }
    private val card = Morph()
    /** Under the card and over everything else: a tap outside a card that allows it closes the card. */
    val outside = View(context).apply {
        visibility = View.GONE; translationZ = context.dp(12).toFloat(); outlineProvider = null
        setOnClickListener { hide() }
    }
    private var content: View? = null
    private var anim: ValueAnimator? = null
    private val hideLater = Runnable { hide() }

    init { view.addView(card, FrameLayout.LayoutParams(FrameLayout.LayoutParams.WRAP_CONTENT, FrameLayout.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM or Gravity.CENTER_HORIZONTAL)) }

    val showing get() = view.visibility == View.VISIBLE
    /** True while something else has grown out of the island (Spotlight, the deck): cards wait for it to close. */
    var held: () -> Boolean = { false }
    private var waiting: (() -> Unit)? = null
    /** Called if the card on show closes without one of its actions. */
    private var dismissed: (() -> Unit)? = null
    private var pendingUnanswered: (() -> Unit)? = null

    /** The island is free again: show the card that waited, if any. */
    fun release() { waiting?.let { waiting = null; if (!held()) it() else waiting = it } }

    /** [stays]: a question that waits for its answer, so a tap outside doesn't close it. */
    fun show(title: String, detail: String?, actions: List<CardAction>, seconds: Int = 12, extra: View? = null, stays: Boolean = false, unanswered: (() -> Unit)? = null) {
        if (held()) { waiting?.let { _ -> pendingUnanswered?.invoke() }; pendingUnanswered = unanswered; waiting = { pendingUnanswered = null; show(title, detail, actions, seconds, extra, stays, unanswered) }; return }
        dismissed?.let { dismissed = null; it() }
        dismissed = unanswered
        outside.visibility = if (stays) View.GONE else View.VISIBLE
        val body = context.cardBody(title, detail, extra, actions) { a -> dismissed = null; hide(); a.run() }
        view.removeCallbacks(hideLater)
        val wasShowing = showing && anim?.isRunning != true
        content?.let { card.removeView(it) }
        content = body
        card.addView(body, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.WRAP_CONTENT))
        if (wasShowing) {
            // Already open: swap what it says without closing.
            body.alpha = 0f
            body.animate().alpha(1f).setDuration(150).start()
        } else {
            view.visibility = View.VISIBLE
            card.from(island.width, island.height)
            body.alpha = 0f
            card.progress = 0f
            card.post { morph(1f) { } }
            island.animate().cancel()
            island.alpha = 0f
            island.visibility = View.INVISIBLE
        }
        if (seconds > 0) view.postDelayed(hideLater, seconds * 1000L)
    }

    fun hide() {
        view.removeCallbacks(hideLater)
        dismissed?.let { dismissed = null; it() }
        pendingUnanswered?.let { pendingUnanswered = null; it() }
        waiting = null
        outside.visibility = View.GONE
        if (!showing) return
        view.hideKeyboard()
        card.from(island.width.takeIf { it > 0 } ?: context.dp(140), island.height.takeIf { it > 0 } ?: context.dp(44))
        morph(0f) {
            view.visibility = View.GONE
            content?.let { card.removeView(it) }; content = null
            if (!held()) { island.alpha = 1f; island.visibility = View.VISIBLE }
        }
    }

    private fun morph(to: Float, end: () -> Unit) {
        anim?.cancel()
        anim = ValueAnimator.ofFloat(card.progress, to).apply {
            duration = if (to > 0f) 340 else 240
            interpolator = if (to > 0f) PathInterpolator(0.2f, 0.9f, 0.25f, 1.08f) else DecelerateInterpolator(1.6f)
            addUpdateListener { card.progress = it.animatedValue as Float }
            addListener(object : android.animation.AnimatorListenerAdapter() {
                var cancelled = false
                override fun onAnimationCancel(a: android.animation.Animator) { cancelled = true }
                override fun onAnimationEnd(a: android.animation.Animator) { if (!cancelled) end() }
            })
            start()
        }
    }

    /** The card, clipped to a rounded rectangle that runs from the island's pill to its own full size. */
    private inner class Morph : FrameLayout(context) {
        private val glass = context.col(R.color.mica_glass)
        private val face = context.col(R.color.mica_card)
        private val radius = context.dp(24).toFloat()
        private var fromW = 0
        private var fromH = 0
        var progress = 1f
            set(v) { field = v; content?.alpha = ((v - 0.45f) / 0.55f).coerceIn(0f, 1f); invalidateOutline(); invalidate() }

        init {
            pad(20, 18)
            elevation = context.dp(10).toFloat()
            isClickable = true
            setWillNotDraw(false)
            clipToOutline = true
            outlineProvider = object : ViewOutlineProvider() {
                override fun getOutline(v: View, o: Outline) {
                    val (l, t, r, b, rad) = shape()
                    o.setRoundRect(l, t, r, b, rad)
                }
            }
        }

        fun from(w: Int, h: Int) { fromW = w; fromH = h }

        private fun shape(): Shape {
            val p = progress.coerceAtLeast(0f)
            val w = (fromW + (width - fromW) * p).toInt().coerceIn(1, width.coerceAtLeast(1))
            val h = (fromH + (height - fromH) * p).toInt().coerceIn(1, height.coerceAtLeast(1))
            val l = (width - w) / 2
            return Shape(l, height - h, l + w, height, fromH / 2f + (radius - fromH / 2f) * p.coerceAtMost(1f))
        }

        override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
            val max = minOf(MeasureSpec.getSize(widthMeasureSpec), context.dp(560))
            super.onMeasure(MeasureSpec.makeMeasureSpec(max, MeasureSpec.EXACTLY), heightMeasureSpec)
        }

        override fun onDraw(canvas: Canvas) {
            // From the island's frosted glass to the card's face, alpha included.
            canvas.drawColor(android.animation.ArgbEvaluator().evaluate(progress.coerceIn(0f, 1f), glass, face) as Int)
        }

        // While it's still opening, taps are ignored rather than landing on a button that isn't visible yet.
        override fun dispatchTouchEvent(ev: android.view.MotionEvent): Boolean = if (progress < 0.9f) true else super.dispatchTouchEvent(ev)
    }

    companion object { const val SHADOW_ROOM = 12 }

    private data class Shape(val l: Int, val t: Int, val r: Int, val b: Int, val radius: Float)

}
