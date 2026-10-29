package app.mica

import java.net.URLEncoder

// Ported from Navigation and Spotlight in src\Core.cs: what an address means, and what Spotlight shows.
object Navigation {
    const val HOME = "mica://home"
    const val TETRIS = "mica://tetris"
    const val TETRIS_HOST = "tetris.mica.invalid"
    const val GAMES_HOST = "games.mica.invalid"

    /** Address, title, path on GAMES_HOST, page in assets/games. Same list as Navigation.Games on desktop. */
    class Game(val address: String, val title: String, val path: String, val page: String)

    val games = listOf(
        Game("mica://games", "Games", "", "GameHub.html"),
        Game("mica://snake", "Snake", "snake", "Snake.html"),
        Game("mica://2048", "2048", "2048", "Game2048.html"),
        Game("mica://minesweeper", "Minesweeper", "minesweeper", "Minesweeper.html"),
        Game("mica://breakout", "Breakout", "breakout", "Breakout.html"),
        Game("mica://sudoku", "Sudoku", "sudoku", "Sudoku.html"),
        Game("mica://memory", "Memory", "memory", "Memory.html"),
        Game("mica://flappy", "Flappy", "flappy", "Flappy.html"),
        Game("mica://pong", "Pong", "pong", "Pong.html"),
        Game("mica://invaders", "Invaders", "invaders", "Invaders.html"),
        Game("mica://connect4", "Connect Four", "connect4", "ConnectFour.html"),
    )

    fun game(address: String) = games.firstOrNull { it.address == address }

    fun isInternal(value: String) = value == HOME || value == TETRIS || game(value) != null || Reader.isReader(value)

    /** The address the engine loads for a mica:// address. */
    fun engineAddress(value: String): String {
        if (value == TETRIS) return "https://$TETRIS_HOST/"
        if (Reader.isReader(value)) return Reader.engineAddress(value)
        val g = game(value) ?: return value
        return "https://$GAMES_HOST/${g.path}"
    }

    /** The mica:// address for an engine address, or the address itself. */
    fun fromEngine(value: String): String {
        val host = hostOf(value)
        if (!value.startsWith("https://")) return value
        if (host == TETRIS_HOST) return TETRIS
        if (host == Reader.HOST) return Reader.fromEngine(value)
        if (host == GAMES_HOST) {
            val path = value.substringAfter(GAMES_HOST).substringBefore('?').substringBefore('#').trim('/')
            games.firstOrNull { it.path == path }?.let { return it.address }
        }
        return value
    }

    fun isGameHost(host: String) = host == GAMES_HOST || host == TETRIS_HOST

    fun hostOf(url: String) = app.mica.blocker.Domains.hostOf(url)

    /** What the island shows: the host without www., or the mica:// address. */
    fun label(url: String): String {
        val shown = fromEngine(url)
        if (Reader.isReader(shown)) return Reader.site(shown)
        if (shown.startsWith("mica://")) return shown
        val host = hostOf(url)
        return if (host.isEmpty()) url else host.removePrefix("www.")
    }

    fun isWeb(url: String) = url.startsWith("http://", true) || url.startsWith("https://", true)

    private val localhost = Regex("^(localhost|127\\.0\\.0\\.1|\\[::1])(:\\d+)?([/?#].*)?$", RegexOption.IGNORE_CASE)
    private val domainLike = Regex("^([\\p{L}\\d-]+\\.)+[\\p{L}\\d-]+(:\\d+)?([/?#].*)?$")
    private val schemeLike = Regex("^[a-z][a-z0-9+.-]*:", RegexOption.IGNORE_CASE)

    fun searchUrl(q: String) = "https://www.google.com/search?q=" + URLEncoder.encode(q, "UTF-8")
    fun isSearch(url: String) = url.startsWith("https://www.google.com/search?q=")

    /** What typing [input] opens: a mica:// address, a web address, or a search. Null for other schemes. */
    fun resolve(input: String): String? {
        val q = input.trim()
        if (q.isEmpty()) return HOME
        val lower = q.lowercase().trimEnd('/')
        if (isInternal(lower)) return lower
        if (localhost.matches(q)) return "http://$q"
        if (q.none { it.isWhitespace() } && domainLike.matches(q)) return "https://$q"
        if (schemeLike.containsMatchIn(q)) return if (isWeb(q)) q else null
        return searchUrl(q)
    }

    val keywords = mapOf(
        "yt" to "https://www.youtube.com/results?search_query=%s", "gh" to "https://github.com/search?q=%s",
        "w" to "https://en.wikipedia.org/w/index.php?search=%s", "maps" to "https://www.google.com/maps/search/%s",
        "g" to "https://www.google.com/search?q=%s", "so" to "https://stackoverflow.com/search?q=%s",
        "r" to "https://www.reddit.com/search/?q=%s", "amz" to "https://www.amazon.com/s?k=%s",
    )

    /** "yt cats" → YouTube's search for cats. */
    fun expandKeyword(q: String): String? {
        val space = q.trim().indexOf(' ')
        if (space <= 0) return null
        val template = keywords[q.trim().substring(0, space).lowercase()] ?: return null
        val rest = q.trim().substring(space + 1).trim()
        if (rest.isEmpty()) return null
        return template.replace("%s", URLEncoder.encode(rest, "UTF-8"))
    }

    class Entry(val url: String, val title: String, val tabId: String? = null, val detail: String = "")

    enum class Kind { TAB, BOOKMARK, HISTORY, SEARCH, OPEN, SHORTCUT }

    class Hit(val kind: Kind, val title: String, val detail: String, val url: String, val tabId: String? = null)

    /**
     * Spotlight's results: tabs from every space, then history, each word typed appearing in the title or
     * address; the typed address or search goes first, as desktop does.
     */
    fun search(query: String, tabs: List<Entry>, history: List<Entry>, searchTitle: (String) -> String, goTitle: (String) -> String, bookmarks: List<Entry> = emptyList()): List<Hit> {
        val q = query.trim()
        val words = q.lowercase().split(' ').filter { it.isNotEmpty() }
        val hits = ArrayList<Hit>()
        val seen = HashSet<String>()
        fun add(list: List<Entry>, kind: Kind) {
            for (e in list) {
                if (hits.size >= 7) return
                if (e.url == HOME || e.url.isEmpty()) continue
                val text = (e.title + " " + fromEngine(e.url)).lowercase()
                if (words.isNotEmpty() && !words.all { text.contains(it) }) continue
                if (!seen.add(e.url)) continue
                hits.add(Hit(kind, e.title.ifBlank { label(e.url) }, e.detail.ifEmpty { label(e.url) }, e.url, e.tabId))
            }
        }
        add(tabs, Kind.TAB)
        add(bookmarks, Kind.BOOKMARK)
        add(history, Kind.HISTORY)
        if (q.isEmpty()) return hits
        val keyword = expandKeyword(q)
        if (keyword != null) {
            hits.removeAll { it.url == keyword }
            hits.add(0, Hit(Kind.SHORTCUT, searchTitle(q.substring(q.indexOf(' ') + 1).trim()), hostOf(keyword).removePrefix("www."), keyword))
        }
        val url = resolve(q) ?: return hits
        val typed = hits.firstOrNull { it.url == url }?.also { hits.remove(it) }
            ?: if (isSearch(url)) Hit(Kind.SEARCH, searchTitle(q), "Google", url) else Hit(Kind.OPEN, goTitle(q), label(url), url)
        val keepTop = keyword != null || (hits.isNotEmpty() && typed.kind == Kind.SEARCH && startsSiteAddress(hits[0].url, q))
        hits.add(if (keepTop) 1 else 0, typed)
        return hits
    }

    // Only the start of a site's address ("git" for github.com) keeps that site above the search.
    private fun startsSiteAddress(url: String, q: String): Boolean {
        if (q.any { it.isWhitespace() }) return false
        var host = hostOf(url)
        if (host.isEmpty()) return false
        var typed = q.lowercase().removePrefix("https://").removePrefix("http://")
        if (host.startsWith("www.") && !typed.startsWith("www.")) host = host.substring(4)
        val rest = url.substringAfter("://").substringAfter('/', "")
        return ("$host/$rest").startsWith(typed) || host.startsWith(typed)
    }
}
