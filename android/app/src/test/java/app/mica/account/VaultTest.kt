package app.mica.account

import app.mica.blocker.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import java.io.File

// desktop-vault.json was written by desktop's own VaultModel.cs and Totp.cs, so these check that a vault
// and 2FA keys synced from Windows work here unchanged.
class VaultTest {
    private val vectors = Json.parse(javaClass.getResource("/desktop-vault.json")!!.readText()) as Map<*, *>
    private val password = vectors["password"] as String
    private val envelope = Envelope.from(vectors["envelope"] as Map<*, *>)

    @Test fun pbkdf2MatchesRfc7914() {
        // RFC 7914 §11, PBKDF2-HMAC-SHA256("passwd", "salt", 1, 64)
        val dk = VaultCrypto.pbkdf2("passwd".toByteArray(), "salt".toByteArray(), 1, 64)
        assertEquals("55ac046e56e3089fec1691c22544b605f94185216dde0465e68b9d57c20dacbc49ca9cccf179b645991664b39d77ef317c71b845b1e30bd509112041d3a19783",
            dk.joinToString("") { "%02x".format(it) })
    }

    @Test fun opensDesktopVault() {
        val p = VaultCrypto.decrypt(envelope, password)
        assertEquals("123456789012345678", p.accountId)
        assertEquals(41L, p.revision)
        assertTrue(p.hasSnapshot)
        assertEquals(2, p.entries.size)
        assertEquals("https://accounts.example.com", p.entries[0].origin)
        assertEquals("rumi@example.com", p.entries[0].username)
        assertEquals("p@ss \"word\" ✓", p.entries[0].password)
        assertEquals("", p.entries[1].username)
        assertEquals(null, p.entries[1].totp)
    }

    @Test fun vaultKeyMatchesDesktop() {
        val key = VaultCrypto.keyFor(envelope, password)
        assertEquals(vectors["vaultKey"], key)
        assertEquals(2, VaultCrypto.decrypt(envelope, key).entries.size)
    }

    @Test fun wrongPasswordAndTamperingFail() {
        try { VaultCrypto.decrypt(envelope, "wrong"); fail() } catch (e: VaultException) {}
        val c = envelope.ciphertext
        val flipped = (if (c[0] == 'A') 'B' else 'A') + c.substring(1)
        val bad = Envelope(1, envelope.kdf, envelope.iterations, envelope.salt, envelope.iv, flipped, envelope.mac)
        try { VaultCrypto.decrypt(bad, VaultCrypto.keyFor(envelope, password)); fail() } catch (e: VaultException) {}
    }

    @Test fun roundTripKeepsWhatAndroidDoesNotUse() {
        val key = VaultCrypto.keyFor(envelope, password)
        val p = VaultCrypto.decrypt(envelope, key)
        p.revision++
        p.entries.add(VaultEntry.new("https://Example.org:443/login", "me", "pw"))
        val json = p.toJson()
        // desktop's dates must come back in its escaped form, and the snapshot as it was
        assertTrue(json, json.contains("\"LastUsed\":\"\\/Date(1792976523000)\\/\""))
        val original = (Json.parse(vectors["payloadJson"] as String) as Map<*, *>)["Snapshot"]
        assertEquals(original, (Json.parse(json) as Map<*, *>)["Snapshot"])
        assertFalse(json.contains(".0,"))

        val again = VaultCrypto.decrypt(VaultCrypto.encrypt(p, key), password)
        assertEquals(42L, again.revision)
        assertEquals("https://example.org", again.entries[2].origin)
        // leave an Android-made vault for desktop's check (Gen.exe check)
        System.getProperty("mica.vaultOut")?.let { File(it).writeText(VaultCrypto.encrypt(p, key).toJson()) }
    }

    @Test fun origins() {
        assertEquals("https://example.com", VaultCrypto.origin("HTTPS://Example.COM/path?q=1"))
        assertEquals("https://example.com:8443", VaultCrypto.origin("https://example.com:8443/"))
        assertEquals(null, VaultCrypto.originOrNull("http://example.com"))
        assertEquals(null, VaultCrypto.originOrNull("https://user@example.com"))
    }

    @Test fun totpMatchesDesktop() {
        for (v in vectors["totp"] as List<*>) {
            v as Map<*, *>
            val k = Totp.parse(v["input"] as String)
            assertEquals(v["input"].toString(), v["code"], Totp.code(k, (v["unix"] as Double).toLong()))
            assertEquals(v["normalized"], Totp.normalize(k))
            assertEquals(v["code"], Totp.code(Totp.parse(Totp.normalize(k)), (v["unix"] as Double).toLong()))
        }
    }

    @Test fun totpRfc6238() {
        // RFC 6238 appendix B, SHA-1 at 59 s
        val k = Totp.Key("12345678901234567890".toByteArray(), digits = 8)
        assertEquals("94287082", Totp.code(k, 59))
        assertEquals("07081804", Totp.code(k, 1111111109))
    }

    @Test fun pkceMatchesDesktop() {
        assertEquals(vectors["challenge"], VaultCrypto.challenge(vectors["verifier"] as String))
    }

    @Test fun passwordCsv() {
        val csv = "﻿name,url,username,password,note\r\n" +
            "a,https://a.example.com/login,me,\"p,w\"\"x\",\r\n" +
            "dup,https://a.example.com/other,me,second,\r\n" +
            "b,http://insecure.example.com,me,pw,\r\n" +
            "c,https://c.example.com,,\"multi\nline\",\r\n" +
            "d,https://d.example.com,me,,\r\n"
        val logins = PasswordCsv.parse(csv)
        assertEquals(2, logins.size)
        assertEquals("https://a.example.com", logins[0].origin)
        assertEquals("p,w\"x", logins[0].password)
        assertEquals("multi\nline", logins[1].password)
    }
}
