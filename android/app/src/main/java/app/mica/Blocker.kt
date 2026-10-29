package app.mica

import android.content.Context
import android.os.Process
import android.util.Log
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import app.mica.blocker.Domains
import app.mica.blocker.Engine
import app.mica.blocker.Request
import app.mica.blocker.ResourceType
import app.mica.blocker.Rule
import app.mica.blocker.RuleCache
import java.io.ByteArrayInputStream
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import kotlin.concurrent.thread

// The WebView side of the blocker: loads the lists desktop ships (assets/blocker, copied from
// extensions\mica-blocker\rules at build time) and answers shouldInterceptRequest.
object Blocker {
    @Volatile var engine: Engine? = null
        private set
    @Volatile var lists = 0
        private set
    @Volatile var enabled = true
    /** Sites the user lets show ads (desktop's excluded list). */
    @Volatile var allowed: Set<String> = emptySet()
    private val loaded = CountDownLatch(1)
    private const val WAIT_MS = 3000L

    fun load(context: Context) {
        val assets = context.applicationContext.assets
        thread(name = "mica-blocker") {
            // Pages wait for this on a cold start, so it runs at normal priority.
            Process.setThreadPriority(Process.THREAD_PRIORITY_DEFAULT)
            val t0 = System.nanoTime()
            try {
                // The lists change only with the app, so the parsed rules are kept per build (RuleCache).
                val build = try { context.packageManager.getPackageInfo(context.packageName, 0).lastUpdateTime } catch (e: Exception) { 0L }
                val cache = java.io.File(context.noBackupFilesDir, "blocker-$build.bin")
                val cached = if (cache.exists()) try {
                    java.io.DataInputStream(cache.inputStream().buffered(1 shl 16)).use { RuleCache.read(it) }
                } catch (e: Exception) { null } else null
                val rules: List<Rule>
                val excluded: List<String>
                if (cached != null) {
                    rules = cached.first; lists = cached.second; excluded = cached.third
                } else {
                    fun read(path: String) = assets.open(path).use { it.readBytes().toString(Charsets.UTF_8) }
                    val (names, ex) = Engine.parseConfig(read("blocker/default.json"))
                    rules = names.flatMap { name ->
                        try { Engine.parseList(read("blocker/$name.json")) } catch (e: Exception) { Log.w("Mica", "blocker list $name: $e"); emptyList() }
                    }
                    excluded = ex; lists = names.size
                    // Before Engine files the rules (it rewrites them as it goes); older builds' files go.
                    try {
                        context.noBackupFilesDir.listFiles { f -> f.name.startsWith("blocker-") && f.name.endsWith(".bin") }?.forEach { it.delete() }
                        val tmp = java.io.File(context.noBackupFilesDir, cache.name + ".tmp")
                        java.io.DataOutputStream(tmp.outputStream().buffered(1 shl 16)).use { RuleCache.write(it, rules, lists, excluded) }
                        tmp.renameTo(cache)
                    } catch (e: Exception) { Log.w("Mica", "blocker cache: $e") }
                }
                engine = Engine(rules, excluded)
                Log.i("Mica", "blocker: ${rules.size} rules from $lists lists in ${(System.nanoTime() - t0) / 1_000_000} ms" + if (cached != null) " (cached)" else "")
            } catch (e: Exception) {
                Log.e("Mica", "blocker failed to load", e)
            } finally {
                loaded.countDown()
            }
        }
    }

    fun isAllowedSite(host: String) = allowed.isNotEmpty() && Domains.matches(allowed, host)

    /**
     * A response that blocks [request], or null to let it through. Requests that arrive before the lists
     * have loaded wait for them, up to 3 seconds, then go through.
     */
    fun check(tab: Tab, request: WebResourceRequest, blockedPage: (String) -> String): WebResourceResponse? {
        if (!enabled) return null
        // On a cold start the page's first requests can beat the lists; WebView asks on its own network
        // threads, so waiting a moment here doesn't block the UI.
        val e = engine ?: if (loaded.await(WAIT_MS, TimeUnit.MILLISECONDS)) engine ?: return null else return null
        val url = request.url.toString()
        if (!Navigation.isWeb(url) && !url.startsWith("ws", true)) return null
        val page = if (request.isForMainFrame) null else tab.pageHost
        if (isAllowedSite(page ?: Domains.hostOf(url))) return null
        val type = ResourceType.guess(url, request.isForMainFrame, request.requestHeaders)
        if (!e.blocks(Request(url, type, request.method ?: "GET", page))) return null
        tab.blocked.incrementAndGet()
        if (BuildConfig.DEBUG) Log.d("MicaBlock", "blocked on ${page ?: "-"}: $url")
        if (request.isForMainFrame) {
            val html = blockedPage(Domains.hostOf(url)).toByteArray()
            return WebResourceResponse("text/html", "utf-8", 200, "OK", emptyMap(), ByteArrayInputStream(html))
        }
        return WebResourceResponse("text/plain", "utf-8", 204, "No Content", emptyMap(), ByteArrayInputStream(ByteArray(0)))
    }
}
