package app.mica.blocker

import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Before
import org.junit.Test
import java.io.File

/** Registrable domains from the bundled public suffix list (assets/psl.dat). */
class SuffixTest {
    @Before fun useList() { Domains.suffixSource = { File("src/main/assets/psl.dat").inputStream() } }
    @After fun noList() { Domains.suffixSource = null }

    @Test fun plain() = assertEquals("example.com", Domains.registrable("a.b.example.com"))
    @Test fun twoLevel() = assertEquals("bbc.co.uk", Domains.registrable("news.bbc.co.uk"))
    @Test fun vietnam() = assertEquals("vnexpress.net", Domains.registrable("e.vnexpress.net"))
    @Test fun vietnamTwoLevel() = assertEquals("tiki.com.vn", Domains.registrable("www.tiki.com.vn"))
    @Test fun privateSuffix() = assertEquals("someone.github.io", Domains.registrable("someone.github.io"))
    @Test fun wildcard() = assertEquals("x.y.ck", Domains.registrable("a.x.y.ck"))
    @Test fun exception() = assertEquals("www.ck", Domains.registrable("a.www.ck"))
    @Test fun suffixItself() = assertEquals("co.uk", Domains.registrable("co.uk"))
    @Test fun unknownTld() = assertEquals("example.zzzz", Domains.registrable("a.example.zzzz"))
    @Test fun sameSite() = assertEquals(false, Domains.sameSite("alice.github.io", "bob.github.io"))
}
