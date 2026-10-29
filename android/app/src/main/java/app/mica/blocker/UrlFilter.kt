package app.mica.blocker

// declarativeNetRequest's urlFilter: '*' is any run of characters, '^' a separator (anything but a letter,
// digit, '_', '-', '.', '%') or the end of the URL, a leading '||' anchors to the start of a host label,
// a leading '|' to the start of the URL and a trailing '|' to its end.
class UrlFilter(val source: String, val caseSensitive: Boolean = false) {
    val hostAnchor: Boolean
    private val startAnchor: Boolean
    private val endAnchor: Boolean
    val body: String

    init {
        var f = if (caseSensitive) source else source.lowercase()
        hostAnchor = f.startsWith("||")
        startAnchor = !hostAnchor && f.startsWith("|")
        f = f.removePrefix(if (hostAnchor) "||" else if (startAnchor) "|" else "")
        endAnchor = f.endsWith("|")
        f = f.removeSuffix("|")
        body = if (hostAnchor || startAnchor) f else f.trimStart('*')
    }

    /** The host a '||host^…' filter names, when the whole host is spelled out, else null. */
    fun anchoredHost(): String? {
        if (!hostAnchor) return null
        var end = 0
        while (end < body.length && body[end].let { it in 'a'..'z' || it in '0'..'9' || it == '.' || it == '-' }) end++
        if (end == 0) return null
        if (end < body.length && body[end] !in "^/:") return null
        return body.substring(0, end).trimEnd('.').ifEmpty { null }
    }

    /**
     * The longest run of letters, digits and '%' that any matching URL must contain as a whole token,
     * or null. Used to file the rule under that token.
     */
    fun token(): String? {
        var best: String? = null
        var i = 0
        while (i < body.length) {
            if (!isTokenChar(body[i])) { i++; continue }
            val start = i
            while (i < body.length && isTokenChar(body[i])) i++
            val leftBound = if (start == 0) hostAnchor || startAnchor else body[start - 1] != '*'
            val rightBound = if (i == body.length) endAnchor else body[i] != '*'
            if (leftBound && rightBound && i - start >= 2 && (best == null || i - start > best.length)) best = body.substring(start, i)
        }
        return best
    }

    fun matches(url: String, hostStart: Int, hostEnd: Int): Boolean {
        if (startAnchor) return matchAt(url, 0)
        if (hostAnchor) {
            if (matchAt(url, hostStart)) return true
            for (i in hostStart + 1 until hostEnd) if (url[i - 1] == '.' && matchAt(url, i)) return true
            return false
        }
        // Unanchored: a leading '*' changes nothing, so it's dropped in init. When the filter starts with
        // a literal character, jump between its occurrences instead of trying every position.
        if (body.isEmpty()) return true
        val first = body[0]
        if (first == '^') {
            for (s in 0..url.length) if (matchAt(url, s)) return true
            return false
        }
        var from = 0
        while (true) {
            val hit = url.indexOf(first, from)
            if (hit < 0) return false
            if (matchAt(url, hit)) return true
            from = hit + 1
        }
    }

    // Wildcard matching with backtracking to the last '*'.
    private fun matchAt(url: String, start: Int): Boolean {
        val n = url.length
        var u = start
        var p = 0
        var starP = -1
        var starU = 0
        while (true) {
            if (p == body.length) {
                if (!endAnchor || u == n) return true
                if (starP < 0 || starU >= n) return false
                starU++; u = starU; p = starP + 1
                continue
            }
            val c = body[p]
            if (c == '*') { starP = p; starU = u; p++; continue }
            if (u < n && (if (c == '^') isSeparator(url[u]) else url[u] == c)) { u++; p++; continue }
            if (u == n && c == '^') { p++; continue }
            if (starP >= 0 && starU < n) { starU++; u = starU; p = starP + 1; continue }
            return false
        }
    }

    companion object {
        fun isTokenChar(c: Char) = c in 'a'..'z' || c in '0'..'9' || c == '%' || c in 'A'..'Z'
        fun isSeparator(c: Char) = !(c in 'a'..'z' || c in 'A'..'Z' || c in '0'..'9' || c == '_' || c == '-' || c == '.' || c == '%')
    }
}
