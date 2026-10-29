package app.mica.blocker

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File

class EngineTest {
    private fun engine(json: String, excluded: List<String> = emptyList()) = Engine(Engine.parseList(json), excluded)
    private fun req(url: String, type: Int = ResourceType.SCRIPT, initiator: String? = "news.example", method: String = "GET") =
        Request(url, type, method, initiator)

    @Test fun urlFilterAnchorsAndSeparator() {
        val f = UrlFilter("||ads.example.com^")
        fun m(url: String) = Request(url, ResourceType.SCRIPT).let { f.matches(it.lowerUrl, it.hostStart, it.hostEnd) }
        assertTrue(m("https://ads.example.com/x.js"))
        assertTrue(m("https://cdn.ads.example.com/x.js"))
        assertTrue(m("https://ads.example.com"))
        assertFalse(m("https://badads.example.com/x.js"))
        assertFalse(m("https://ads.example.com.evil.net/"))
        assertFalse(m("https://news.example/?u=ads.example.com"))

        val start = UrlFilter("|https://track.")
        assertTrue(start.matches("https://track.io/p", 8, 16))
        assertFalse(start.matches("http://x/https://track.", 7, 8))

        val end = UrlFilter("/pixel.gif|")
        assertTrue(end.matches("https://a.com/pixel.gif", 8, 13))
        assertFalse(end.matches("https://a.com/pixel.gif?x=1", 8, 13))

        val wild = UrlFilter("/ads/*/banner^")
        assertTrue(wild.matches("https://a.com/ads/300x250/banner?x", 8, 13))
        assertTrue(wild.matches("https://a.com/ads/1/2/banner", 8, 13))
        assertFalse(wild.matches("https://a.com/ads/1/bannerx", 8, 13))

        assertTrue(UrlFilter("ADS.JS").matches("https://a.com/ads.js", 8, 13))
        assertFalse(UrlFilter("ADS.JS", caseSensitive = true).matches("https://a.com/ads.js", 8, 13))
    }

    @Test fun indexKeys() {
        assertEquals("ads.example.com", UrlFilter("||ads.example.com^").anchoredHost())
        assertEquals("ads.example.com", UrlFilter("||ads.example.com/path").anchoredHost())
        assertNull(UrlFilter("||ads.example.*/x").anchoredHost())
        assertEquals("banner", UrlFilter("/banner/*").token())
        assertNull(UrlFilter("ads*").token())
        assertEquals("pixel", UrlFilter("/pixel.").token())
    }

    @Test fun requestDomainsMatchSubdomains() {
        val e = engine("""[{"action":{"type":"block"},"condition":{"requestDomains":["doubleclick.net"]},"priority":1}]""")
        assertTrue(e.blocks(req("https://securepubads.g.doubleclick.net/tag/js/gpt.js")))
        assertTrue(e.blocks(req("https://doubleclick.net/x")))
        assertFalse(e.blocks(req("https://notdoubleclick.net/x")))
        // Not main frame unless the rule says so.
        assertFalse(e.blocks(req("https://doubleclick.net/", ResourceType.MAIN_FRAME, initiator = null)))
    }

    @Test fun firstPartyExceptionForPureHostRules() {
        val e = engine("""[{"action":{"type":"block"},"condition":{"requestDomains":["admob.com"]},"priority":1}]""")
        assertTrue(e.blocks(req("https://ads.admob.com/x.js", initiator = "news.example")))
        // Visiting the host itself loads its own scripts.
        assertFalse(e.blocks(req("https://ads.admob.com/x.js", initiator = "apps.admob.com")))
        // Only pure-host rules get the exception.
        val withType = engine("""[{"action":{"type":"block"},"condition":{"requestDomains":["admob.com"],"resourceTypes":["script"]}}]""")
        assertTrue(withType.blocks(req("https://ads.admob.com/x.js", initiator = "apps.admob.com")))
    }

    @Test fun allowBeatsBlockAtEqualPriorityHigherPriorityWins() {
        val e = engine("""[
            {"action":{"type":"block"},"condition":{"urlFilter":"/ads/"},"priority":10},
            {"action":{"type":"allow"},"condition":{"urlFilter":"/ads/ok^"},"priority":10},
            {"action":{"type":"block"},"condition":{"urlFilter":"/ads/ok/worse"},"priority":20}
        ]""")
        assertTrue(e.blocks(req("https://a.com/ads/x.js")))
        assertFalse(e.blocks(req("https://a.com/ads/ok/x.js")))
        assertTrue(e.blocks(req("https://a.com/ads/ok/worse.js")))
    }

    @Test fun domainTypeAndInitiators() {
        val e = engine("""[
            {"action":{"type":"block"},"condition":{"urlFilter":"/track.js","domainType":"thirdParty"}},
            {"action":{"type":"block"},"condition":{"urlFilter":"/only-on-news.js","initiatorDomains":["news.example"]}},
            {"action":{"type":"block"},"condition":{"urlFilter":"/not-on-shop.js","excludedInitiatorDomains":["shop.example"]}}
        ]""")
        assertTrue(e.blocks(req("https://cdn.other.net/track.js", initiator = "news.example")))
        assertFalse(e.blocks(req("https://static.news.example/track.js", initiator = "www.news.example")))
        assertTrue(e.blocks(req("https://x.net/only-on-news.js", initiator = "m.news.example")))
        assertFalse(e.blocks(req("https://x.net/only-on-news.js", initiator = "blog.example")))
        assertFalse(e.blocks(req("https://x.net/only-on-news.js", initiator = null)))
        assertTrue(e.blocks(req("https://x.net/not-on-shop.js", initiator = "news.example")))
        assertFalse(e.blocks(req("https://x.net/not-on-shop.js", initiator = "www.shop.example")))
    }

    @Test fun registrableDomains() {
        assertEquals("example.com", Domains.registrable("a.b.example.com"))
        assertEquals("bbc.co.uk", Domains.registrable("www.bbc.co.uk"))
        assertEquals("vnexpress.net", Domains.registrable("s1.vnexpress.net"))
        assertEquals("tuoitre.com.vn", Domains.registrable("cdn.tuoitre.com.vn"))
        assertEquals("10.0.0.1", Domains.registrable("10.0.0.1"))
        assertTrue(Request("https://cdn.tuoitre.com.vn/a.js", ResourceType.SCRIPT, initiator = "tuoitre.com.vn").firstParty)
        assertFalse(Request("https://ads.com.vn/a.js", ResourceType.SCRIPT, initiator = "tuoitre.com.vn").firstParty)
    }

    @Test fun resourceTypesAndMethods() {
        val e = engine("""[
            {"action":{"type":"block"},"condition":{"urlFilter":"/beacon","resourceTypes":["ping","xmlhttprequest"]}},
            {"action":{"type":"block"},"condition":{"urlFilter":"/collect","requestMethods":["post"]}},
            {"action":{"type":"block"},"condition":{"urlFilter":"/widget","excludedResourceTypes":["image"]}},
            {"action":{"type":"block"},"condition":{"requestDomains":["bad.example"],"resourceTypes":["main_frame"]}}
        ]""")
        assertTrue(e.blocks(req("https://a.com/beacon", ResourceType.XMLHTTPREQUEST)))
        assertFalse(e.blocks(req("https://a.com/beacon", ResourceType.IMAGE)))
        assertTrue(e.blocks(req("https://a.com/collect", method = "POST")))
        assertFalse(e.blocks(req("https://a.com/collect", method = "GET")))
        assertTrue(e.blocks(req("https://a.com/widget.js")))
        assertFalse(e.blocks(req("https://a.com/widget.png", ResourceType.IMAGE)))
        assertTrue(e.blocks(req("https://bad.example/", ResourceType.MAIN_FRAME, initiator = null)))
    }

    @Test fun excludedSitesLoadEverything() {
        val json = """[{"action":{"type":"block"},"condition":{"requestDomains":["ads.net"]}}]"""
        assertTrue(engine(json).blocks(req("https://ads.net/a.js", initiator = "news.example")))
        assertFalse(engine(json, excluded = listOf("news.example")).blocks(req("https://ads.net/a.js", initiator = "m.news.example")))
    }

    @Test fun skipsWhatCannotBeChecked() {
        val e = engine("""[
            {"action":{"type":"block"},"condition":{"requestDomains":["com"],"responseHeaders":[{"header":"link"}]}},
            {"action":{"type":"redirect","redirect":{"extensionPath":"/x.js"}},"condition":{"urlFilter":"/x.js"}},
            {"action":{"type":"block"},"condition":{"regexFilter":"^https://"}}
        ]""")
        assertEquals(0, e.size)
    }

    @Test fun guessesResourceTypes() {
        assertEquals(ResourceType.MAIN_FRAME, ResourceType.guess("https://a.com/", true, emptyMap()))
        assertEquals(ResourceType.SCRIPT, ResourceType.guess("https://a.com/x.js?v=1", false, emptyMap()))
        assertEquals(ResourceType.IMAGE, ResourceType.guess("https://a.com/p", false, mapOf("Accept" to "image/avif,image/webp")))
        assertEquals(ResourceType.SUB_FRAME, ResourceType.guess("https://a.com/p", false, mapOf("sec-fetch-dest" to "iframe")))
        assertEquals(ResourceType.STYLESHEET, ResourceType.guess("https://a.com/p", false, mapOf("Accept" to "text/css,*/*;q=0.1")))
        assertEquals(ResourceType.OTHER, ResourceType.guess("https://a.com/p", false, emptyMap()))
    }

    // The real default lists, as the app loads them.
    @Test fun realDefaultLists() {
        val dir = File("../../extensions/mica-blocker/rules")
        val (lists, _) = Engine.parseConfig(File(dir, "default.json").readText())
        fun used(): Long { repeat(3) { System.gc(); Thread.sleep(50) }; return Runtime.getRuntime().let { it.totalMemory() - it.freeMemory() } }
        val before = used()
        val t0 = System.nanoTime()
        val rules = lists.flatMap { Engine.parseList(File(dir, "$it.json").readText()) }
        val e = Engine(rules)
        val loadMs = (System.nanoTime() - t0) / 1_000_000
        val heapMb = (used() - before) / 1_000_000.0
        println("Loaded ${e.size} rules from ${lists.size} lists in $loadMs ms, ${"%.1f".format(heapMb)} MB heap; ${e.stats()}")
        assertTrue(e.size > 15000)

        val blocked = listOf(
            req("https://securepubads.g.doubleclick.net/tag/js/gpt.js"),
            req("https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js"),
            req("https://www.google-analytics.com/analytics.js"),
            req("https://static.ads-twitter.com/uwt.js"),
            req("https://connect.facebook.net/en_US/fbevents.js"),
            req("https://ad.doubleclick.net/ddm/adj/x", ResourceType.SUB_FRAME),
        )
        val allowed = listOf(
            req("https://news.example/style.css", ResourceType.STYLESHEET),
            req("https://www.wikipedia.org/", ResourceType.MAIN_FRAME, initiator = null),
            req("https://cdn.jsdelivr.net/npm/react@18.3.1/umd/react.production.min.js"),
            req("https://fonts.gstatic.com/s/roboto/v30/KFOmCnqEu92Fr1Mu4mxK.woff2", ResourceType.FONT),
            req("https://upload.wikimedia.org/wikipedia/commons/a/a9/Example.jpg", ResourceType.IMAGE),
            req("https://github.com/", ResourceType.MAIN_FRAME, initiator = null),
        )
        for (r in blocked) assertTrue("should block ${r.url}", e.blocks(r))
        for (r in allowed) assertFalse("should allow ${r.url} (rule ${e.decide(r)?.first?.urlFilter?.body})", e.blocks(r))

        // Rough speed check: under 50 µs a request on average, mixed URLs.
        val mix = blocked + allowed
        repeat(2000) { e.blocks(mix[it % mix.size]) }
        val n = 20000
        val t1 = System.nanoTime()
        repeat(n) { e.blocks(Request(mix[it % mix.size].url + "?n=$it", ResourceType.SCRIPT, "GET", "news.example")) }
        val perUs = (System.nanoTime() - t1) / 1000.0 / n
        println("Average ${"%.1f".format(perUs)} µs per request")
        assertTrue("too slow: $perUs µs", perUs < 50)
    }
}
