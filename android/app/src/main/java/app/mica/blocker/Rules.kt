package app.mica.blocker

// declarativeNetRequest resource types as bits.
object ResourceType {
    const val MAIN_FRAME = 1 shl 0
    const val SUB_FRAME = 1 shl 1
    const val STYLESHEET = 1 shl 2
    const val SCRIPT = 1 shl 3
    const val IMAGE = 1 shl 4
    const val FONT = 1 shl 5
    const val OBJECT = 1 shl 6
    const val XMLHTTPREQUEST = 1 shl 7
    const val PING = 1 shl 8
    const val CSP_REPORT = 1 shl 9
    const val MEDIA = 1 shl 10
    const val WEBSOCKET = 1 shl 11
    const val WEBTRANSPORT = 1 shl 12
    const val WEBBUNDLE = 1 shl 13
    const val OTHER = 1 shl 14
    const val ALL = (1 shl 15) - 1

    private val names = mapOf(
        "main_frame" to MAIN_FRAME, "sub_frame" to SUB_FRAME, "stylesheet" to STYLESHEET, "script" to SCRIPT,
        "image" to IMAGE, "font" to FONT, "object" to OBJECT, "xmlhttprequest" to XMLHTTPREQUEST, "ping" to PING,
        "csp_report" to CSP_REPORT, "media" to MEDIA, "websocket" to WEBSOCKET, "webtransport" to WEBTRANSPORT,
        "webbundle" to WEBBUNDLE, "other" to OTHER,
    )

    fun mask(list: List<*>?): Int = list?.fold(0) { m, n -> m or (names[n] ?: 0) } ?: 0

    /**
     * WebView doesn't say what a request is for, so guess like Chromium would label it, from what it does
     * tell us: whether it's the main frame, the request headers and the file extension.
     */
    fun guess(url: String, isMainFrame: Boolean, headers: Map<String, String>): Int {
        if (isMainFrame) return MAIN_FRAME
        fun header(name: String) = headers.entries.firstOrNull { it.key.equals(name, true) }?.value
        when (header("Sec-Fetch-Dest")?.lowercase()) {
            "iframe", "frame" -> return SUB_FRAME
            "script", "worker", "sharedworker", "serviceworker" -> return SCRIPT
            "style" -> return STYLESHEET
            "image" -> return IMAGE
            "font" -> return FONT
            "audio", "video", "track" -> return MEDIA
            "object", "embed" -> return OBJECT
            "empty" -> return XMLHTTPREQUEST
        }
        val accept = header("Accept")?.lowercase().orEmpty()
        if (accept.startsWith("text/css")) return STYLESHEET
        if (accept.startsWith("image/")) return IMAGE
        if (accept.startsWith("text/html")) return SUB_FRAME
        var end = url.length
        for (c in "?#") { val i = url.indexOf(c); if (i in 0 until end) end = i }
        val path = url.substring(0, end).lowercase()
        val dot = path.lastIndexOf('.')
        val ext = if (dot > path.lastIndexOf('/')) path.substring(dot + 1) else ""
        when (ext) {
            "js", "mjs" -> return SCRIPT
            "css" -> return STYLESHEET
            "png", "jpg", "jpeg", "gif", "webp", "avif", "svg", "ico", "bmp" -> return IMAGE
            "woff", "woff2", "ttf", "otf", "eot" -> return FONT
            "mp4", "webm", "mp3", "m4a", "ogg", "wav", "m3u8", "mpd", "ts", "aac", "vtt" -> return MEDIA
            "json" -> return XMLHTTPREQUEST
        }
        if (header("X-Requested-With").equals("XMLHttpRequest", true)) return XMLHTTPREQUEST
        return OTHER
    }
}

enum class Action { BLOCK, ALLOW }

class Rule(
    val action: Action,
    val priority: Int,
    /** Set to null by Engine once the rule is filed under these domains: finding it there proves the match. */
    var requestDomains: Set<String>?,
    val excludedRequestDomains: Set<String>?,
    val initiatorDomains: Set<String>?,
    val excludedInitiatorDomains: Set<String>?,
    /** 0 = any, 1 = first party only, 2 = third party only. */
    val domainType: Int,
    val types: Int,
    val methods: Set<String>?,
    val excludedMethods: Set<String>?,
    val urlFilter: UrlFilter?,
    /**
     * A block rule with only requestDomains: like the twin desktop's blocker.js adds, it allows (rather than
     * blocks) the host's requests on its own site, outside the main frame.
     */
    val ownSiteAllowed: Boolean = false,
) {
    /** What this rule does to a request it matches. */
    fun actionFor(r: Request) = if (ownSiteAllowed && r.firstParty && r.type != ResourceType.MAIN_FRAME) Action.ALLOW else action

    fun matches(r: Request): Boolean {
        if (types and r.type == 0) return false
        if (methods != null && r.method !in methods) return false
        if (excludedMethods != null && r.method in excludedMethods) return false
        requestDomains?.let { if (!Domains.matches(it, r.host)) return false }
        if (excludedRequestDomains != null && Domains.matches(excludedRequestDomains, r.host)) return false
        if (domainType != 0 && (domainType == 1) != r.firstParty) return false
        if (initiatorDomains != null && (r.initiator == null || !Domains.matches(initiatorDomains, r.initiator))) return false
        if (excludedInitiatorDomains != null && r.initiator != null && Domains.matches(excludedInitiatorDomains, r.initiator)) return false
        if (urlFilter != null && !urlFilter.matches(if (urlFilter.caseSensitive) r.url else r.lowerUrl, r.hostStart, r.hostEnd)) return false
        return true
    }

    companion object {
        /**
         * Builds a rule from its JSON form, or returns null for what can't be applied before a response
         * arrives (responseHeaders), regexFilter, and actions other than block and allow.
         */
        @Suppress("UNCHECKED_CAST")
        fun from(json: Map<String, Any?>): Rule? {
            val action = when ((json["action"] as? Map<String, Any?>)?.get("type")) {
                "block" -> Action.BLOCK
                "allow", "allowAllRequests" -> Action.ALLOW
                else -> return null
            }
            val c = json["condition"] as? Map<String, Any?> ?: emptyMap()
            if (c.containsKey("responseHeaders") || c.containsKey("excludedResponseHeaders") || c.containsKey("regexFilter")) return null
            fun set(key: String) = (c[key] as? List<*>)?.mapTo(HashSet()) { (it as String).lowercase() }
            val included = ResourceType.mask(c["resourceTypes"] as? List<*>)
            val types = if (included != 0) included else ResourceType.ALL and ResourceType.MAIN_FRAME.inv() and ResourceType.mask(c["excludedResourceTypes"] as? List<*>).inv()
            return Rule(
                action = action,
                priority = (json["priority"] as? Double)?.toInt() ?: 1,
                requestDomains = set("requestDomains"),
                excludedRequestDomains = set("excludedRequestDomains"),
                initiatorDomains = set("initiatorDomains"),
                excludedInitiatorDomains = set("excludedInitiatorDomains"),
                domainType = when (c["domainType"]) { "firstParty" -> 1; "thirdParty" -> 2; else -> 0 },
                types = types,
                methods = set("requestMethods"),
                excludedMethods = set("excludedRequestMethods"),
                urlFilter = (c["urlFilter"] as? String)?.let { UrlFilter(it, c["isUrlFilterCaseSensitive"] == true) },
                ownSiteAllowed = action == Action.BLOCK && c.keys == setOf("requestDomains"),
            )
        }
    }
}

/** One request, prepared once so every rule check is cheap. */
class Request(val url: String, val type: Int, method: String = "GET", initiator: String? = null) {
    val lowerUrl = url.lowercase()
    val host = Domains.hostOf(url)
    val hostStart: Int
    val hostEnd: Int
    val method = method.lowercase()
    /** The host of the page that made the request; null for a navigation the user started. */
    val initiator = initiator?.lowercase()?.ifEmpty { null }
    /** declarativeNetRequest counts a request without an initiator as first party. */
    val firstParty = this.initiator == null || Domains.sameSite(host, this.initiator)

    init {
        val at = if (host.isEmpty()) -1 else lowerUrl.indexOf(host, lowerUrl.indexOf("://").coerceAtLeast(0))
        hostStart = if (at < 0) 0 else at
        hostEnd = if (at < 0) 0 else at + host.length
    }
}
