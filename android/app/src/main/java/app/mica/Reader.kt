package app.mica

import android.content.Context
import android.webkit.WebResourceResponse
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayInputStream
import java.io.File
import java.security.MessageDigest

/**
 * Reading mode, from desktop's Reader.js and BrowserReader.cs: the article is found on the page, kept as plain
 * blocks, and drawn by Mica as its own page, mica://read/<site>/<id> (served on a host that never resolves).
 * It opens in the same tab, so Back returns to the page. Articles saved to read later live in
 * files/ReadLater/<id>.json and read without a connection; one just being read sits in the cache.
 */
object Reader {
    const val HOST = "read.mica.invalid"
    private const val PREFIX = "mica://read/"

    fun isReader(url: String) = url.startsWith(PREFIX)
    fun engineAddress(url: String) = "https://$HOST/" + url.removePrefix(PREFIX)
    fun fromEngine(url: String) = PREFIX + url.substringAfter("$HOST/").substringBefore('?').substringBefore('#')
    /** The site part of a reader address, for the island. */
    fun site(url: String) = url.removePrefix(PREFIX).substringBefore('/')
    fun id(url: String) = url.removePrefix(PREFIX).substringAfter('/', "").substringBefore('/')
    fun address(site: String, id: String) = PREFIX + site.replace("/", "") + "/" + id

    /** One id per article address, so saving the same page twice updates one copy. */
    fun idFor(url: String) = MessageDigest.getInstance("SHA-256").digest(url.toByteArray()).take(8).joinToString("") { "%02x".format(it) }

    private val validId = Regex("^[0-9a-f]{16}$")
    private fun savedDir(c: Context) = File(c.filesDir, "ReadLater")
    private fun cacheDir(c: Context) = File(c.cacheDir, "reader")

    fun script(c: Context) = c.assets.open("scripts/Reader.js").use { it.readBytes().toString(Charsets.UTF_8) }

    /** The script's answer (a JSON string inside a JS string) as an article, or null if the page isn't one. */
    fun parse(raw: String?): JSONObject? = try {
        val inner = org.json.JSONTokener(raw ?: "null").nextValue() as? String
        JSONObject(inner ?: "{}").takeIf { it.optBoolean("ok") && it.optJSONArray("blocks") != null }
    } catch (e: Exception) { null }

    /** Keeps an article to read now (cache) or for later (files); returns its reader address. */
    fun keep(c: Context, article: JSONObject, saved: Boolean): String {
        val url = article.optString("url")
        val id = idFor(url)
        val dir = if (saved) savedDir(c) else cacheDir(c)
        dir.mkdirs()
        val file = android.util.AtomicFile(File(dir, "$id.json"))
        val out = file.startWrite()
        try { out.write(article.toString().toByteArray()); file.finishWrite(out) } catch (e: Exception) { file.failWrite(out); throw e }
        return address(article.optString("site").ifEmpty { Navigation.label(url) }, id)
    }

    fun isSaved(c: Context, url: String) = File(savedDir(c), idFor(url) + ".json").exists()
    fun forget(c: Context, url: String) { File(savedDir(c), idFor(url) + ".json").delete() }
    /** The reader address of a saved copy of [url], or null. */
    fun savedAddress(c: Context, url: String): String? {
        val a = load(c, idFor(url)) ?: return null
        return address(a.optString("site").ifEmpty { Navigation.label(url) }, idFor(url))
    }

    fun load(c: Context, id: String): JSONObject? {
        if (!validId.matches(id)) return null
        for (dir in listOf(savedDir(c), cacheDir(c))) {
            val f = File(dir, "$id.json")
            if (f.exists()) return try { JSONObject(f.readText()) } catch (e: Exception) { null }
        }
        return null
    }

    /** Articles read more than two days ago leave the cache. */
    fun trimCache(c: Context) {
        val old = System.currentTimeMillis() - 2 * 24 * 3600_000L
        cacheDir(c).listFiles()?.forEach { if (it.lastModified() < old) it.delete() }
    }

    private val headers = mapOf(
        "Cache-Control" to "no-store",
        // Pictures come from the web; nothing else does, and only Mica's own script runs.
        "Content-Security-Policy" to "default-src 'none'; img-src https: http: data:; style-src 'unsafe-inline'; script-src 'unsafe-inline'",
    )

    fun serve(c: Context, url: String, size: Int, serif: Boolean): WebResourceResponse {
        val id = url.substringAfter("$HOST/").substringAfter('/').substringBefore('?').substringBefore('#')
        val article = load(c, id)
        val html = if (article == null) gone(c) else page(c, article, id, size, serif)
        return WebResourceResponse("text/html", "utf-8", 200, "OK", headers, ByteArrayInputStream(html.toByteArray()))
    }

    private fun esc(s: String) = s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("\"", "&quot;")

    private fun gone(c: Context) = "<!doctype html><meta name=viewport content='width=device-width,initial-scale=1'><title>${esc(c.getString(R.string.reader))}</title>" +
        "<body style='font:16px sans-serif;padding:32px;color:#888'>${esc(c.getString(R.string.reader_gone))}</body>"

    private fun page(c: Context, a: JSONObject, id: String, size: Int, serif: Boolean): String {
        val dark = Games.dark
        val title = a.optString("title")
        val site = a.optString("site")
        val author = a.optString("author")
        val words = a.optInt("words")
        val vietnamese = a.optString("lang").startsWith("vi") || title.any { it in "ăâđêôơưạảấầẩẫậắằẳẵặẹẻẽếềểễệỉịọỏốồổỗộớờởỡợụủứừửữựỳỵỷỹ" }
        val saved = File(savedDir(c), "$id.json").exists()
        val sb = StringBuilder()
        sb.append("<!doctype html><html lang='").append(esc(a.optString("lang"))).append("'").append(if (dark) " class='dark'" else "").append("><head><meta charset='utf-8'>")
        sb.append("<meta name='viewport' content='width=device-width,initial-scale=1'><title>").append(esc(title)).append("</title><style>")
        sb.append(":root{--ink:#24212b;--muted:#6f6a78;--accent:#71608c;--canvas:#faf8f5;--panel:#efebf3;--line:#e3dee8;--size:").append(size).append("px}")
        sb.append("html.dark{--ink:#ece8f2;--muted:#a7a0b3;--accent:#b6a4d6;--canvas:#1c1a20;--panel:#2a2730;--line:#38343f}")
        sb.append("html{background:var(--canvas);color:var(--ink);-webkit-text-size-adjust:100%}")
        sb.append("body{margin:0;padding:72px 22px 120px;font:var(--size)/1.62 ").append(if (serif) (if (vietnamese) "'Noto Serif',serif" else "Georgia,'Noto Serif',serif") else "system-ui,sans-serif").append("}")
        sb.append("main{max-width:40em;margin:0 auto}.kicker{font:600 11px system-ui,sans-serif;letter-spacing:.08em;color:var(--accent);text-transform:uppercase;margin:0 0 10px}")
        sb.append("h1{font:600 calc(var(--size)*1.6)/1.22 system-ui,sans-serif;margin:0 0 12px}.by{font:13px system-ui,sans-serif;color:var(--muted);margin:0 0 calc(var(--size)*1.6)}")
        sb.append("h2{font:600 calc(var(--size)*1.3)/1.3 system-ui,sans-serif;margin:1.2em 0 .5em}h3{font:600 calc(var(--size)*1.12)/1.3 system-ui,sans-serif;margin:1em 0 .4em}")
        sb.append("p{margin:0 0 .9em}ul{margin:0 0 .9em;padding-left:1.4em}li{margin:0 0 .35em}")
        sb.append("blockquote{margin:0 0 .9em;padding:2px 0 2px 1em;border-left:3px solid var(--accent);font-style:italic}")
        sb.append("pre{font:calc(var(--size)*.8)/1.45 monospace;background:var(--panel);padding:10px 14px;border-radius:10px;overflow-x:auto;white-space:pre}")
        sb.append("figure{margin:.4em 0 1.1em}img{display:block;max-width:100%;height:auto;margin:0 auto;border-radius:8px}.cap{font:13px system-ui,sans-serif;color:var(--muted);margin:0 0 1.2em}")
        sb.append(".bar{position:fixed;top:0;left:0;right:0;display:flex;gap:4px;justify-content:flex-end;padding:10px 12px;background:var(--canvas);border-bottom:1px solid var(--line);z-index:2}")
        sb.append(".bar button{font:600 14px system-ui,sans-serif;color:var(--accent);background:none;border:0;border-radius:14px;padding:8px 12px}.bar button:active{background:var(--panel)}")
        sb.append(".bar .grow{flex:1}a{color:inherit}mark{color:inherit}</style></head><body>")
        // Size, serif, save and the original, along the top.
        sb.append("<div class='bar'><button data-a='original'>").append(esc(c.getString(R.string.reader_original))).append("</button><span class='grow'></span>")
        sb.append("<button data-a='smaller' aria-label='").append(esc(c.getString(R.string.smaller_text))).append("'>A−</button>")
        sb.append("<button data-a='larger' aria-label='").append(esc(c.getString(R.string.larger_text))).append("'>A+</button>")
        sb.append("<button data-a='serif' style='font-family:Georgia,serif'>Aa</button>")
        sb.append("<button data-a='save'>").append(esc(c.getString(if (saved) R.string.reader_saved else R.string.read_later))).append("</button></div><main>")
        if (site.isNotEmpty()) sb.append("<p class='kicker'>").append(esc(site)).append("</p>")
        sb.append("<h1>").append(esc(title)).append("</h1>")
        val by = listOfNotNull(author.takeIf { it.isNotBlank() }, if (words > 0) c.getString(R.string.min_read, maxOf(1, Math.round(words / 230.0).toInt())) else null)
        if (by.isNotEmpty()) sb.append("<p class='by'>").append(esc(by.joinToString(" · "))).append("</p>")
        val blocks: JSONArray = a.optJSONArray("blocks") ?: JSONArray()
        var inList = false
        for (i in 0 until blocks.length()) {
            val b = blocks.optJSONObject(i) ?: continue
            val kind = b.optString("t")
            val text = esc(b.optString("text"))
            if (kind != "li" && inList) { sb.append("</ul>"); inList = false }
            when (kind) {
                "h2" -> sb.append("<h2>").append(text).append("</h2>")
                "h3" -> sb.append("<h3>").append(text).append("</h3>")
                "li" -> { if (!inList) { sb.append("<ul>"); inList = true }; sb.append("<li>").append(text).append("</li>") }
                "quote" -> sb.append("<blockquote>").append(text).append("</blockquote>")
                "pre" -> sb.append("<pre>").append(text).append("</pre>")
                "caption" -> sb.append("<p class='cap'>").append(text).append("</p>")
                "img" -> {
                    val src = b.optString("src")
                    if (src.startsWith("https://") || src.startsWith("http://")) sb.append("<figure><img loading='lazy' referrerpolicy='no-referrer' src=\"").append(esc(src)).append("\" alt=\"").append(text).append("\"></figure>")
                }
                else -> sb.append("<p>").append(text).append("</p>")
            }
        }
        if (inList) sb.append("</ul>")
        sb.append("</main><script>(()=>{let size=").append(size).append(",serif=").append(serif).append(";")
        sb.append("const post=m=>{try{micaReader.postMessage(m)}catch(e){}};")
        sb.append("document.querySelector('.bar').addEventListener('click',e=>{const b=e.target.closest('button');if(!b)return;const a=b.dataset.a;")
        sb.append("if(a==='smaller'||a==='larger'){size=Math.max(14,Math.min(30,size+(a==='larger'?1:-1)));document.documentElement.style.setProperty('--size',size+'px');post('size:'+size)}")
        sb.append("else if(a==='serif'){post('serif:'+!serif)}else post(a)});")
        sb.append("addEventListener('micatheme',()=>{});})()</script></body></html>")
        return sb.toString()
    }
}
