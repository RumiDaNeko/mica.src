package app.mica

import android.app.Application
import android.content.ComponentCallbacks2
import android.content.Context
import android.content.res.Configuration
import android.content.res.Resources
import android.os.Build
import android.os.LocaleList
import androidx.appcompat.app.AppCompatDelegate

class MicaApp : Application() {
    lateinit var prefs: Prefs
        private set
    // Tabs and their WebViews live here, not in the Activity, so a theme or language change (which
    // recreates the Activity) never reloads a page or stops what's playing.
    val browser by lazy { Browser(this) }
    val account by lazy {
        app.mica.account.Account(this).apply {
            currentDark = { prefs.nightMode == AppCompatDelegate.MODE_NIGHT_YES || (prefs.nightMode != AppCompatDelegate.MODE_NIGHT_NO && (resources.configuration.uiMode and android.content.res.Configuration.UI_MODE_NIGHT_MASK) == android.content.res.Configuration.UI_MODE_NIGHT_YES) }
            applyDark = { dark ->
                prefs.nightMode = if (dark) AppCompatDelegate.MODE_NIGHT_YES else AppCompatDelegate.MODE_NIGHT_NO
                AppCompatDelegate.setDefaultNightMode(prefs.nightMode)
            }
        }
    }

    override fun onCreate() {
        super.onCreate()
        instance = this
        prefs = Prefs(this)
        AppCompatDelegate.setDefaultNightMode(prefs.nightMode)
        Blocker.enabled = prefs.blockerOn
        Blocker.allowed = prefs.allowedSites
        app.mica.blocker.Domains.suffixSource = { assets.open("psl.dat") }
        Blocker.load(this)
        Thread { Reader.trimCache(this) }.start()
    }

    // Before Android 13, the language picked in Settings reaches only Activities; text Mica writes from here
    // (reading mode, error pages, island notices, space names) would stay in the phone's language.
    private var localized: Pair<String, Resources>? = null
    override fun getResources(): Resources {
        val base = super.getResources()
        if (Build.VERSION.SDK_INT >= 33) return base
        val locales = AppCompatDelegate.getApplicationLocales()
        if (locales.isEmpty) return base
        val c = base.configuration
        val key = "${locales.toLanguageTags()} ${c.uiMode} ${c.densityDpi} ${c.fontScale}"
        localized?.let { if (it.first == key) return it.second }
        val config = Configuration(c).apply { setLocales(LocaleList.forLanguageTags(locales.toLanguageTags())) }
        return createConfigurationContext(config).resources.also { localized = key to it }
    }

    override fun onTrimMemory(level: Int) {
        super.onTrimMemory(level)
        browser.trim(level)
    }

    /**
     * Asks WebView to give back what it can without reloading anything (desktop's MemoryPurgeOnFreeze): its
     * renderer frees caches and unused heap, as under moderate memory pressure. Pages stay as they are.
     */
    fun purgeEngine() {
        @Suppress("DEPRECATION")
        super.onTrimMemory(ComponentCallbacks2.TRIM_MEMORY_RUNNING_CRITICAL)
    }

    companion object {
        lateinit var instance: MicaApp
            private set
    }
}

class Prefs(context: Context) {
    private val p = context.getSharedPreferences("mica", Context.MODE_PRIVATE)

    var nightMode: Int
        get() = p.getInt("night", AppCompatDelegate.MODE_NIGHT_FOLLOW_SYSTEM)
        set(v) { p.edit().putInt("night", v).apply() }
    var blockerOn: Boolean
        get() = p.getBoolean("blocker", true)
        set(v) { p.edit().putBoolean("blocker", v).apply() }
    var allowedSites: Set<String>
        get() = p.getStringSet("allowed", emptySet())!!.toSet()
        set(v) { p.edit().putStringSet("allowed", v).apply() }
    /** Mica offers to save and fill passwords (off: Android's autofill service does). */
    var micaPasswords: Boolean
        get() = p.getBoolean("micaPasswords", true)
        set(v) { p.edit().putBoolean("micaPasswords", v).apply() }
    /** Sites' camera, microphone, location... answers, as "origin<TAB>kind<TAB>1|0" (see SitePermissions). */
    var sitePermissions: Set<String>
        get() = p.getStringSet("sitePermissions", emptySet())!!.toSet()
        set(v) { p.edit().putStringSet("sitePermissions", v).apply() }
    /** Reading mode's text size in px (14–30) and typeface, as desktop's ReaderSize and ReaderSerif. */
    /** Text size per site ("host\tpercent"), 100 when not set. */
    fun zoomFor(host: String): Int = p.getStringSet("siteZoom", emptySet())!!.firstOrNull { it.startsWith(host + "\t") }?.substringAfter('\t')?.toIntOrNull() ?: 100
    fun setZoom(host: String, percent: Int) {
        val rest = p.getStringSet("siteZoom", emptySet())!!.filterNot { it.startsWith(host + "\t") }.toMutableSet()
        if (percent != 100) rest.add(host + "\t" + percent.coerceIn(50, 300))
        p.edit().putStringSet("siteZoom", rest).apply()
    }
    /** Jars deleted while their profile was in use; their folders go at the next start. */
    var deletedJars: Set<String>
        get() = p.getStringSet("deletedJars", emptySet())!!.toSet()
        set(v) { p.edit().putStringSet("deletedJars", v).apply() }
    var readerSize: Int
        get() = p.getInt("readerSize", 18)
        set(v) { p.edit().putInt("readerSize", v.coerceIn(14, 30)).apply() }
    var readerSerif: Boolean
        get() = p.getBoolean("readerSerif", false)
        set(v) { p.edit().putBoolean("readerSerif", v).apply() }
    var desktopDefault: Boolean
        get() = p.getBoolean("desktop", false)
        set(v) { p.edit().putBoolean("desktop", v).apply() }
}
