package app.mica.account

import app.mica.blocker.Json
import java.util.UUID

/**
 * One login. [saved] is when it was last saved here or on another device (ms since 1970; 0 for logins saved
 * before sync merged), which decides which copy wins a merge. Fields Android doesn't know ride along in [extra].
 */
class VaultEntry(var id: String, var origin: String, var username: String, var password: String, var totp: String? = null,
                 var saved: Long = 0, private val extra: Map<String, Any?> = emptyMap()) {
    fun toMap(): Map<String, Any?> = LinkedHashMap(extra).apply {
        put("Id", id); put("Origin", origin); put("Username", username); put("Password", password); put("Totp", totp); put("Saved", saved)
    }

    /** Lowercase origin, newline, username: how two copies of the vault recognise the same login (as desktop's LoginKey). */
    val key get() = origin.lowercase() + "\n" + username

    fun copy(id: String = this.id) = VaultEntry(id, origin, username, password, totp, saved, extra)

    companion object {
        fun new(origin: String, username: String, password: String, totp: String? = null) =
            VaultEntry(newId(), VaultCrypto.origin(origin), username, password, totp)

        fun newId() = UUID.randomUUID().toString().replace("-", "")
    }
}

/**
 * The decrypted vault, in desktop's PascalCase JSON. What Android doesn't use (Snapshot, InboxPrivate,
 * anything newer) is kept as it came, so a round trip through Android loses nothing desktop relies on.
 */
class VaultPayload(internal val raw: MutableMap<String, Any?> = linkedMapOf("Format" to 1.0)) {
    val entries = ArrayList<VaultEntry>()
    var accountId: String?
        get() = raw["AccountId"] as? String
        set(v) { raw["AccountId"] = v }
    var revision: Long
        get() = (raw["Revision"] as? Number)?.toLong() ?: 0
        set(v) { raw["Revision"] = v }
    val hasSnapshot get() = raw["Snapshot"] is Map<*, *>
    /** Logins deleted on some device, as "origin\nusername\tms", so a merge doesn't bring them back. */
    var removed: List<String>
        get() = (raw["Removed"] as? List<*>)?.filterIsInstance<String>() ?: emptyList()
        set(v) { raw["Removed"] = v }

    /** A login was saved (now): it no longer counts as deleted. */
    fun touch(e: VaultEntry, now: Long = System.currentTimeMillis()) {
        e.saved = now
        val k = e.key + "\t"
        if (removed.any { it.startsWith(k) }) removed = removed.filterNot { it.startsWith(k) }
    }

    /** A login was deleted (now): other copies drop it too when they merge with this one. */
    fun forget(e: VaultEntry, now: Long = System.currentTimeMillis()) {
        entries.remove(e)
        forgetKey(e.key, now)
    }

    /** As [forget], for a login that is gone under its old origin or username (an edit renamed it). */
    fun forgetKey(key: String, now: Long = System.currentTimeMillis()) {
        val list = removed.filterNot { it.startsWith(key + "\t") } + (key + "\t" + now)
        removed = if (list.size > MAX_REMOVED) list.takeLast(MAX_REMOVED) else list
    }

    /**
     * Desktop's MergePayload: another copy of this vault folds into this one. Every login either has, the newest
     * save of a login both have, minus logins deleted at or after they were last saved. Returns how many changed here.
     */
    fun merge(from: VaultPayload): Int {
        val before = entries.size
        var replaced = 0
        val gone = HashMap<String, Long>()
        for (r in removed + from.removed) {
            val tab = r.lastIndexOf('\t')
            val at = if (tab > 0) r.substring(tab + 1).toLongOrNull() else null
            if (at != null) { val k = r.substring(0, tab); if ((gone[k] ?: Long.MIN_VALUE) < at) gone[k] = at }
        }
        for (e in from.entries) {
            if (e.password.isEmpty() && e.totp.isNullOrEmpty()) continue
            val same = entries.firstOrNull { it.key == e.key }
            if (same == null) { entries.add(e.copy(if (e.id.isEmpty() || entries.any { it.id == e.id }) VaultEntry.newId() else e.id)); continue }
            if (e.saved > same.saved) { same.password = e.password; same.totp = e.totp; same.saved = e.saved; replaced++ }
            else if (same.totp.isNullOrEmpty() && !e.totp.isNullOrEmpty()) same.totp = e.totp
        }
        entries.removeAll { x -> gone[x.key]?.let { it >= x.saved } == true }
        removed = gone.entries.sortedBy { it.value }.takeLast(MAX_REMOVED).map { it.key + "\t" + it.value }
        if (raw["Snapshot"] == null && from.raw["Snapshot"] != null) raw["Snapshot"] = from.raw["Snapshot"]
        return maxOf(0, entries.size - before) + replaced
    }

    /** The same logins and deletions as [other] (ids and order aside): nothing to upload or save. */
    fun sameLogins(other: VaultPayload): Boolean {
        fun sig(p: VaultPayload) = p.entries.map { listOf(it.key, it.password, it.totp ?: "", it.saved.toString()).joinToString("\u0000") }.sorted() to p.removed.sorted()
        return sig(this) == sig(other)
    }

    fun validate() {
        if ((raw["Format"] as? Number)?.toInt() != 1 || entries.size > 10000) throw IllegalArgumentException("Invalid vault payload.")
        for (e in entries) {
            if (e.origin != VaultCrypto.origin(e.origin) || e.username.length > 4096 || e.password.length > 65536 || (e.totp?.length ?: 0) > 1024)
                throw IllegalArgumentException("Invalid credential in the vault.")
        }
    }

    fun toJson(): String {
        val out = LinkedHashMap(raw)
        out["Format"] = 1
        out["Entries"] = entries.map { it.toMap() }
        return JsonOut.write(out)
    }

    companion object {
        const val MAX_REMOVED = 2000

        @Suppress("UNCHECKED_CAST")
        fun parse(json: String): VaultPayload {
            val map = (Json.parse(json) as? Map<String, Any?>)?.let { LinkedHashMap(it) } ?: throw IllegalArgumentException("Invalid vault payload.")
            val p = VaultPayload(map)
            (map["Entries"] as? List<*>)?.forEach { item ->
                val e = item as? Map<*, *> ?: return@forEach
                @Suppress("UNCHECKED_CAST")
                p.entries.add(VaultEntry(e["Id"] as? String ?: VaultEntry.newId(), e["Origin"] as? String ?: "",
                    e["Username"] as? String ?: "", e["Password"] as? String ?: "", e["Totp"] as? String, (e["Saved"] as? Number)?.toLong() ?: 0, e as Map<String, Any?>))
            }
            return p
        }
    }
}

/**
 * JSON the way desktop's JavaScriptSerializer reads it back: whole numbers without ".0", and its dates
 * ("/Date(ms)/" once parsed) written in the escaped "\/Date(ms)\/" form it requires.
 */
object JsonOut {
    private val date = Regex("^/Date\\(-?\\d+\\)/$")

    fun write(value: Any?): String = StringBuilder().also { write(it, value) }.toString()

    private fun write(sb: StringBuilder, v: Any?) {
        when (v) {
            null -> sb.append("null")
            is Boolean -> sb.append(v)
            is Int, is Long -> sb.append(v)
            is Double -> if (v == Math.floor(v) && !v.isInfinite() && Math.abs(v) < 1e15) sb.append(v.toLong()) else sb.append(v)
            is Float -> write(sb, v.toDouble())
            is String -> if (date.matches(v)) sb.append('"').append(v.replace("/", "\\/")).append('"') else string(sb, v)
            is Map<*, *> -> {
                sb.append('{')
                var first = true
                for ((k, value) in v) { if (!first) sb.append(','); first = false; string(sb, k.toString()); sb.append(':'); write(sb, value) }
                sb.append('}')
            }
            is Iterable<*> -> {
                sb.append('[')
                var first = true
                for (x in v) { if (!first) sb.append(','); first = false; write(sb, x) }
                sb.append(']')
            }
            else -> string(sb, v.toString())
        }
    }

    private fun string(sb: StringBuilder, s: String) {
        sb.append('"')
        for (c in s) {
            when (c) {
                '"' -> sb.append("\\\""); '\\' -> sb.append("\\\\"); '\n' -> sb.append("\\n"); '\r' -> sb.append("\\r"); '\t' -> sb.append("\\t")
                '<', '>', '&', '\'' -> sb.append("\\u").append(String.format("%04x", c.code))
                else -> if (c < ' ') sb.append("\\u").append(String.format("%04x", c.code)) else sb.append(c)
            }
        }
        sb.append('"')
    }
}
