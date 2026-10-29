package app.mica.account

import java.net.URI
import java.net.URLDecoder
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/**
 * Authenticator codes (RFC 6238), ported from desktop's src\Totp.cs: a site's 2FA setup key, kept with its
 * login in the vault as a normalized otpauth:// link, gives the code for the current time step.
 */
object Totp {
    class Key(val secret: ByteArray, val digits: Int = 6, val period: Int = 30, val algorithm: String = "SHA1", val issuer: String? = null, val account: String? = null)

    private const val ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"

    /** Throws IllegalArgumentException for anything that isn't a usable key. */
    fun parse(text: String?): Key {
        if (text.isNullOrBlank()) throw IllegalArgumentException("Enter the site's 2FA setup key.")
        val t = text.trim()
        if (!t.startsWith("otpauth://", true)) return check(Key(base32(t)))
        val uri = try { URI(t) } catch (e: Exception) { throw IllegalArgumentException("Only time-based (TOTP) 2FA keys are supported.") }
        if (!uri.host.equals("totp", true)) throw IllegalArgumentException("Only time-based (TOTP) 2FA keys are supported.")
        val label = (uri.path ?: "").trimStart('/')
        var issuer: String? = null
        var account: String? = null
        val colon = label.indexOf(':')
        if (colon >= 0) { issuer = label.substring(0, colon).trim(); account = label.substring(colon + 1).trim() } else if (label.isNotEmpty()) account = label.trim()
        var secret: String? = null
        var digits = 6; var period = 30; var algorithm = "SHA1"
        for (pair in (uri.rawQuery ?: "").split('&')) {
            val at = pair.indexOf('=')
            if (at <= 0) continue
            val name = pair.substring(0, at).lowercase()
            val value = URLDecoder.decode(pair.substring(at + 1), "UTF-8")
            when (name) {
                "secret" -> secret = value
                "issuer" -> if (value.isNotEmpty()) issuer = value
                "digits" -> value.toIntOrNull()?.let { digits = it }
                "period" -> value.toIntOrNull()?.let { period = it }
                "algorithm" -> algorithm = value.uppercase()
            }
        }
        return check(Key(base32(secret), digits, period, algorithm, issuer, account))
    }

    private fun check(k: Key): Key {
        if (k.digits < 6 || k.digits > 8) throw IllegalArgumentException("This 2FA key asks for ${k.digits}-digit codes, which Mica can't make.")
        if (k.period < 10 || k.period > 300) throw IllegalArgumentException("This 2FA key has an unusual time step.")
        if (k.algorithm !in setOf("SHA1", "SHA256", "SHA512")) throw IllegalArgumentException("This 2FA key uses an algorithm Mica doesn't know.")
        return k
    }

    fun parseOrNull(text: String?) = try { parse(text) } catch (e: IllegalArgumentException) { null }

    /** Spaces, dashes and padding are ignored; letters may be any case. */
    fun base32(text: String?): ByteArray {
        val clean = (text ?: "").filter { !it.isWhitespace() && it != '-' && it != '=' }.uppercase()
        if (clean.length < 16 || clean.length > 128) throw IllegalArgumentException("That doesn't look like a 2FA setup key.")
        val bytes = ByteArray(clean.length * 5 / 8)
        var buffer = 0; var bits = 0; var index = 0
        for (c in clean) {
            val v = ALPHABET.indexOf(c)
            if (v < 0) throw IllegalArgumentException("That doesn't look like a 2FA setup key.")
            buffer = (buffer shl 5) or v; bits += 5
            if (bits >= 8) { bytes[index++] = (buffer shr (bits - 8)).toByte(); bits -= 8; buffer = buffer and ((1 shl bits) - 1) }
        }
        return bytes
    }

    private fun toBase32(bytes: ByteArray): String {
        val sb = StringBuilder()
        var buffer = 0; var bits = 0
        for (b in bytes) {
            buffer = (buffer shl 8) or (b.toInt() and 0xFF); bits += 8
            while (bits >= 5) { sb.append(ALPHABET[(buffer shr (bits - 5)) and 31]); bits -= 5 }
            buffer = buffer and ((1 shl bits) - 1)
        }
        if (bits > 0) sb.append(ALPHABET[(buffer shl (5 - bits)) and 31])
        return sb.toString()
    }

    /** .NET's Uri.EscapeDataString: everything but RFC 3986 unreserved characters is %-escaped. */
    private fun escape(s: String): String {
        val sb = StringBuilder()
        for (b in s.toByteArray(Charsets.UTF_8)) {
            val c = b.toInt() and 0xFF
            if (c in 'A'.code..'Z'.code || c in 'a'.code..'z'.code || c in '0'.code..'9'.code || c == '-'.code || c == '_'.code || c == '.'.code || c == '~'.code) sb.append(c.toChar())
            else sb.append('%').append(String.format("%02X", c))
        }
        return sb.toString()
    }

    /** How a key is kept in the vault: one form, whatever it was saved from. */
    fun normalize(k: Key) = "otpauth://totp/" + escape(k.account ?: "") + "?secret=" + toBase32(k.secret) + "&digits=" + k.digits + "&period=" + k.period +
        "&algorithm=" + k.algorithm + (if (k.issuer.isNullOrEmpty()) "" else "&issuer=" + escape(k.issuer))

    fun code(k: Key, unixSeconds: Long): String {
        val step = Math.floorDiv(unixSeconds, k.period.toLong())
        val counter = ByteArray(8) { i -> (step ushr (56 - 8 * i)).toByte() }
        val name = when (k.algorithm) { "SHA256" -> "HmacSHA256"; "SHA512" -> "HmacSHA512"; else -> "HmacSHA1" }
        val hash = Mac.getInstance(name).run { init(SecretKeySpec(k.secret, name)); doFinal(counter) }
        val offset = hash[hash.size - 1].toInt() and 0x0F
        val binary = ((hash[offset].toInt() and 0x7F) shl 24) or ((hash[offset + 1].toInt() and 0xFF) shl 16) or ((hash[offset + 2].toInt() and 0xFF) shl 8) or (hash[offset + 3].toInt() and 0xFF)
        var mod = 1; repeat(k.digits) { mod *= 10 }
        return (binary % mod).toString().padStart(k.digits, '0')
    }

    fun code(saved: String?, now: Long = System.currentTimeMillis() / 1000): String? = parseOrNull(saved)?.let { code(it, now) }
    fun secondsLeft(saved: String?, now: Long = System.currentTimeMillis() / 1000): Int = parseOrNull(saved)?.let { it.period - (now % it.period).toInt() } ?: 0
}
