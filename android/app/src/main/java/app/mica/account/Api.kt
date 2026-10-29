package app.mica.account

import app.mica.blocker.Json
import java.io.ByteArrayOutputStream
import java.net.HttpURLConnection
import java.net.URL

/** A failed call: the HTTP status and the service's {"error": code}, when it sent one. */
class ApiException(val status: Int, val code: String?) : Exception("Sync service error $status${code?.let { " ($it)" } ?: ""}")

/**
 * The Mica sync service (desktop's VaultApi): JSON in and out, no redirects, 15 s timeouts, 2 MiB at most
 * either way. Call off the main thread.
 */
object Api {
    const val BASE = "https://the-api.lessnoise.online"
    private const val LIMIT = 2 * 1024 * 1024

    fun call(method: String, route: String, body: Any? = null, token: String? = null, timeoutMs: Int = 15000): Map<*, *> {
        val c = URL(BASE + route).openConnection() as HttpURLConnection
        try {
            c.requestMethod = method
            c.instanceFollowRedirects = false
            c.connectTimeout = timeoutMs; c.readTimeout = timeoutMs
            c.useCaches = false
            c.setRequestProperty("Accept", "application/json")
            if (token != null) c.setRequestProperty("Authorization", "Bearer $token")
            if (body != null) {
                val bytes = JsonOut.write(body).toByteArray(Charsets.UTF_8)
                if (bytes.size > LIMIT) throw IllegalStateException("Too large to sync (2 MB limit).")
                c.doOutput = true
                c.setRequestProperty("Content-Type", "application/json")
                c.setFixedLengthStreamingMode(bytes.size)
                c.outputStream.use { it.write(bytes) }
            }
            val status = c.responseCode
            val text = read(if (status in 200..299) c.inputStream else c.errorStream)
            if (status !in 200..299) throw ApiException(status, (try { Json.parse(text) as? Map<*, *> } catch (e: Exception) { null })?.get("error") as? String)
            return Json.parse(text.ifEmpty { "{}" }) as? Map<*, *> ?: emptyMap<String, Any>()
        } finally { c.disconnect() }
    }

    private fun read(stream: java.io.InputStream?): String {
        if (stream == null) return ""
        stream.use { s ->
            val out = ByteArrayOutputStream()
            val buf = ByteArray(8192)
            while (true) {
                val n = s.read(buf)
                if (n < 0) break
                if (out.size() + n > LIMIT) throw IllegalStateException("Sync response too large.")
                out.write(buf, 0, n)
            }
            return out.toString("UTF-8")
        }
    }
}
