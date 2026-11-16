package app.mica

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import app.mica.account.Account
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest

/**
 * The signed-in account's Discord picture and banner, as desktop shows them: only from Discord's public CDN,
 * one cached file per kind (a new one replaces the old), decoded no wider than needed.
 */
object DiscordImages {
    private val idPattern = Regex("^[0-9]{1,20}$")
    private val hashPattern = Regex("^(a_)?[0-9a-f]{32}$")
    private const val LIMIT = 2 * 1024 * 1024
    /** Decoded pictures by address, touched on the main thread only, so a re-render doesn't decode again. */
    private val memory = HashMap<String, Bitmap>()

    fun avatarUrl(u: Account.User): String =
        if (u.avatar != null && hashPattern.matches(u.avatar) && idPattern.matches(u.id)) "https://cdn.discordapp.com/avatars/${u.id}/${u.avatar}.png?size=128"
        else "https://cdn.discordapp.com/embed/avatars/${u.id.toULongOrNull()?.let { (it shr 22) % 6u } ?: 0u}.png"

    fun bannerUrl(u: Account.User): String? =
        if (u.banner != null && hashPattern.matches(u.banner) && idPattern.matches(u.id)) "https://cdn.discordapp.com/banners/${u.id}/${u.banner}.png?size=600" else null

    fun cached(url: String): Bitmap? = memory[url]

    /** Main thread: [shown] gets the picture (from memory, the disk cache or the CDN) or nothing. */
    fun load(context: Context, account: Account, url: String, folder: String, width: Int, shown: (Bitmap) -> Unit) {
        memory[url]?.let { return shown(it) }
        val app = context.applicationContext
        account.bg({ fetch(app, url, folder, width) }) { b, _ -> if (b != null) { memory[url] = b; shown(b) } }
    }

    private fun fetch(context: Context, url: String, folder: String, width: Int): Bitmap? {
        val dir = File(context.cacheDir, "discord-$folder")
        val key = MessageDigest.getInstance("SHA-256").digest(url.toByteArray()).joinToString("") { "%02x".format(it) }
        val file = File(dir, "$key.png")
        if (!file.exists()) {
            val u = URL(url)
            if (u.protocol != "https" || u.host != "cdn.discordapp.com") return null
            val c = u.openConnection() as HttpURLConnection
            val data = try {
                c.instanceFollowRedirects = false; c.connectTimeout = 10000; c.readTimeout = 10000
                if (c.responseCode != 200) return null
                c.inputStream.use { s ->
                    val out = java.io.ByteArrayOutputStream(); val buf = ByteArray(16384)
                    while (true) { val n = s.read(buf); if (n < 0) break; out.write(buf, 0, n); if (out.size() > LIMIT) return null }
                    out.toByteArray()
                }
            } finally { c.disconnect() }
            if (data.isEmpty()) return null
            dir.mkdirs()
            file.writeBytes(data)
            dir.listFiles()?.forEach { if (it.name != file.name) it.delete() }
        }
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeFile(file.path, bounds)
        if (bounds.outWidth <= 0) { file.delete(); return null }
        var sample = 1
        while (bounds.outWidth / (sample * 2) >= width) sample *= 2
        return BitmapFactory.decodeFile(file.path, BitmapFactory.Options().apply { inSampleSize = sample }) ?: run { file.delete(); null }
    }
}
