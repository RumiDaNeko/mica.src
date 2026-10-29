package app.mica

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class NavigationTest {
    @Test fun resolvesLikeDesktop() {
        assertEquals("https://example.com", Navigation.resolve("example.com"))
        assertEquals("http://localhost:8080/x", Navigation.resolve("localhost:8080/x"))
        assertEquals("https://www.google.com/search?q=hello+world", Navigation.resolve("hello world"))
        assertEquals("mica://snake", Navigation.resolve("MICA://Snake/"))
        assertEquals(Navigation.HOME, Navigation.resolve("  "))
        assertNull(Navigation.resolve("file:///etc/passwd"))
        assertEquals("https://vnexpress.net/", Navigation.resolve("https://vnexpress.net/"))
    }

    @Test fun gameAddresses() {
        assertEquals("https://games.mica.invalid/snake", Navigation.engineAddress("mica://snake"))
        assertEquals("https://games.mica.invalid/", Navigation.engineAddress("mica://games"))
        assertEquals("https://tetris.mica.invalid/", Navigation.engineAddress(Navigation.TETRIS))
        assertEquals("mica://connect4", Navigation.fromEngine("https://games.mica.invalid/connect4"))
        assertEquals("mica://games", Navigation.fromEngine("https://games.mica.invalid/"))
        assertEquals(Navigation.TETRIS, Navigation.fromEngine("https://tetris.mica.invalid/"))
        assertEquals("mica://2048", Navigation.label("https://games.mica.invalid/2048"))
        assertEquals("github.com", Navigation.label("https://www.github.com/x"))
        assertEquals(11, Navigation.games.size)
    }

    @Test fun spotlightPutsTypedFirstAndFindsTabs() {
        val tabs = listOf(Navigation.Entry("https://github.com/", "GitHub", "t1"))
        val history = listOf(Navigation.Entry("https://news.ycombinator.com/", "Hacker News"))
        val hits = Navigation.search("git", tabs, history, { "Search “$it”" }, { "Go to “$it”" })
        // "git" starts github.com, so the site stays on top and the search comes second.
        assertEquals("t1", hits[0].tabId)
        assertEquals(Navigation.Kind.SEARCH, hits[1].kind)
        val news = Navigation.search("hacker", tabs, history, { "S $it" }, { "G $it" })
        assertEquals(Navigation.Kind.SEARCH, news[0].kind)
        assertTrue(news.any { it.url == "https://news.ycombinator.com/" })
        val yt = Navigation.search("yt lofi", tabs, history, { "S $it" }, { "G $it" })
        assertEquals(Navigation.Kind.SHORTCUT, yt[0].kind)
        assertEquals("https://www.youtube.com/results?search_query=lofi", yt[0].url)
    }

    @Test fun parsesPageColours() {
        assertEquals(0xFF112233.toInt(), Browser.parseColor("\"#112233\""))
        assertEquals(0xFFAABBCC.toInt(), Browser.parseColor("\"#abc\""))
        assertEquals(0xFF0A141E.toInt(), Browser.parseColor("\"rgb(10, 20, 30)\""))
        assertEquals(0, Browser.parseColor("\"rgba(0, 0, 0, 0)\""))
        assertEquals(0, Browser.parseColor("\"\""))
    }
}
