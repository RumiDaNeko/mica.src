package app.mica.blocker

import java.io.DataInputStream
import java.io.DataOutputStream

/**
 * The parsed rules in a compact binary form, so starts after the first skip the JSON (most of the load
 * time). Written once per app build; a file that doesn't read back exactly is ignored and rebuilt.
 */
object RuleCache {
    private const val MAGIC = 0x4D494352 // "MICR"
    private const val VERSION = 1

    fun write(out: DataOutputStream, rules: List<Rule>, lists: Int, excluded: List<String>) {
        out.writeInt(MAGIC); out.writeInt(VERSION)
        out.writeInt(lists)
        writeSet(out, excluded)
        out.writeInt(rules.size)
        for (r in rules) {
            out.writeByte(if (r.action == Action.BLOCK) 0 else 1)
            out.writeInt(r.priority)
            writeSet(out, r.requestDomains); writeSet(out, r.excludedRequestDomains)
            writeSet(out, r.initiatorDomains); writeSet(out, r.excludedInitiatorDomains)
            out.writeByte(r.domainType)
            out.writeInt(r.types)
            writeSet(out, r.methods); writeSet(out, r.excludedMethods)
            val f = r.urlFilter
            if (f == null) out.writeByte(0) else { out.writeByte(if (f.caseSensitive) 2 else 1); out.writeUTF(f.source) }
            out.writeBoolean(r.ownSiteAllowed)
        }
        out.writeInt(MAGIC)
    }

    /** (rules, lists, excluded), or null if the file isn't one of ours. */
    fun read(input: DataInputStream): Triple<List<Rule>, Int, List<String>>? {
        if (input.readInt() != MAGIC || input.readInt() != VERSION) return null
        val lists = input.readInt()
        val excluded = readSet(input)?.toList().orEmpty()
        val n = input.readInt()
        if (n < 0 || n > 1_000_000) return null
        val rules = ArrayList<Rule>(n)
        repeat(n) {
            val action = if (input.readByte().toInt() == 0) Action.BLOCK else Action.ALLOW
            val priority = input.readInt()
            val req = readSet(input); val exReq = readSet(input); val ini = readSet(input); val exIni = readSet(input)
            val domainType = input.readByte().toInt()
            val types = input.readInt()
            val methods = readSet(input); val exMethods = readSet(input)
            val filter = when (input.readByte().toInt()) { 0 -> null; 2 -> UrlFilter(input.readUTF(), true); else -> UrlFilter(input.readUTF(), false) }
            rules.add(Rule(action, priority, req, exReq, ini, exIni, domainType, types, methods, exMethods, filter, input.readBoolean()))
        }
        if (input.readInt() != MAGIC) return null
        return Triple(rules, lists, excluded)
    }

    private fun writeSet(out: DataOutputStream, set: Collection<String>?) {
        if (set == null) { out.writeInt(-1); return }
        out.writeInt(set.size)
        for (s in set) out.writeUTF(s)
    }

    private fun readSet(input: DataInputStream): Set<String>? {
        val n = input.readInt()
        if (n < 0) return null
        val s = HashSet<String>(n * 4 / 3 + 1)
        repeat(n) { s.add(input.readUTF()) }
        return s
    }
}
