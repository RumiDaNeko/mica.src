package app.mica.account

import app.mica.blocker.Json
import java.net.URI
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.Base64
import javax.crypto.Cipher
import javax.crypto.Mac
import javax.crypto.spec.IvParameterSpec
import javax.crypto.spec.SecretKeySpec

// Desktop's vault, byte for byte (src\VaultModel.cs), so a vault synced from Windows opens here and back:
// PBKDF2-HMAC-SHA256(UTF-8 password, 16-byte salt, 600,000 rounds, 64 bytes) → AES-256-CBC/PKCS7 key
// (first half) and HMAC-SHA256 key (second half); the MAC covers "MicaVault\n1\nPBKDF2-SHA256\n600000\n"
// + salt + "\n" + iv + "\n" + ciphertext (standard base64), checked before decrypting.
class VaultException(message: String) : Exception(message)

class Envelope(val format: Int, val kdf: String, val iterations: Int, val salt: String, val iv: String, val ciphertext: String, val mac: String) {
    fun toJson(): String = JsonOut.write(mapOf("format" to format, "kdf" to kdf, "iterations" to iterations, "salt" to salt, "iv" to iv, "ciphertext" to ciphertext, "mac" to mac))
    fun toMap(): Map<String, Any?> = mapOf("format" to format, "kdf" to kdf, "iterations" to iterations, "salt" to salt, "iv" to iv, "ciphertext" to ciphertext, "mac" to mac)

    companion object {
        /** Only the seven authenticated fields; a local inbox (desktop-only) is ignored. */
        fun from(map: Map<*, *>) = Envelope(
            (map["format"] as? Double)?.toInt() ?: 0, map["kdf"] as? String ?: "", (map["iterations"] as? Double)?.toInt() ?: 0,
            map["salt"] as? String ?: "", map["iv"] as? String ?: "", map["ciphertext"] as? String ?: "", map["mac"] as? String ?: "")
        fun parse(json: String) = from(Json.parse(json) as Map<*, *>)
    }
}

object VaultCrypto {
    const val ITERATIONS = 600000
    const val KDF = "PBKDF2-SHA256"
    private const val KEY_PREFIX = "\u0001MicaVaultKey1:"
    private val random = SecureRandom()
    private val b64 = Base64.getEncoder()
    private val unb64 = Base64.getDecoder()

    fun randomBytes(n: Int) = ByteArray(n).also { random.nextBytes(it) }
    fun base64(bytes: ByteArray): String = b64.encodeToString(bytes)
    fun base64Url(bytes: ByteArray): String = Base64.getUrlEncoder().withoutPadding().encodeToString(bytes)

    /** Sign-in: a random verifier and its SHA-256 challenge (both unpadded base64url). */
    fun randomVerifier() = base64Url(randomBytes(32))
    fun challenge(verifier: String) = base64Url(MessageDigest.getInstance("SHA-256").digest(verifier.toByteArray(Charsets.US_ASCII)))

    /** The site part of an HTTPS address as the vault keeps it: lower-case https://host[:port]. */
    fun origin(value: String): String {
        val u = try { URI(value.trim()) } catch (e: Exception) { throw IllegalArgumentException("Enter an HTTPS website address without user information.") }
        if (u.scheme?.lowercase() != "https" || u.host.isNullOrEmpty() || u.rawUserInfo != null) throw IllegalArgumentException("Enter an HTTPS website address without user information.")
        val port = if (u.port == -1 || u.port == 443) "" else ":${u.port}"
        return "https://${u.host}$port".lowercase()
    }

    fun originOrNull(value: String) = try { origin(value) } catch (e: IllegalArgumentException) { null }

    /** PBKDF2-HMAC-SHA256 over the UTF-8 bytes (the same as .NET's Rfc2898DeriveBytes and CNG). */
    fun pbkdf2(password: ByteArray, salt: ByteArray, iterations: Int, length: Int): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(password, "HmacSHA256"))
        val out = ByteArray(length)
        val u = ByteArray(32)
        val t = ByteArray(32)
        var block = 1
        var off = 0
        while (off < length) {
            mac.update(salt)
            mac.update(byteArrayOf((block ushr 24).toByte(), (block ushr 16).toByte(), (block ushr 8).toByte(), block.toByte()))
            mac.doFinal(u, 0)
            u.copyInto(t)
            for (i in 1 until iterations) {
                mac.update(u)
                mac.doFinal(u, 0)
                for (j in 0 until 32) t[j] = (t[j].toInt() xor u[j].toInt()).toByte()
            }
            val n = minOf(32, length - off)
            t.copyInto(out, off, 0, n)
            off += n; block++
        }
        return out
    }

    private fun derive(password: String, salt: ByteArray): ByteArray {
        if (password.isEmpty()) throw VaultException("Enter your vault master password.")
        val secret = password.toByteArray(Charsets.UTF_8)
        try { return pbkdf2(secret, salt, ITERATIONS, 64) } finally { secret.fill(0) }
    }

    fun isKey(secret: String?) = secret != null && secret.startsWith(KEY_PREFIX)
    private fun keyText(salt: ByteArray, key: ByteArray) = KEY_PREFIX + base64(salt) + ":" + base64(key)
    private fun keyPart(secret: String, index: Int, size: Int): ByteArray {
        val parts = secret.substring(KEY_PREFIX.length).split(':')
        if (parts.size != 2) throw VaultException("Invalid vault key.")
        return decode(parts[index], size)
    }

    /** A password or a vault key → the 64-byte key for this salt. */
    private fun key(secret: String, salt: ByteArray): ByteArray {
        if (!isKey(secret)) return derive(secret, salt)
        if (base64(keyPart(secret, 0, 16)) != base64(salt)) throw VaultException("Wrong master password or damaged vault.")
        return keyPart(secret, 1, 64)
    }

    /** A vault key for a new master password (new salt). */
    fun newKey(password: String): String { val salt = randomBytes(16); return keyText(salt, derive(password, salt)) }
    /** A random vault key, for a vault created before the user picks a master password. */
    fun newDeviceKey(): String = keyText(randomBytes(16), randomBytes(64))

    /** Checks a password (or key) against the vault and returns its vault key. */
    fun keyFor(e: Envelope, secret: String): String {
        validate(e)
        val salt = decode(e.salt, 16)
        val key = key(secret, salt)
        authenticate(e, key)
        return if (isKey(secret)) secret else keyText(salt, key)
    }

    private fun canonical(e: Envelope) = ("MicaVault\n" + e.format + "\n" + e.kdf + "\n" + e.iterations + "\n" + e.salt + "\n" + e.iv + "\n" + e.ciphertext).toByteArray(Charsets.UTF_8)

    private fun hmac(key: ByteArray, data: ByteArray): ByteArray = Mac.getInstance("HmacSHA256").run { init(SecretKeySpec(key, "HmacSHA256")); doFinal(data) }

    private fun authenticate(e: Envelope, key: ByteArray) {
        val expected = hmac(key.copyOfRange(32, 64), canonical(e))
        if (!MessageDigest.isEqual(expected, decode(e.mac, 32))) throw VaultException("Wrong master password or damaged vault.")
    }

    /** Standard padded base64 that re-encodes to itself, of an exact size when [size] > 0. */
    private fun decode(s: String, size: Int): ByteArray {
        val b = try { unb64.decode(s) } catch (e: IllegalArgumentException) { throw VaultException("Invalid vault format.") }
        if ((size > 0 && b.size != size) || base64(b) != s) throw VaultException("Invalid vault format.")
        return b
    }

    fun validate(e: Envelope) {
        if (e.format != 1 || e.kdf != KDF || e.iterations != ITERATIONS) throw VaultException("Unsupported vault format.")
        decode(e.salt, 16); decode(e.iv, 16); decode(e.mac, 32)
        val c = decode(e.ciphertext, 0)
        if (c.isEmpty() || c.size % 16 != 0 || c.size > 4 * 1024 * 1024) throw VaultException("Invalid vault size.")
    }

    fun encrypt(payload: VaultPayload, secret: String): Envelope {
        payload.validate()
        val salt = if (isKey(secret)) keyPart(secret, 0, 16) else randomBytes(16)
        val iv = randomBytes(16)
        val key = key(secret, salt)
        val plain = payload.toJson().toByteArray(Charsets.UTF_8)
        try {
            val cipher = Cipher.getInstance("AES/CBC/PKCS5Padding")
            cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(key.copyOfRange(0, 32), "AES"), IvParameterSpec(iv))
            val partial = Envelope(1, KDF, ITERATIONS, base64(salt), base64(iv), base64(cipher.doFinal(plain)), "")
            val mac = base64(hmac(key.copyOfRange(32, 64), canonical(partial)))
            return Envelope(1, KDF, ITERATIONS, partial.salt, partial.iv, partial.ciphertext, mac)
        } finally { key.fill(0); plain.fill(0) }
    }

    fun decrypt(e: Envelope, secret: String): VaultPayload {
        validate(e)
        val key = key(secret, decode(e.salt, 16))
        try {
            authenticate(e, key)
            val cipher = Cipher.getInstance("AES/CBC/PKCS5Padding")
            cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(key.copyOfRange(0, 32), "AES"), IvParameterSpec(decode(e.iv, 16)))
            val plain = cipher.doFinal(decode(e.ciphertext, 0))
            return VaultPayload.parse(plain.toString(Charsets.UTF_8)).also { plain.fill(0); it.validate() }
        } finally { key.fill(0) }
    }
}
