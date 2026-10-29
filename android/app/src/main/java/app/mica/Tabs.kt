package app.mica

import android.graphics.Bitmap
import android.os.Bundle
import android.util.AtomicFile
import android.webkit.WebView
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.UUID
import java.util.concurrent.atomic.AtomicInteger

class Space(val id: String, var name: String, var color: Int) {
    var activeTab: String? = null
}

class Tab(val id: String = UUID.randomUUID().toString(), var space: String, var url: String, var title: String = "") {
    var pinned = false
    var lastUsed = System.currentTimeMillis()
    /** Live while in use or recently used; null while resting. */
    var view: WebView? = null
    /** Back/forward history kept while resting (this session only). */
    var saved: Bundle? = null
    /** Host of the page now shown, read by the blocker on WebView's network threads. */
    @Volatile var pageHost: String? = null
    /** Requests blocked since the page's main-frame load. */
    val blocked = AtomicInteger()
    var progress = 100
    /** Frames (reply proxies) that report media playing; a tab with any is never paused or rested. */
    val playingFrames = HashSet<Any>()
    val playing get() = playingFrames.isNotEmpty()
    /** The tab a link opened this one from; back closes it and returns there. */
    var parent: String? = null
    /** The page's own colour (theme-color or background), or 0. */
    var tint = 0
    var icon: Bitmap? = null
    var desktop = false
    /** The http:// address Mica is trying over HTTPS right now, so a failure can offer it (this session only). */
    var upgraded: String? = null
    /** The cookie jar it's in (its own WebView profile), or null for Mica's own cookies. */
    var jar: String? = null
    /** The tab group it's in, or null. */
    var group: String? = null
    /** When it went out of sight while live (0 while shown or resting), for the memory timers. */
    var hiddenAt = 0L
    /** Its page memory was already given back since it was hidden. */
    var frozen = false
    /** You typed into the page before leaving it: never rested, so the text isn't lost. */
    var typed = false
    val isHome get() = url == Navigation.HOME
    /** A private tab: its own WebView profile, never saved, gone with the last private tab. */
    val isPrivate get() = space == Browser.PRIVATE
}

/** A named set of cookies and sign-ins of its own (desktop's cookie jar), kept in a WebView profile. */
class Jar(val id: String, var name: String, var color: Int)

/** Tabs kept together under a name and colour in the deck, as desktop's TabGroup. */
class TabGroup(val id: String, var name: String, var color: Int, val space: String, var collapsed: Boolean = false)

class HistoryEntry(val url: String, var title: String, var at: Long, var visits: Int = 1)

/** Tabs, spaces, history and the library (bookmarks, read later, recently closed) in files/state.json, written atomically. */
class Store(dir: File) {
    private val file = AtomicFile(File(dir, "state.json"))

    class State(val spaces: MutableList<Space>, val tabs: MutableList<Tab>, var activeSpace: String, val history: MutableList<HistoryEntry>,
                val bookmarks: MutableList<HistoryEntry> = mutableListOf(), val readLater: MutableList<HistoryEntry> = mutableListOf(),
                val closed: MutableList<HistoryEntry> = mutableListOf(), val groups: MutableList<TabGroup> = mutableListOf(), val jars: MutableList<Jar>? = null)

    private fun entries(o: JSONObject, name: String) = o.optJSONArray(name)?.let { a ->
        (0 until a.length()).map { a.getJSONObject(it).let { h -> HistoryEntry(h.getString("u"), h.optString("t"), h.optLong("at"), h.optInt("v", 1).coerceAtLeast(1)) } }
    }.orEmpty().toMutableList()

    private fun entries(list: List<HistoryEntry>) = JSONArray().apply { list.forEach { put(JSONObject().put("u", it.url).put("t", it.title).put("at", it.at).apply { if (it.visits > 1) put("v", it.visits) }) } }

    fun load(): State? = try {
        val o = JSONObject(file.readFully().toString(Charsets.UTF_8))
        val spaces = o.getJSONArray("spaces").let { a ->
            (0 until a.length()).map { a.getJSONObject(it).let { s -> Space(s.getString("id"), s.optString("name"), s.getInt("color")).also { it.activeTab = s.optString("active").ifEmpty { null } } } }
        }.toMutableList()
        val tabs = o.getJSONArray("tabs").let { a ->
            (0 until a.length()).map {
                a.getJSONObject(it).let { t ->
                    Tab(t.getString("id"), t.getString("space"), t.getString("url"), t.optString("title")).apply {
                        pinned = t.optBoolean("pinned"); lastUsed = t.optLong("used"); desktop = t.optBoolean("desktop")
                        tint = t.optInt("tint"); parent = t.optString("parent").ifEmpty { null }; group = t.optString("group").ifEmpty { null }; jar = t.optString("jar").ifEmpty { null }
                    }
                }
            }
        }.toMutableList()
        val groups = o.optJSONArray("groups")?.let { a ->
            (0 until a.length()).map { a.getJSONObject(it).let { g -> TabGroup(g.getString("id"), g.optString("name"), g.optInt("color"), g.getString("space"), g.optBoolean("collapsed")) } }
        }.orEmpty().toMutableList()
        val jars = o.optJSONArray("jars")?.let { a ->
            (0 until a.length()).map { a.getJSONObject(it).let { j -> Jar(j.getString("id"), j.optString("name"), j.optInt("color")) } }.toMutableList()
        }
        if (spaces.isEmpty()) null
        else State(spaces, tabs, o.optString("activeSpace", spaces[0].id), entries(o, "history"), entries(o, "bookmarks"), entries(o, "readLater"), entries(o, "closed"), groups, jars)
    } catch (e: Exception) { null }

    fun save(state: State) {
        val o = JSONObject()
        o.put("activeSpace", state.activeSpace)
        o.put("spaces", JSONArray().apply { state.spaces.filter { it.id != Browser.PRIVATE }.forEach { put(JSONObject().put("id", it.id).put("name", it.name).put("color", it.color).put("active", it.activeTab ?: "")) } })
        o.put("tabs", JSONArray().apply {
            state.tabs.filter { !it.isPrivate }.forEach {
                put(JSONObject().put("id", it.id).put("space", it.space).put("url", it.url).put("title", it.title).put("pinned", it.pinned)
                    .put("used", it.lastUsed).put("desktop", it.desktop).put("tint", it.tint).put("parent", it.parent ?: "").put("group", it.group ?: "").put("jar", it.jar ?: ""))
            }
        })
        o.put("history", entries(state.history))
        o.put("bookmarks", entries(state.bookmarks))
        o.put("readLater", entries(state.readLater))
        o.put("closed", entries(state.closed))
        state.jars?.let { js -> o.put("jars", JSONArray().apply { js.forEach { put(JSONObject().put("id", it.id).put("name", it.name).put("color", it.color)) } }) }
        o.put("groups", JSONArray().apply { state.groups.filter { it.space != Browser.PRIVATE }.forEach { put(JSONObject().put("id", it.id).put("name", it.name).put("color", it.color).put("space", it.space).put("collapsed", it.collapsed)) } })
        val out = file.startWrite()
        try {
            out.write(o.toString().toByteArray())
            file.finishWrite(out)
        } catch (e: Exception) {
            file.failWrite(out)
        }
    }
}
