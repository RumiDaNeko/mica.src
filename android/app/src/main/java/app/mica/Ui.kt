package app.mica

import android.content.Context
import android.content.res.ColorStateList
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.RippleDrawable
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.widget.LinearLayout
import android.widget.TextView
import androidx.core.content.ContextCompat

// Small helpers so the shell can be built in code (no Material, no Compose).
fun Context.dp(v: Number) = (v.toFloat() * resources.displayMetrics.density + 0.5f).toInt()
fun Context.col(id: Int) = ContextCompat.getColor(this, id)

fun Context.rounded(fill: Int, radiusDp: Number, stroke: Int = 0, strokeDp: Number = 0.75f) = GradientDrawable().apply {
    setColor(fill)
    cornerRadius = dp(radiusDp).toFloat()
    if (stroke != 0) setStroke(maxOf(1, dp(strokeDp)), stroke)
}

/** A rounded background with a touch ripple. */
fun Context.pressable(fill: Int, radiusDp: Number, stroke: Int = 0) =
    RippleDrawable(ColorStateList.valueOf(col(R.color.mica_accent_soft)), rounded(fill, radiusDp, stroke), rounded(Color.WHITE, radiusDp))

fun Context.text(value: CharSequence = "", sizeSp: Float = 15f, color: Int = col(R.color.mica_ink), bold: Boolean = false) = TextView(this).apply {
    text = value
    setTextSize(TypedValue.COMPLEX_UNIT_SP, sizeSp)
    setTextColor(color)
    if (bold) typeface = android.graphics.Typeface.create("sans-serif-medium", android.graphics.Typeface.NORMAL)
    includeFontPadding = false
}

fun Context.row(vararg children: View, gravity: Int = Gravity.CENTER_VERTICAL) = LinearLayout(this).apply {
    orientation = LinearLayout.HORIZONTAL
    this.gravity = gravity
    children.forEach { addView(it) }
}

fun Context.column(vararg children: View) = LinearLayout(this).apply {
    orientation = LinearLayout.VERTICAL
    children.forEach { addView(it) }
}

fun View.pad(h: Int, v: Int = h) = apply { setPadding(context.dp(h), context.dp(v), context.dp(h), context.dp(v)) }

fun lp(w: Int, h: Int, weight: Float = 0f) = LinearLayout.LayoutParams(w, h, weight)
const val MATCH = LinearLayout.LayoutParams.MATCH_PARENT
const val WRAP = LinearLayout.LayoutParams.WRAP_CONTENT

/** Colours for spaces and tab groups. */
val PALETTE = intArrayOf(0xFF71608C.toInt(), 0xFF7C977D.toInt(), 0xFFB0708A.toInt(), 0xFF5F86A8.toInt(), 0xFFB08A4F.toInt(), 0xFF7A7084.toInt())

/** Mixes [b] into [a] by [t] (0..1), keeping a's alpha. */
fun blend(a: Int, b: Int, t: Float): Int {
    fun ch(x: Int, y: Int) = (x + (y - x) * t).toInt().coerceIn(0, 255)
    return Color.argb(Color.alpha(a), ch(Color.red(a), Color.red(b)), ch(Color.green(a), Color.green(b)), ch(Color.blue(a), Color.blue(b)))
}

/** The space colour lightened for dark mode so dots stay visible. */
fun Context.spaceColor(c: Int): Int {
    val night = (resources.configuration.uiMode and android.content.res.Configuration.UI_MODE_NIGHT_MASK) == android.content.res.Configuration.UI_MODE_NIGHT_YES
    return if (night) blend(c, Color.WHITE, 0.3f) else c
}
