package app.mica

import android.app.Activity
import android.content.Context
import android.graphics.drawable.StateListDrawable
import android.text.InputType
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.inputmethod.InputMethodManager
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import androidx.activity.ComponentActivity
import androidx.activity.OnBackPressedCallback
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat

// Mica's own fields, cards and sheets, so forms and notices look like the island rather than stock Android.

class CardAction(val label: String, val primary: Boolean = false, val run: () -> Unit)

/** A text field in Mica's style: a soft rounded well, an accent ring while focused, no underline. */
fun Context.field(hint: String, value: String = "", kind: Int = InputType.TYPE_CLASS_TEXT): EditText = EditText(this).apply {
    this.hint = hint
    setText(value)
    setSingleLine()
    inputType = kind
    textSize = 15f
    setTextColor(col(R.color.mica_ink))
    setHintTextColor(col(R.color.mica_faint))
    highlightColor = col(R.color.mica_accent_soft)
    pad(14, 12)
    background = StateListDrawable().apply {
        addState(intArrayOf(android.R.attr.state_focused), rounded(col(R.color.mica_canvas), 14, col(R.color.mica_accent), 1.5f))
        addState(intArrayOf(), rounded(col(R.color.mica_canvas), 14, col(R.color.mica_line)))
    }
    if (android.os.Build.VERSION.SDK_INT >= 29) textCursorDrawable = rounded(col(R.color.mica_accent), 1).apply { setSize(dp(2), dp(20)) }
}

/** A password field with a Show / Hide switch inside its well. Returns the row and the EditText. */
fun Context.passwordField(hint: String, value: String = ""): Pair<View, EditText> {
    val hidden = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
    val shown = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD
    val edit = field(hint, value, hidden)
    edit.setPadding(edit.paddingLeft, edit.paddingTop, dp(52), edit.paddingBottom)
    val toggle = android.widget.ImageView(this).apply {
        setImageResource(R.drawable.ic_eye)
        imageTintList = android.content.res.ColorStateList.valueOf(col(R.color.mica_muted))
        contentDescription = getString(R.string.show_password)
        setPadding(dp(10), dp(10), dp(10), dp(10))
        background = pressable(0, 20)
        setOnClickListener {
            val reveal = edit.inputType == hidden
            val at = edit.selectionEnd
            edit.inputType = if (reveal) shown else hidden
            edit.setSelection(at.coerceIn(0, edit.text.length))
            setImageResource(if (reveal) R.drawable.ic_eye_off else R.drawable.ic_eye)
            contentDescription = getString(if (reveal) R.string.hide else R.string.show_password)
        }
    }
    val box = FrameLayout(this)
    box.addView(edit, FrameLayout.LayoutParams(MATCH, WRAP))
    box.addView(toggle, FrameLayout.LayoutParams(dp(44), dp(44), Gravity.END or Gravity.CENTER_VERTICAL).apply { marginEnd = dp(4) })
    return box to edit
}

/** Fields stacked with a little air between them. */
fun Context.form(vararg fields: View) = column().apply {
    fields.forEachIndexed { i, f -> addView(f, lp(MATCH, WRAP).apply { if (i > 0) topMargin = dp(8) }) }
}

/** Tappable rows (a title and an optional line under it), for picking one thing. */
fun Context.choices(items: List<Pair<String, String?>>, pick: (Int) -> Unit) = column().apply {
    items.forEachIndexed { i, (title, sub) ->
        val t = text(title, 15f)
        val c = column(t).apply { pad(12, 11); background = pressable(0, 14); setOnClickListener { pick(i) } }
        if (!sub.isNullOrEmpty()) c.addView(text(sub, 12f, col(R.color.mica_muted)), lp(MATCH, WRAP).apply { topMargin = dp(3) })
        addView(c, lp(MATCH, WRAP))
    }
}

/** A card's insides: title, a line or two, optional content, then its buttons on the right. */
fun Context.cardBody(title: String, detail: String?, extra: View?, actions: List<CardAction>, chosen: (CardAction) -> Unit): LinearLayout {
    val body = column()
    body.addView(text(title, 16f, bold = true))
    if (!detail.isNullOrEmpty()) body.addView(text(detail, 13f, col(R.color.mica_muted)).apply { maxLines = 6; setLineSpacing(0f, 1.12f) }, lp(MATCH, WRAP).apply { topMargin = dp(5) })
    extra?.let { (it.parent as? ViewGroup)?.removeView(it); body.addView(it, lp(MATCH, WRAP).apply { topMargin = dp(12) }) }
    if (actions.isNotEmpty()) {
        val row = LinearLayout(this).apply { gravity = Gravity.END or Gravity.CENTER_VERTICAL; orientation = LinearLayout.HORIZONTAL }
        for (a in actions) {
            val b = text(a.label, 14f, if (a.primary) col(R.color.mica_canvas) else col(R.color.mica_accent), bold = true).apply {
                pad(16, 10)
                background = if (a.primary) pressable(col(R.color.mica_accent), 16) else pressable(0, 16)
                setOnClickListener { chosen(a) }
            }
            row.addView(b, lp(WRAP, WRAP).apply { marginStart = dp(4) })
        }
        body.addView(row, lp(MATCH, WRAP).apply { topMargin = dp(12) })
    }
    return body
}

fun View.hideKeyboard() { (context.getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager).hideSoftInputFromWindow(windowToken, 0) }

/**
 * A card that rises from the bottom over a scrim, for screens without the island (Passwords, Settings).
 * It's a view in the Activity, not a Dialog window: same look, keyboard-aware, Back closes it.
 */
class Sheet private constructor(private val activity: ComponentActivity) {
    private val root = activity.findViewById<FrameLayout>(android.R.id.content)
    private val scrim = FrameLayout(activity)
    private val card = FrameLayout(activity)
    private var closed = false
    private var answered = false
    private var unanswered: (() -> Unit)? = null
    private val back = object : OnBackPressedCallback(true) { override fun handleOnBackPressed() = dismiss() }

    private fun open(title: String, detail: String?, extra: View?, actions: List<CardAction>, keepOpen: Boolean) = with(activity) {
        scrim.setBackgroundColor(col(R.color.mica_scrim))
        scrim.isClickable = true
        scrim.setOnClickListener { dismiss() }
        card.background = rounded(col(R.color.mica_card), 26)
        card.elevation = dp(12).toFloat()
        card.isClickable = true
        card.pad(20, 18)
        card.addView(cardBody(title, detail, extra, actions) { a -> if (!keepOpen) { answered = true; dismiss() }; a.run() })
        val max = dp(560)
        scrim.addView(card, FrameLayout.LayoutParams(minOf(resources.displayMetrics.widthPixels - dp(20), max), FrameLayout.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM or Gravity.CENTER_HORIZONTAL))
        ViewCompat.setOnApplyWindowInsetsListener(scrim) { _, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            val ime = insets.getInsets(WindowInsetsCompat.Type.ime())
            (card.layoutParams as FrameLayout.LayoutParams).bottomMargin = maxOf(bars.bottom, ime.bottom) + dp(10)
            card.requestLayout(); insets
        }
        root.addView(scrim, FrameLayout.LayoutParams(MATCH, MATCH))
        ViewCompat.requestApplyInsets(scrim)
        onBackPressedDispatcher.addCallback(activity, back)
        scrim.alpha = 0f
        scrim.animate().alpha(1f).setDuration(160).start()
        card.translationY = dp(80).toFloat()
        card.animate().translationY(0f).setDuration(240).setInterpolator(android.view.animation.DecelerateInterpolator(2f)).start()
    }

    fun dismiss() {
        if (closed) return
        closed = true
        if (!answered) unanswered?.invoke()
        back.remove()
        card.hideKeyboard()
        card.animate().translationY(card.height.toFloat() / 2).setDuration(160).start()
        scrim.animate().alpha(0f).setDuration(160).withEndAction { root.removeView(scrim) }.start()
    }

    companion object {
        /** [keepOpen]: the actions decide when to close (a form that checks its input first). */
        fun show(activity: ComponentActivity, title: String, detail: String? = null, extra: View? = null, actions: List<CardAction> = emptyList(), keepOpen: Boolean = false,
                 unanswered: (() -> Unit)? = null) =
            Sheet(activity).also { it.unanswered = unanswered; it.open(title, detail, extra, actions, keepOpen) }
    }
}

/** A short notice in an island-like pill at the bottom (Mica's Toast), for screens without the island. */
fun Activity.notice(message: String) {
    val root = findViewById<FrameLayout>(android.R.id.content)
    root.findViewWithTag<View>("mica-notice")?.let { root.removeView(it) }
    val pill: TextView = text(message, 14f).apply {
        tag = "mica-notice"
        pad(18, 12)
        maxLines = 3
        gravity = Gravity.CENTER
        background = rounded(col(R.color.mica_glass), 22, col(R.color.mica_line))
        elevation = dp(6).toFloat()
    }
    val bottom = ViewCompat.getRootWindowInsets(root)?.getInsets(WindowInsetsCompat.Type.systemBars() or WindowInsetsCompat.Type.ime())?.bottom ?: 0
    root.addView(pill, FrameLayout.LayoutParams(FrameLayout.LayoutParams.WRAP_CONTENT, FrameLayout.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM or Gravity.CENTER_HORIZONTAL).apply {
        bottomMargin = bottom + dp(18); marginStart = dp(24); marginEnd = dp(24)
    })
    pill.alpha = 0f; pill.translationY = dp(10).toFloat(); pill.scaleX = 0.92f
    pill.animate().alpha(1f).translationY(0f).scaleX(1f).setDuration(180).start()
    pill.postDelayed({ pill.animate().alpha(0f).translationY(dp(8).toFloat()).setDuration(160).withEndAction { root.removeView(pill) }.start() }, 2600)
}
