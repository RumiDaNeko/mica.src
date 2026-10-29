package app.mica

import android.content.Context
import android.webkit.WebResourceResponse
import java.io.ByteArrayInputStream

// The built-in games, served from assets/games (desktop's src\*.html, copied at build time) on hosts that
// never resolve, the same way BrowserGames.cs does: the page arrives already dark when Mica is dark, and a
// theme change flips open pages in place.
object Games {
    @Volatile var dark = false

    const val THEME_SCRIPT = "document.documentElement.classList.toggle('dark',%s);dispatchEvent(new Event('micatheme'))"

    private val headers = mapOf(
        "Cache-Control" to "no-store",
        "Content-Security-Policy" to "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:",
    )

    fun serve(context: Context, url: String, host: String, method: String?): WebResourceResponse {
        val path = url.substringAfter(host).substringBefore('?').substringBefore('#').trim('/')
        val page = when {
            method != null && method != "GET" -> null
            host == Navigation.TETRIS_HOST -> if (path.isEmpty()) "Tetris.html" else null
            else -> Navigation.games.firstOrNull { it.path == path }?.page
        } ?: return WebResourceResponse("text/plain", "utf-8", 404, "Not Found", headers, ByteArrayInputStream(ByteArray(0)))
        var html = context.assets.open("games/$page").use { it.readBytes() }.toString(Charsets.UTF_8)
        if (dark) html = html.replaceFirst("<html lang=\"en\">", "<html lang=\"en\" class=\"dark\">")
        return WebResourceResponse("text/html", "utf-8", 200, "OK", headers, ByteArrayInputStream(html.toByteArray()))
    }
}
