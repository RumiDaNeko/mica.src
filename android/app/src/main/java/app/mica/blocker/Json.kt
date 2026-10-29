package app.mica.blocker

// A small JSON reader for the rule lists, kept free of Android classes so the matcher can be tested on
// the JVM. Objects become Map<String, Any?>, arrays List<Any?>, numbers Double.
class Json private constructor(private val s: String) {
    private var i = 0

    companion object {
        fun parse(text: String): Any? {
            val p = Json(text)
            p.space()
            val value = p.value()
            p.space()
            if (p.i != p.s.length) p.fail("trailing text")
            return value
        }
    }

    private fun fail(what: String): Nothing = throw IllegalArgumentException("JSON: $what at $i")

    private fun space() {
        while (i < s.length && s[i].let { it == ' ' || it == '\n' || it == '\r' || it == '\t' }) i++
    }

    private fun value(): Any? {
        if (i >= s.length) fail("unexpected end")
        return when (s[i]) {
            '{' -> obj()
            '[' -> arr()
            '"' -> str()
            't' -> word("true", true)
            'f' -> word("false", false)
            'n' -> word("null", null)
            else -> num()
        }
    }

    private fun word(w: String, v: Any?): Any? {
        if (!s.startsWith(w, i)) fail("bad literal")
        i += w.length
        return v
    }

    private fun obj(): Map<String, Any?> {
        i++
        val map = HashMap<String, Any?>()
        space()
        if (i < s.length && s[i] == '}') { i++; return map }
        while (true) {
            space()
            if (i >= s.length || s[i] != '"') fail("expected key")
            val key = str()
            space()
            if (i >= s.length || s[i] != ':') fail("expected :")
            i++
            space()
            map[key] = value()
            space()
            if (i >= s.length) fail("unexpected end")
            when (s[i++]) {
                ',' -> continue
                '}' -> return map
                else -> fail("expected , or }")
            }
        }
    }

    private fun arr(): List<Any?> {
        i++
        val list = ArrayList<Any?>()
        space()
        if (i < s.length && s[i] == ']') { i++; return list }
        while (true) {
            space()
            list.add(value())
            space()
            if (i >= s.length) fail("unexpected end")
            when (s[i++]) {
                ',' -> continue
                ']' -> { list.trimToSize(); return list }
                else -> fail("expected , or ]")
            }
        }
    }

    private fun str(): String {
        i++
        val start = i
        while (i < s.length && s[i] != '"' && s[i] != '\\') i++
        if (i < s.length && s[i] == '"') return s.substring(start, i++)
        val sb = StringBuilder().append(s, start, i)
        while (true) {
            if (i >= s.length) fail("unterminated string")
            val c = s[i++]
            when (c) {
                '"' -> return sb.toString()
                '\\' -> {
                    if (i >= s.length) fail("bad escape")
                    when (val e = s[i++]) {
                        'n' -> sb.append('\n'); 't' -> sb.append('\t'); 'r' -> sb.append('\r')
                        'b' -> sb.append('\b'); 'f' -> sb.append('\u000C')
                        'u' -> {
                            if (i + 4 > s.length) fail("bad \\u")
                            sb.append(s.substring(i, i + 4).toInt(16).toChar()); i += 4
                        }
                        else -> sb.append(e)
                    }
                }
                else -> sb.append(c)
            }
        }
    }

    private fun num(): Double {
        val start = i
        while (i < s.length && s[i].let { it == '-' || it == '+' || it == '.' || it == 'e' || it == 'E' || it in '0'..'9' }) i++
        if (start == i) fail("unexpected character")
        return s.substring(start, i).toDouble()
    }
}
