package app.mica

import android.net.Uri
import app.mica.blocker.Domains

/**
 * HTTPS first (desktop's BrowserProtection): an http:// address goes to its https:// twin, unless it's on
 * your own network or a site you chose to open without HTTPS this session. Always on, as on desktop.
 */
object Https {
    /** Sites (registrable domains) you continued to over plain HTTP; until Mica closes. */
    private val allowed = HashSet<String>()

    /** The error page's "Continue without HTTPS" goes here; Mica catches it before it leaves the device. */
    const val CONTINUE_HOST = "continue-http.mica.invalid"

    private val localSuffixes = listOf(".localhost", ".local", ".test", ".internal", ".lan", ".home.arpa")

    fun allow(host: String) { allowed.add(Domains.registrable(host.lowercase())) }

    /** Hosts you went on to despite their certificate, until Mica closes. Private tabs keep their own. */
    private val certAllowed = HashSet<String>()

    /** The certificate error page's "Go to … anyway" goes here, caught like [CONTINUE_HOST]. */
    const val CERT_HOST = "continue-cert.mica.invalid"

    private fun certKey(host: String, isPrivate: Boolean) = (if (isPrivate) "private:" else "") + host.lowercase()
    fun allowCertificate(host: String, isPrivate: Boolean) { certAllowed.add(certKey(host, isPrivate)) }
    fun certificateAllowed(host: String, isPrivate: Boolean) = certKey(host, isPrivate) in certAllowed
    /** The last private tab closed: its choices go with it. */
    fun forgetPrivateCertificates() { certAllowed.removeAll { it.startsWith("private:") } }

    /** The https:// address to load instead of [url], or null to load it as it is. */
    fun upgrade(url: String): String? {
        if (!url.startsWith("http://", true)) return null
        val u = Uri.parse(url)
        val host = u.host?.lowercase() ?: return null
        if (keepsHttp(u, host) || Domains.registrable(host) in allowed) return null
        return "https" + url.substring(4)
    }

    private fun keepsHttp(u: Uri, host: String): Boolean {
        if (u.port != -1 && u.port != 80) return true
        if (!host.contains('.') || host == "localhost" || localSuffixes.any { host.endsWith(it) }) return true
        if (host.startsWith("[")) return true
        val p = host.split('.')
        if (p.size == 4 && p.all { it.toIntOrNull() in 0..255 }) {
            val a = p[0].toInt(); val b = p[1].toInt()
            return a == 10 || a == 127 || (a == 172 && b in 16..31) || (a == 192 && b == 168) || (a == 169 && b == 254) || (a == 100 && b in 64..127)
        }
        return false
    }
}
