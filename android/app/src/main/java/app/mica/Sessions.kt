package app.mica

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.io.File

/**
 * Named sets of tabs you can bring back later (desktop's BrowserSessions): one file per session in
 * files/Sessions, as {name, saved, tabs:[{url, title, space}]}. Saving under a name that exists replaces it.
 */
object Sessions {
    class Page(val url: String, val title: String, val space: String)
    class Saved(val name: String, val saved: Long, val tabs: List<Page>, val file: File)

    private fun dir(context: Context) = File(context.filesDir, "Sessions")

    private fun fileName(name: String) = name.map { if (it.isLetterOrDigit() || it in " -_.()" ) it else '_' }.joinToString("").trim().ifEmpty { "Session" } + ".json"

    fun save(context: Context, name: String, tabs: List<Tab>): Boolean = try {
        val d = dir(context).apply { mkdirs() }
        val o = JSONObject().put("name", name).put("saved", System.currentTimeMillis())
            .put("tabs", JSONArray().apply { tabs.forEach { put(JSONObject().put("url", it.url).put("title", it.title).put("space", it.space)) } })
        val f = File(d, fileName(name))
        val tmp = File(d, f.name + ".tmp")
        tmp.writeText(o.toString())
        tmp.renameTo(f) || run { f.delete(); tmp.renameTo(f) }
    } catch (e: Exception) { false }

    /** Newest first; a file that doesn't read is left out. */
    fun list(context: Context): List<Saved> = (dir(context).listFiles { f -> f.name.endsWith(".json") } ?: emptyArray()).mapNotNull { f ->
        try {
            val o = JSONObject(f.readText())
            val a = o.optJSONArray("tabs") ?: return@mapNotNull null
            Saved(o.optString("name", f.nameWithoutExtension), o.optLong("saved", f.lastModified()),
                (0 until a.length()).map { a.getJSONObject(it).let { t -> Page(t.getString("url"), t.optString("title"), t.optString("space")) } }, f)
        } catch (e: Exception) { null }
    }.sortedByDescending { it.saved }

    fun delete(s: Saved) { s.file.delete() }
}
