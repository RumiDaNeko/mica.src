package app.mica.blocker

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.DataInputStream
import java.io.DataOutputStream

/** Rules read back from RuleCache decide every request exactly as the rules parsed from JSON. */
class RuleCacheTest {
    private val json = """[
        {"id":1,"priority":1,"action":{"type":"block"},"condition":{"requestDomains":["ads.example.com"]}},
        {"id":2,"priority":2,"action":{"type":"allow"},"condition":{"urlFilter":"||ads.example.com/ok^","resourceTypes":["script"]}},
        {"id":3,"priority":1,"action":{"type":"block"},"condition":{"urlFilter":"/Banner/","isUrlFilterCaseSensitive":true,"domainType":"thirdParty","excludedInitiatorDomains":["news.example"]}},
        {"id":4,"priority":1,"action":{"type":"block"},"condition":{"urlFilter":"track","requestMethods":["post"],"initiatorDomains":["shop.example"]}}
    ]"""

    private fun roundTrip(): Triple<List<Rule>, Int, List<String>>? {
        val bytes = ByteArrayOutputStream()
        DataOutputStream(bytes).use { RuleCache.write(it, Engine.parseList(json), 3, listOf("allowed.example")) }
        return DataInputStream(ByteArrayInputStream(bytes.toByteArray())).use { RuleCache.read(it) }
    }

    @Test fun sameDecisions() {
        val back = roundTrip()
        assertNotNull(back)
        assertEquals(3, back!!.second)
        assertEquals(listOf("allowed.example"), back.third)
        val fresh = Engine(Engine.parseList(json))
        val cached = Engine(back.first)
        val requests = listOf(
            Request("https://ads.example.com/x.js", ResourceType.SCRIPT, initiator = "site.example"),
            Request("https://ads.example.com/ok/x.js", ResourceType.SCRIPT, initiator = "site.example"),
            Request("https://cdn.other/Banner/a.png", ResourceType.IMAGE, initiator = "site.example"),
            Request("https://cdn.other/banner/a.png", ResourceType.IMAGE, initiator = "site.example"),
            Request("https://cdn.other/Banner/a.png", ResourceType.IMAGE, initiator = "news.example"),
            Request("https://t.other/track", ResourceType.XMLHTTPREQUEST, "POST", "shop.example"),
            Request("https://t.other/track", ResourceType.XMLHTTPREQUEST, "GET", "shop.example"),
        )
        for (r in requests) assertEquals(r.url, fresh.decide(r)?.second, cached.decide(r)?.second)
    }

    @Test fun notOurs() {
        assertNull(DataInputStream(ByteArrayInputStream(ByteArray(16))).use { RuleCache.read(it) })
    }
}
