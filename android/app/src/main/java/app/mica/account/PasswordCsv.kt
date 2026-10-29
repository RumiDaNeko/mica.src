package app.mica.account

/**
 * Passwords exported from Chrome, Edge, Firefox, Bitwarden and the like, as desktop's BrowserImport reads
 * them: RFC 4180 CSV, header names matched loosely, HTTPS logins with a password only, one per site and user.
 */
object PasswordCsv {
    class Login(val origin: String, val username: String, val password: String)

    fun parse(text: String): List<Login> {
        val rows = rows(text.removePrefix("﻿"))
        if (rows.isEmpty()) return emptyList()
        val head = rows[0].map { it.trim().lowercase() }
        fun col(vararg names: String) = head.indexOfFirst { it in names }
        val url = col("url", "login_uri", "website", "origin", "web site")
        val user = col("username", "login_username", "user name", "login", "email")
        val pass = col("password", "login_password")
        if (url < 0 || pass < 0) return emptyList()
        val seen = HashSet<String>()
        val out = ArrayList<Login>()
        for (r in rows.drop(1)) {
            val origin = VaultCrypto.originOrNull(r.getOrNull(url) ?: "") ?: continue
            val p = r.getOrNull(pass).orEmpty()
            if (p.isEmpty()) continue
            val u = if (user >= 0) r.getOrNull(user).orEmpty() else ""
            if (seen.add(origin + "\n" + u)) out.add(Login(origin, u, p))
        }
        return out
    }

    private fun rows(s: String): List<List<String>> {
        val rows = ArrayList<List<String>>()
        var row = ArrayList<String>()
        val cell = StringBuilder()
        var quoted = false
        var i = 0
        while (i < s.length) {
            val c = s[i]
            if (quoted) {
                if (c == '"') { if (i + 1 < s.length && s[i + 1] == '"') { cell.append('"'); i++ } else quoted = false } else cell.append(c)
            } else when (c) {
                '"' -> quoted = true
                ',' -> { row.add(cell.toString()); cell.clear() }
                '\r' -> {}
                '\n' -> { row.add(cell.toString()); cell.clear(); if (row.any { it.isNotEmpty() }) rows.add(row); row = ArrayList() }
                else -> cell.append(c)
            }
            i++
        }
        row.add(cell.toString())
        if (row.any { it.isNotEmpty() }) rows.add(row)
        return rows
    }
}
