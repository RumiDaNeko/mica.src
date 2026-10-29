package app.mica.account

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

// The same rules as desktop's VaultCrypto.MergePayload, Touch and Forget, so the two never replace each other's logins.
class MergeTest {
    private fun login(origin: String, user: String, pass: String, saved: Long) = VaultEntry(VaultEntry.newId(), origin, user, pass, null, saved)
    private fun vault(vararg e: VaultEntry) = VaultPayload().apply { entries.addAll(e) }

    @Test fun unionOfBothCopies() {
        val here = vault(login("https://a.example", "me", "1", 10))
        val cloud = vault(login("https://b.example", "me", "2", 10))
        assertEquals(1, here.merge(cloud))
        assertEquals(setOf("https://a.example", "https://b.example"), here.entries.map { it.origin }.toSet())
    }

    @Test fun newestSaveWins() {
        val here = vault(login("https://a.example", "me", "old", 10))
        here.merge(vault(login("https://a.example", "me", "new", 20)))
        assertEquals("new", here.entries.single().password)
        here.merge(vault(login("https://a.example", "me", "older", 5)))
        assertEquals("new", here.entries.single().password)
    }

    @Test fun originMatchesIgnoringCase() {
        val here = vault(login("https://a.example", "me", "1", 10))
        here.merge(vault(login("https://A.example", "me", "2", 30)))
        assertEquals(1, here.entries.size)
        assertEquals("2", here.entries.single().password)
    }

    @Test fun deletionDropsOlderSavesOnly() {
        val cloud = vault(login("https://a.example", "me", "1", 10), login("https://b.example", "me", "2", 10))
        val here = vault()
        here.forgetKey("https://a.example\nme", 15)
        here.forgetKey("https://b.example\nme", 5)
        here.merge(cloud)
        assertEquals(listOf("https://b.example"), here.entries.map { it.origin })
        // A marker at the same moment as the save still deletes it.
        val again = vault(login("https://c.example", "me", "3", 7))
        again.forgetKey("https://c.example\nme", 7)
        again.merge(vault())
        assertTrue(again.entries.isEmpty())
    }

    @Test fun markersCombineKeepingNewest() {
        val a = vault(); a.forgetKey("https://a.example\nme", 10)
        val b = vault(); b.forgetKey("https://a.example\nme", 30); b.forgetKey("https://z.example\nyou", 3)
        a.merge(b)
        assertEquals(listOf("https://z.example\nyou\t3", "https://a.example\nme\t30"), a.removed)
    }

    @Test fun savingClearsItsMarker() {
        val p = vault()
        p.forgetKey("https://a.example\nme", 10)
        val e = login("https://a.example", "me", "x", 0)
        p.entries.add(e); p.touch(e, 20)
        assertEquals(20L, e.saved)
        assertTrue(p.removed.isEmpty())
        val other = vault(); other.forgetKey("https://a.example\nme", 15)
        p.merge(other)
        assertEquals(1, p.entries.size)
    }

    @Test fun markersAreCapped() {
        val p = vault()
        for (i in 0 until VaultPayload.MAX_REMOVED + 5) p.forgetKey("https://s$i.example\nme", i.toLong())
        assertEquals(VaultPayload.MAX_REMOVED, p.removed.size)
        assertTrue(p.removed.first().startsWith("https://s5.example"))
    }

    @Test fun keepsSavedRemovedAndUnknownFields() {
        val json = """{"Format":1,"Revision":3,"Removed":["https://x.example\nme\t99"],"Future":true,"Entries":[{"Id":"a","Origin":"https://a.example","Username":"me","Password":"p","Totp":null,"Saved":1730000000000,"Note":"hi"}]}"""
        val p = VaultPayload.parse(json)
        assertEquals(1730000000000L, p.entries.single().saved)
        val out = p.toJson()
        assertTrue(out, out.contains("\"Saved\":1730000000000"))
        assertTrue(out, out.contains("\"Note\":\"hi\""))
        assertTrue(out, out.contains("\"Future\":true"))
        assertEquals(listOf("https://x.example\nme\t99"), VaultPayload.parse(out).removed)
    }
}
