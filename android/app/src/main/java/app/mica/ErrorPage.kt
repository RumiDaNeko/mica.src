package app.mica

import android.content.Context
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.net.http.SslError
import android.webkit.WebViewClient
import org.json.JSONObject

/**
 * Mica's own screen for a page that didn't load (desktop's ErrorPanel): written over WebView's grey error
 * page, so the address, Back and reload stay the page's own. Offline, it says so and offers a game.
 */
object ErrorPage {
    private val CONNECTION = setOf(WebViewClient.ERROR_HOST_LOOKUP, WebViewClient.ERROR_CONNECT, WebViewClient.ERROR_TIMEOUT,
        WebViewClient.ERROR_IO, WebViewClient.ERROR_PROXY_AUTHENTICATION)

    fun online(context: Context): Boolean {
        val cm = context.getSystemService(ConnectivityManager::class.java) ?: return true
        val caps = cm.getNetworkCapabilities(cm.activeNetwork) ?: return false
        return caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET)
    }

    /** A script that replaces the error page in place. [code] is WebViewClient's error code, [cert] SslError's primary error. */
    fun script(context: Context, url: String, code: Int, description: String, insecure: String? = null, cert: Int? = null): String {
        val offline = !online(context)
        val plain = insecure?.takeIf { !offline }
        // A broken certificate: say why, and offer to go on to that host anyway (a router's own page, an expired certificate).
        val certHost = android.net.Uri.parse(url).host?.takeIf { cert != null && plain == null && !offline && url.startsWith("https://", true) }
        val title = context.getString(when { offline -> R.string.error_offline_title; plain != null -> R.string.no_https_title; certHost != null -> R.string.cert_title; else -> R.string.error_title })
        val text = when {
            offline -> context.getString(R.string.error_offline)
            plain != null -> context.getString(R.string.no_https_text, android.net.Uri.parse(plain).host ?: plain)
            certHost != null -> context.getString(when (cert) {
                SslError.SSL_EXPIRED, SslError.SSL_NOTYETVALID, SslError.SSL_DATE_INVALID -> R.string.cert_expired
                SslError.SSL_IDMISMATCH -> R.string.cert_mismatch
                else -> R.string.cert_untrusted
            }, certHost) + " " + context.getString(R.string.cert_warning)
            else -> context.getString(R.string.error_text, description.removePrefix("net::"))
        }
        val games = plain == null && (offline || code in CONNECTION)
        val dark = Games.dark
        val canvas = if (dark) "#17151B" else "#FAF9F6"
        val ink = if (dark) "#F7F4FA" else "#2A2530"
        val muted = if (dark) "#A69AAA" else "#716B7A"
        val hover = if (dark) "#2A2631" else "#EFEBF3"
        fun esc(s: String) = s.replace("&", "&amp;").replace("<", "&lt;")
        val html = "<!doctype html><meta name=viewport content='width=device-width,initial-scale=1'><title>${esc(title)}</title><style>" +
            "html{color-scheme:${if (dark) "dark" else "light"}}body{margin:0;min-height:100vh;display:grid;place-items:center;background:$canvas;color:$ink;font:16px/1.5 sans-serif}" +
            "main{max-width:420px;padding:28px;animation:in .42s cubic-bezier(.2,.9,.25,1) both}h1{font:400 30px/1.2 serif;margin:0 0 14px}p{color:$muted;margin:0 0 22px;overflow-wrap:anywhere}" +
            "button{font:500 15px sans-serif;border:0;border-radius:999px;padding:11px 18px;margin:0 8px 8px 0;background:$hover;color:$ink;transition:transform .15s}button:active{transform:scale(.96)}" +
            "button.quiet{background:none;color:$muted}@keyframes in{from{opacity:0;transform:translateY(14px) scale(.98)}}</style>" +
            "<main><h1>${esc(title)}</h1><p>${esc(text)}</p><button id=r>${esc(context.getString(R.string.try_again))}</button>" +
            (if (plain != null) "<button class=quiet id=p>${esc(context.getString(R.string.continue_http))}</button>" else "") +
            (if (certHost != null) "<button class=quiet id=c>${esc(context.getString(R.string.cert_go, certHost))}</button>" else "") +
            (if (games) "<button class=quiet id=g>${esc(context.getString(R.string.error_games))}</button>" else "") + "</main>"
        val go = JSONObject.quote(url)
        val hub = JSONObject.quote(Navigation.engineAddress(Navigation.games[0].address))
        return "(()=>{document.open();document.write(${JSONObject.quote(html)});document.close();" +
            "document.getElementById('r').onclick=()=>location.replace($go);" +
            "const g=document.getElementById('g');if(g)g.onclick=()=>location.href=$hub;" +
            (if (plain != null) "document.getElementById('p').onclick=()=>location.href=" + JSONObject.quote("https://" + Https.CONTINUE_HOST + "/?u=" + android.net.Uri.encode(plain)) + ";" else "") +
            (if (certHost != null) "document.getElementById('c').onclick=()=>location.href=" + JSONObject.quote("https://" + Https.CERT_HOST + "/?u=" + android.net.Uri.encode(url)) + ";" else "") +
            // Back online: try again by itself.
            "addEventListener('online',()=>location.replace($go))})()"
    }
}
