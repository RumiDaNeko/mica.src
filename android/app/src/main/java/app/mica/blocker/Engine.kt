package app.mica.blocker

/**
 * Mica's blocker without an extension: uBlock Origin Lite's declarativeNetRequest lists, matched natively.
 * Immutable once built, so WebView's network threads can share it without locks. (Building it takes the
 * rules' requestDomains sets over, so a list of rules builds one Engine.)
 *
 * Every rule is filed under one key so a request only looks at a handful of rules:
 *  - its requestDomains (or the host a '||host^' filter spells out), in a table of 64-bit host hashes found
 *    by walking the request host's suffixes;
 *  - else the longest whole token its urlFilter requires, found by splitting the URL into tokens;
 *  - else the sites it's limited to (initiatorDomains);
 *  - else a short generic list checked every time.
 */
class Engine(rules: List<Rule>, excluded: Collection<String> = emptyList()) {
    private val byHost: HostTable
    private val byToken = HashMap<String, MutableList<Rule>>()
    private val byInitiator = HashMap<String, MutableList<Rule>>()
    private val generic = ArrayList<Rule>()
    private val excluded: Set<String> = excluded.mapTo(HashSet()) { it.lowercase() }
    val size = rules.size

    init {
        val hosts = HashMap<Long, Any>()
        fun file(host: String, rule: Rule) {
            val key = HostTable.hash(host, 0)
            when (val old = hosts[key]) {
                null -> hosts[key] = rule
                is Rule -> hosts[key] = arrayListOf(old, rule)
                else -> @Suppress("UNCHECKED_CAST") (old as ArrayList<Rule>).add(rule)
            }
        }
        for (rule in rules) {
            val domains = rule.requestDomains
            val filter = rule.urlFilter
            when {
                domains != null -> { for (d in domains) file(d, rule); rule.requestDomains = null }
                filter?.anchoredHost() != null -> file(filter.anchoredHost()!!, rule)
                filter?.token() != null -> byToken.getOrPut(filter.token()!!.lowercase()) { ArrayList(2) }.add(rule)
                rule.initiatorDomains != null -> for (d in rule.initiatorDomains) byInitiator.getOrPut(d) { ArrayList(2) }.add(rule)
                else -> generic.add(rule)
            }
        }
        byHost = HostTable(hosts)
    }

    /** The rule that decides this request and what it does, or null when no rule matches. */
    fun decide(r: Request): Pair<Rule, Action>? {
        if (excluded.isNotEmpty() && Domains.matches(excluded, r.initiator ?: r.host)) return null
        var best: Rule? = null
        var bestAction = Action.BLOCK
        fun consider(rule: Rule) {
            val b = best
            // declarativeNetRequest: the highest priority wins; at equal priority allow beats block.
            if (b != null && (rule.priority < b.priority || (rule.priority == b.priority && bestAction == Action.ALLOW))) return
            if (!rule.matches(r)) return
            val action = rule.actionFor(r)
            if (b != null && rule.priority == b.priority && action == Action.BLOCK) return
            best = rule; bestAction = action
        }
        // Host suffixes: a.b.ads.com, b.ads.com, ads.com, com.
        val host = r.host
        if (host.isNotEmpty()) {
            var at = 0
            while (true) {
                byHost.forEach(host, at, ::consider)
                val dot = host.indexOf('.', at)
                if (dot < 0) break
                at = dot + 1
            }
        }
        val initiator = r.initiator
        if (initiator != null && byInitiator.isNotEmpty()) {
            var at = 0
            while (true) {
                byInitiator[if (at == 0) initiator else initiator.substring(at)]?.forEach(::consider)
                val dot = initiator.indexOf('.', at)
                if (dot < 0) break
                at = dot + 1
            }
        }
        // Whole tokens of the URL.
        val url = r.lowerUrl
        var i = 0
        while (i < url.length) {
            if (!UrlFilter.isTokenChar(url[i])) { i++; continue }
            val start = i
            while (i < url.length && UrlFilter.isTokenChar(url[i])) i++
            if (i - start >= 2) byToken[url.substring(start, i)]?.forEach(::consider)
        }
        generic.forEach(::consider)
        return best?.let { it to bestAction }
    }

    fun blocks(r: Request) = decide(r)?.second == Action.BLOCK

    /** Index shape, for tests and logs. */
    fun stats(): String {
        val top = byToken.entries.sortedByDescending { it.value.size }.take(5).joinToString { "${it.key}=${it.value.size}" }
        return "hosts=${byHost.size} tokens=${byToken.size} initiators=${byInitiator.size} generic=${generic.size}; biggest tokens: $top"
    }

    companion object {
        /** Rules from one list's JSON. */
        @Suppress("UNCHECKED_CAST")
        fun parseList(json: String): List<Rule> {
            val out = ArrayList<Rule>()
            for (item in Json.parse(json) as List<Any?>) {
                val map = item as? Map<String, Any?> ?: continue
                Rule.from(map)?.let(out::add)
            }
            return out
        }

        /** default.json: { "lists": [...], "excluded": [...] }. */
        @Suppress("UNCHECKED_CAST")
        fun parseConfig(json: String): Pair<List<String>, List<String>> {
            val map = Json.parse(json) as Map<String, Any?>
            return ((map["lists"] as? List<String>).orEmpty()) to ((map["excluded"] as? List<String>).orEmpty())
        }
    }
}

/**
 * Hosts → rules as flat arrays: open addressing on 64-bit FNV-1a hashes of the host, so a lookup hashes
 * the suffix in place (no substring) and the table holds no strings. About 4 bytes of rule reference and
 * 16 bytes of table per host, against ~100 for a HashMap of Strings to lists.
 */
internal class HostTable(entries: Map<Long, Any>) {
    private val keys: LongArray
    private val starts: IntArray
    private val counts: IntArray
    private val rules: Array<Rule?>
    private val mask: Int
    val size = entries.size

    init {
        var cap = 16
        while (cap < entries.size * 2) cap = cap shl 1
        mask = cap - 1
        keys = LongArray(cap); starts = IntArray(cap); counts = IntArray(cap)
        rules = arrayOfNulls(entries.values.sumOf { if (it is Rule) 1 else (it as List<*>).size })
        var next = 0
        for ((key, value) in entries) {
            var slot = (key xor (key ushr 32)).toInt() and mask
            while (keys[slot] != 0L) slot = (slot + 1) and mask
            keys[slot] = key
            starts[slot] = next
            if (value is Rule) { rules[next++] = value; counts[slot] = 1 }
            else { @Suppress("UNCHECKED_CAST") (value as List<Rule>).forEach { rules[next++] = it }; counts[slot] = value.size }
        }
    }

    fun forEach(host: String, at: Int, action: (Rule) -> Unit) {
        val key = hash(host, at)
        var slot = (key xor (key ushr 32)).toInt() and mask
        while (true) {
            val k = keys[slot]
            if (k == 0L) return
            if (k == key) {
                val s = starts[slot]
                for (i in s until s + counts[slot]) action(rules[i]!!)
                return
            }
            slot = (slot + 1) and mask
        }
    }

    companion object {
        /** FNV-1a over host[at..]; never 0, which marks an empty slot. */
        fun hash(host: String, at: Int): Long {
            var h = -0x340d631b7bdddcdbL
            for (i in at until host.length) { h = h xor host[i].code.toLong(); h *= 0x100000001b3L }
            return if (h == 0L) 1L else h
        }
    }
}
