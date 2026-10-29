package app.mica

import android.animation.ValueAnimator
import android.graphics.Outline
import android.view.View
import android.view.ViewGroup
import android.view.ViewOutlineProvider
import android.view.animation.DecelerateInterpolator
import android.view.animation.PathInterpolator

/**
 * Grows a panel out of the island's pill and shrinks it back in, like the bubble: the panel's outline (so its
 * clip and its shadow) runs from the pill's rectangle to its own, its contents fade in late, and the pill
 * hides while the panel is open. [scrim], if given, is a view whose background fades with it.
 */
class IslandMorph(private val panel: ViewGroup, private val island: View, private val radius: Float, private val scrim: View? = null,
                  private val keepHidden: () -> Boolean = { false }) {
    var progress = 1f
        private set(v) {
            field = v
            val inside = ((v - 0.45f) / 0.55f).coerceIn(0f, 1f)
            for (i in 0 until panel.childCount) panel.getChildAt(i).alpha = inside
            scrim?.background?.alpha = (v.coerceIn(0f, 1f) * 255).toInt()
            panel.invalidateOutline()
        }
    private var anim: ValueAnimator? = null
    /** Shrinking back into the pill; the panel is still visible but already on its way out. */
    var closing = false
        private set
    private val at = IntArray(2)
    private val pill = IntArray(2)

    init {
        panel.clipToOutline = true
        panel.outlineProvider = object : ViewOutlineProvider() {
            override fun getOutline(v: View, o: Outline) {
                val w = v.width; val h = v.height
                val p = progress
                if (p >= 1f || island.width == 0) { o.setRoundRect(0, 0, w, h, radius); return }
                island.getLocationInWindow(pill); v.getLocationInWindow(at)
                val il = (pill[0] - at[0]).toFloat(); val itop = (pill[1] - at[1]).toFloat()
                val ir = il + island.width; val ib = itop + island.height
                fun lerp(a: Float, b: Float) = a + (b - a) * p
                // Overshoot past 1 only widens the card a hair; never let the rectangle turn inside out.
                o.setRoundRect(lerp(il, 0f).toInt(), lerp(itop, 0f).toInt(), lerp(ir, w.toFloat()).toInt().coerceAtLeast(lerp(il, 0f).toInt() + 1),
                    lerp(ib, h.toFloat()).toInt().coerceAtLeast(lerp(itop, 0f).toInt() + 1), lerp(island.height / 2f, radius))
            }
        }
    }

    /** Call right after making the panel visible. */
    fun open() {
        island.animate().cancel()
        island.alpha = 0f
        island.visibility = View.INVISIBLE
        closing = false
        progress = 0f
        run(1f, 340, PathInterpolator(0.2f, 0.9f, 0.25f, 1.04f)) {}
    }

    /** Shrinks into the pill, then [end] (hide the panel there) and the pill comes back. */
    fun close(end: () -> Unit) {
        closing = true
        run(0f, 230, DecelerateInterpolator(1.6f)) {
            closing = false
            end()
            progress = 1f
            // A card that waited for the panel may have taken the island's place already.
            if (!keepHidden()) { island.alpha = 1f; island.visibility = View.VISIBLE }
        }
    }

    /** Hidden without the animation (picture in picture, a tab opened from inside). */
    fun cut() {
        anim?.cancel()
        closing = false
        progress = 1f
        island.alpha = 1f
        island.visibility = View.VISIBLE
    }

    private fun run(to: Float, ms: Long, curve: android.animation.TimeInterpolator, end: () -> Unit) {
        anim?.cancel()
        anim = ValueAnimator.ofFloat(progress, to).apply {
            duration = ms
            interpolator = curve
            addUpdateListener { progress = it.animatedValue as Float }
            addListener(object : android.animation.AnimatorListenerAdapter() {
                var cancelled = false
                override fun onAnimationCancel(a: android.animation.Animator) { cancelled = true }
                override fun onAnimationEnd(a: android.animation.Animator) { if (!cancelled) end() }
            })
            start()
        }
    }
}
