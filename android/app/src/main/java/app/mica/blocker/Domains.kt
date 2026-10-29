package app.mica.blocker

object Domains {
    /** Set by the app before first use: opens the public suffix list (assets/psl.dat). Unset (unit tests), the short list below stands in. */
    @Volatile var suffixSource: (() -> java.io.InputStream)? = null

    private class Psl(val rules: HashSet<String>, val wild: HashSet<String>, val except: HashSet<String>)

    @Volatile private var loaded: Psl? = null
    @Volatile private var loadedFrom: Any? = null

    // Read on first use, off the main thread as a rule (the blocker asks first, from WebView's network threads).
    private val psl: Psl?
        get() {
            val open = suffixSource ?: return null
            if (loadedFrom !== open) synchronized(this) { if (loadedFrom !== open) { loaded = read(open); loadedFrom = open } }
            return loaded
        }

    private fun read(open: () -> java.io.InputStream): Psl? {
        return try {
            val rules = HashSet<String>(12_000); val wild = HashSet<String>(400); val except = HashSet<String>(16)
            open().bufferedReader().useLines { lines ->
                for (line in lines) {
                    if (line.isEmpty() || line.startsWith("//")) continue
                    val r = line.trim().let { if (it.any { c -> c.code > 127 }) runCatching { java.net.IDN.toASCII(it) }.getOrDefault(it) else it }.lowercase()
                    when {
                        r.startsWith("!") -> except.add(r.substring(1))
                        r.startsWith("*.") -> wild.add(r.substring(2))
                        else -> rules.add(r)
                    }
                }
            }
            Psl(rules, wild, except)
        } catch (e: Exception) { null }
    }


    /** eTLD+1 by the public suffix list: the longest matching rule wins, an exception rule first. */
    private fun registrable(host: String, p: Psl): String {
        val starts = ArrayList<Int>(6)
        starts.add(0)
        for (i in host.indices) if (host[i] == '.') starts.add(i + 1)
        for (k in starts.indices) {
            val s = host.substring(starts[k])
            if (s in p.except) return s
            if (s in p.rules || (k + 1 < starts.size && host.substring(starts[k + 1]) in p.wild)) return if (k == 0) host else host.substring(starts[k - 1])
        }
        // No rule: the last label is the suffix ("*").
        return if (starts.size >= 2) host.substring(starts[starts.size - 2]) else host
    }

    // Without the list (unit tests): multi-label public suffixes common enough to matter for first/third-party
    // checks; everything else is treated as a single-label TLD.
    private val twoLabel = hashSetOf(
        "co.uk", "org.uk", "ac.uk", "gov.uk", "me.uk", "ltd.uk", "plc.uk",
        "com.au", "net.au", "org.au", "edu.au", "gov.au", "co.nz", "org.nz", "net.nz",
        "co.jp", "ne.jp", "or.jp", "ac.jp", "go.jp", "co.kr", "or.kr", "ne.kr",
        "com.vn", "net.vn", "org.vn", "edu.vn", "gov.vn", "info.vn", "biz.vn",
        "com.cn", "net.cn", "org.cn", "gov.cn", "edu.cn", "com.tw", "org.tw", "com.hk", "org.hk",
        "co.in", "net.in", "org.in", "gov.in", "co.id", "or.id", "web.id", "ac.id",
        "com.sg", "com.my", "com.ph", "co.th", "in.th", "com.br", "net.br", "org.br",
        "com.mx", "com.ar", "com.co", "com.pe", "com.tr", "co.za", "com.sa", "com.eg", "com.pk",
        "com.ng", "com.ua", "co.il", "com.pl", "net.pl", "org.pl", "co.at", "or.at", "com.es",
        "github.io", "gitlab.io", "blogspot.com", "appspot.com", "herokuapp.com", "cloudfront.net",
        "vercel.app", "netlify.app", "pages.dev", "workers.dev", "azurewebsites.net", "web.app",
        "firebaseapp.com", "amazonaws.com", "fly.dev", "onrender.com",
    )

    /** The registrable domain (eTLD+1) of a lower-case host, or the host itself for IPs and bare names. */
    fun registrable(host: String): String {
        if (host.isEmpty() || host[0] == '[' || host.last().isDigit()) return host
        psl?.let { return registrable(host, it) }
        val last = host.lastIndexOf('.')
        if (last <= 0) return host
        val second = host.lastIndexOf('.', last - 1)
        if (second < 0) return host
        if (host.substring(second + 1) in twoLabel) {
            val third = host.lastIndexOf('.', second - 1)
            return if (third < 0) host else host.substring(third + 1)
        }
        return host.substring(second + 1)
    }

    fun sameSite(a: String, b: String) = a == b || registrable(a) == registrable(b)

    /** True if [host] is one of [set] or a subdomain of one: a.b.c.com tries a.b.c.com, b.c.com, c.com, com. */
    fun matches(set: Set<String>, host: String): Boolean {
        var at = 0
        while (true) {
            if (set.contains(if (at == 0) host else host.substring(at))) return true
            val dot = host.indexOf('.', at)
            if (dot < 0) return false
            at = dot + 1
        }
    }

    /** The lower-case host of an http(s)/ws(s) URL, or "" if there isn't one. */
    fun hostOf(url: String): String {
        val scheme = url.indexOf("://")
        if (scheme < 0) return ""
        var start = scheme + 3
        var end = start
        while (end < url.length && url[end] != '/' && url[end] != '?' && url[end] != '#') end++
        val at = url.lastIndexOf('@', end - 1)
        if (at >= start) start = at + 1
        if (start < url.length && url[start] == '[') {
            val close = url.indexOf(']', start)
            return if (close in start until end) url.substring(start, close + 1).lowercase() else ""
        }
        val colon = url.indexOf(':', start)
        if (colon in start until end) end = colon
        return url.substring(start, end).lowercase().trimEnd('.')
    }
}
