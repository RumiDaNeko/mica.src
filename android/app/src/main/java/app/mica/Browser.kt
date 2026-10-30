package app.mica

import android.annotation.SuppressLint
import android.content.ComponentCallbacks2
import android.content.Context
import android.content.ContextWrapper
import android.content.MutableContextWrapper
import android.app.ActivityManager
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Color
import android.graphics.Rect
import android.net.Uri
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.Message
import android.view.PixelCopy
import android.view.View
import android.view.ViewGroup
import android.webkit.RenderProcessGoneDetail
import android.webkit.ValueCallback
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.ImageView
import androidx.webkit.WebSettingsCompat
import androidx.webkit.WebViewCompat
import androidx.webkit.WebViewFeature
import app.mica.blocker.Domains

/**
 * Tabs, spaces and their WebViews. Lives in the Application so the Activity can come and go.
 *
 * Battery and memory: only the active tab and the two used most recently keep a WebView, and those two are
 * paused. Older tabs rest (their WebView is destroyed, back/forward history kept in a Bundle). When the app
 * goes to the background every tab is paused and JS timers stop, except that a tab playing media is never
 * paused, rested or dropped.
 */
class Browser(private val app: MicaApp) {
    interface Host {
        val activity: android.app.Activity
        val webHost: ViewGroup
        /** Something of Mica's (Spotlight, the deck, a card) is over the page, so a picture of it would show that. */
        val pageCovered: Boolean
        fun onActiveChanged()
        fun onTabsChanged()
        fun onProgress(tab: Tab)
        fun onPageChanged(tab: Tab)
        fun onScrolled(dy: Int, atTop: Boolean)
        fun announce(text: String)
        fun showCustomView(view: View, callback: WebChromeClient.CustomViewCallback)
        fun hideCustomView()
        fun chooseFiles(callback: ValueCallback<Array<Uri>>, params: WebChromeClient.FileChooserParams): Boolean
        fun download(url: String, userAgent: String, contentDisposition: String?, mime: String?)
        fun openExternal(url: String, fallback: (String) -> Unit): Boolean
        fun onLinkLongPress(tab: Tab, hit: WebView.HitTestResult): Boolean
        fun onLoginMessage(tab: Tab, origin: String, json: String)
        fun onSitePermission(tab: Tab, request: android.webkit.PermissionRequest)
        fun onSitePermissionCanceled(request: android.webkit.PermissionRequest)
        fun onPopupBlocked(tab: Tab, url: String)
        /** alert / confirm / prompt / beforeunload; [answer] true accepts (with the prompt's text). Called exactly once. */
        fun onPageDialog(tab: Tab, kind: Int, origin: String, message: String, default: String?, answer: (Boolean, String?) -> Unit)
        fun onHttpAuth(tab: Tab, host: String, handler: android.webkit.HttpAuthHandler)
        fun onGeolocation(tab: Tab, origin: String, callback: android.webkit.GeolocationPermissions.Callback)
        fun onReaderMessage(tab: Tab, message: String)
    }

    private val store = Store(app.filesDir)
    val spaces: MutableList<Space>
    val tabs: MutableList<Tab>
    val history: MutableList<HistoryEntry>
    /** Newest first, as on desktop. */
    val bookmarks: MutableList<HistoryEntry>
    val readLater: MutableList<HistoryEntry>
    val recentlyClosed: MutableList<HistoryEntry>
    val groups: MutableList<TabGroup>
    val jars: MutableList<Jar>
    /** The space to return to when the private tabs are gone. */
    private var lastSpaceId = "personal"
    var activeSpaceId: String
        private set
    var host: Host? = null
        private set
    private val context = MutableContextWrapper(app)
    private val main = Handler(Looper.getMainLooper())
    private val saveSoon = Runnable { save() }
    private var inBackground = false

    private val loginScripts by lazy { listOf("login", "totp", "passkey").joinToString("\n") { n -> app.assets.open("scripts/$n.js").use { it.readBytes().toString(Charsets.UTF_8) } } }

    private val mobileUa: String
    private val desktopUa: String

    init {
        val state = store.load()
        if (state != null) {
            spaces = state.spaces; tabs = state.tabs; history = state.history; activeSpaceId = state.activeSpace
            bookmarks = state.bookmarks; readLater = state.readLater; recentlyClosed = state.closed; groups = state.groups
            jars = state.jars ?: defaultJars()
        } else {
            jars = defaultJars()
            spaces = mutableListOf(Space("personal", "", Color.parseColor("#71608C")), Space("work", "", Color.parseColor("#7C977D")))
            tabs = mutableListOf(); history = mutableListOf(); activeSpaceId = "personal"
            bookmarks = mutableListOf(); readLater = mutableListOf(); recentlyClosed = mutableListOf(); groups = mutableListOf()
        }
        if (spaces.none { it.id == activeSpaceId }) activeSpaceId = spaces[0].id
        tabs.removeAll { t -> spaces.none { it.id == t.space } }
        if (tabs.none { it.space == activeSpaceId }) open(Navigation.HOME, activate = true)
        normalizeGroups()
        lastSpaceId = activeSpaceId
        // Whatever a private session left behind (Mica stopped while it was open) goes now.
        if (privateSupported) wipePrivate()
        for (t in tabs) if (t.jar != null && jars.none { it.id == t.jar }) t.jar = null
        val def = try { WebSettings.getDefaultUserAgent(app) } catch (e: Exception) { "Mozilla/5.0 (Linux; Android) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36" }
        // Without "; wv" and "Version/4.0" sites (and Google sign-in) treat Mica as the browser it is.
        mobileUa = def.replace("; wv", "").replace(Regex("Version/\\S+ "), "")
        val chrome = Regex("Chrome/([\\d.]+)").find(def)?.groupValues?.get(1)?.substringBefore('.') ?: "130"
        desktopUa = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/$chrome.0.0.0 Safari/537.36"
    }

    val activeSpace get() = spaces.first { it.id == activeSpaceId }
    val active: Tab? get() = activeSpace.activeTab?.let { id -> tabs.firstOrNull { it.id == id && it.space == activeSpaceId } }
    fun tabsIn(spaceId: String = activeSpaceId) = tabs.filter { it.space == spaceId }
    fun tab(id: String?) = tabs.firstOrNull { it.id == id }
    fun spaceName(space: Space) = space.name.ifEmpty {
        when (space.id) { "personal" -> app.getString(R.string.personal); "work" -> app.getString(R.string.work); PRIVATE -> app.getString(R.string.private_tabs); else -> space.id }
    }

    // ---- Host (the Activity) ----

    fun attach(h: Host) {
        host = h
        context.baseContext = h.activity
        show(active)
    }

    fun detach(h: Host) {
        if (host !== h) return
        tabs.forEach { t -> t.view?.let { (it.parent as? ViewGroup)?.removeView(it) } }
        host = null
        context.baseContext = app
    }

    // ---- Tabs ----

    fun open(url: String, activate: Boolean = true, parent: Tab? = null, spaceId: String = activeSpaceId): Tab {
        val tab = Tab(space = spaceId, url = url)
        tab.parent = parent?.id
        tab.desktop = app.prefs.desktopDefault
        if (parent != null && parent.space == spaceId) tab.jar = parent.jar
        val at = parent?.let { p -> tabs.indexOfLast { it.space == spaceId && (it.parent == p.id || it.id == p.id) } + 1 } ?: tabs.size
        tabs.add(at.coerceIn(0, tabs.size), tab)
        if (activate) activate(tab) else { host?.onTabsChanged(); saveLater() }
        return tab
    }

    fun activate(tab: Tab) {
        val old = active
        // In a split, another website takes the place of the pane you were on; anything else ends the split.
        split?.let { (a, b) ->
            if (tab.id != a && tab.id != b) {
                val other = if (old?.id == a) b else a
                split = if (tab.space == activeSpaceId && Navigation.isWeb(tab.url) && old != null && (old.id == a || old.id == b)) (if (old.id == a) tab.id to other else other to tab.id) else null
                if (split == null) {
                    tab(other)?.let { hide(it) }
                    dropSplitLayout()
                }
            }
        }
        if (tab.space != activeSpaceId) activeSpaceId = tab.space
        if (!tab.isPrivate) lastSpaceId = tab.space
        activeSpace.activeTab = tab.id
        tab.lastUsed = System.currentTimeMillis()
        if (old !== tab && !inSplit(old)) old?.let { hide(it) }
        show(tab)
        enforceBudget()
        host?.onActiveChanged()
        saveLater()
    }

    /** The tab's WebView, created (or restored, if it was resting) when needed. */
    private fun viewOf(tab: Tab): WebView = tab.view ?: createView(tab).also { v ->
        val saved = tab.saved
        if (saved == null || v.restoreState(saved) == null) load(tab, tab.url)
        tab.saved = null
    }

    /** Puts the tab's WebView (creating or restoring it if resting) into the host, beside its split partner if any. */
    private fun show(tab: Tab?) {
        val h = host ?: return
        val pair = split?.let { (a, b) -> tab(a) to tab(b) }
        if (tab != null && pair != null && pair.first != null && pair.second != null && (tab === pair.first || tab === pair.second)) {
            val first = viewOf(pair.first!!)
            val second = viewOf(pair.second!!)
            val fresh = splitLayout == null
            val layout = splitLayout ?: SplitLayout(h.activity) { i -> split?.let { (a, b) -> tab(if (i == 0) a else b)?.let { focusPane(it) } } }.also { splitLayout = it }
            layout.put(first, second)
            for (t in listOf(pair.first!!, pair.second!!)) { t.hiddenAt = 0; t.frozen = false; t.typed = false }
            if (fresh && !inBackground) layout.enter()
            layout.activeIndex = if (tab === pair.first) 0 else 1
            if (layout.parent !== h.webHost) { h.webHost.removeAllViews(); (layout.parent as? ViewGroup)?.removeView(layout); h.webHost.addView(layout, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)) }
            if (!inBackground) { first.onResume(); second.onResume() }
            return
        }
        cover = null
        h.webHost.removeAllViews()
        if (tab == null || tab.isHome) return
        val resting = tab.view == null && tab.saved != null
        val view = viewOf(tab)
        tab.hiddenAt = 0; tab.frozen = false; tab.typed = false
        (view.parent as? ViewGroup)?.removeView(view)
        h.webHost.addView(view, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        if (resting) coverWhileLoading(tab)
        if (!inBackground) view.onResume()
    }

    // ---- Split view ----

    /** The two tabs shown together (first, second), or null. Both stay awake while shown. */
    var split: Pair<String, String>? = null
        private set
    private var splitLayout: SplitLayout? = null

    fun inSplit(tab: Tab?) = tab != null && split?.let { it.first == tab.id || it.second == tab.id } == true
    fun splitPartner(tab: Tab): Tab? = split?.let { (a, b) -> if (tab.id == a) tab(b) else if (tab.id == b) tab(a) else null }

    /** [with] beside [tab], the one you're on first. Both must be websites in the same space. */
    fun startSplit(tab: Tab, with: Tab) {
        if (tab === with || tab.space != with.space || tab.isHome || with.isHome) return
        split = tab.id to with.id
        activate(tab)
    }

    /** The pane you're on grows over the other one, then the split is gone. */
    fun endSplit() {
        val pair = split ?: return
        val keep = active ?: return
        val l = splitLayout
        if (l != null && l.isAttachedToWindow && !inBackground) l.leave(if (pair.first == keep.id) 0 else 1) { if (split == pair && splitLayout === l) finishSplit(keep) }
        else finishSplit(keep)
    }

    private fun finishSplit(keep: Tab) {
        splitPartner(keep)?.let { hide(it) }
        split = null
        dropSplitLayout()
        show(keep)
        enforceBudget()
        host?.onTabsChanged()
    }

    private fun dropSplitLayout() {
        splitLayout?.let { l -> l.removeAllViews(); (l.parent as? ViewGroup)?.removeView(l) }
        splitLayout = null
    }

    /** A touch in a pane: that tab becomes the active one, without moving anything. */
    private fun focusPane(tab: Tab) {
        activeSpace.activeTab = tab.id
        tab.lastUsed = System.currentTimeMillis()
        splitLayout?.activeIndex = if (split?.first == tab.id) 0 else 1
        host?.onActiveChanged()
        saveLater()
    }

    fun navigate(tab: Tab, url: String) {
        tab.url = url
        if (url == Navigation.HOME && inSplit(tab)) endSplit()
        if (url == Navigation.HOME) { destroyView(tab); tab.title = ""; if (tab === active) show(tab); host?.onPageChanged(tab); saveLater(); return }
        val view = tab.view
        if (view == null) { if (tab === active) show(tab) else tab.saved = null } else load(tab, url)
        host?.onPageChanged(tab)
        saveLater()
    }

    private fun load(tab: Tab, url: String) {
        val secure = Https.upgrade(url)
        tab.upgraded = if (secure != null) url else null
        tab.view?.loadUrl(Navigation.engineAddress(secure ?: url))
    }

    fun close(tab: Tab) {
        val wasActive = tab === active
        val siblings = tabsIn(tab.space)
        val index = siblings.indexOf(tab)
        val partner = splitPartner(tab)
        if (partner != null) { split = null; dropSplitLayout() }
        if (tab.isPrivate && tabs.count { it.isPrivate } == 1) tab.view?.clearCache(true)
        destroyView(tab)
        tabs.remove(tab)
        snapshots.remove(tab.id)
        if (Navigation.isWeb(tab.url) && !tab.isPrivate) {
            recentlyClosed.removeAll { it.url == tab.url }
            recentlyClosed.add(0, HistoryEntry(tab.url, tab.title, System.currentTimeMillis()))
            while (recentlyClosed.size > 25) recentlyClosed.removeAt(recentlyClosed.size - 1)
        }
        if (tab.isPrivate && tabs.none { it.isPrivate }) { endPrivate(); return }
        normalizeGroups()
        if (wasActive) {
            val rest = tabsIn(tab.space)
            val next = partner ?: tab(tab.parent)?.takeIf { it.space == tab.space } ?: rest.getOrNull(index.coerceAtMost(rest.size - 1)) ?: rest.lastOrNull()
            if (next != null) activate(next) else open(Navigation.HOME, activate = true, spaceId = tab.space)
        } else { if (partner != null) show(active); host?.onTabsChanged() }
        saveLater()
    }

    // ---- Library ----

    val highlights by lazy { Highlights(app) }

    private val blobs by lazy {
        Blobs(app) { saved, name -> host?.announce(app.getString(if (saved != null) R.string.blob_saved else R.string.blob_failed, name)) }
    }

    /** The address highlights and read later use for a tab: the article's own address in reading mode. */
    fun pageKey(tab: Tab): String =
        if (Reader.isReader(tab.url)) Reader.load(app, Reader.id(tab.url))?.optString("url")?.takeIf { it.isNotEmpty() } ?: tab.url
        else Navigation.fromEngine(tab.url)

    /** Marks the text selected in [tab]; [done] gets the text, or null when nothing was selected. */
    fun highlight(tab: Tab, done: (String?) -> Unit) {
        val v = tab.view ?: return done(null)
        val id = Highlights.newId()
        v.evaluateJavascript(Highlights.markScript(id)) { raw ->
            val text = try { org.json.JSONTokener(raw ?: "null").nextValue() as? String } catch (e: Exception) { null }
            if (!text.isNullOrBlank() && !tab.isPrivate) highlights.add(pageKey(tab), text, id, tab.title)
            done(text)
        }
    }

    fun removeHighlight(item: Highlights.Item) {
        highlights.remove(item)
        for (t in tabs) if (t.view != null && Highlights.key(pageKey(t)) == item.url) t.view?.evaluateJavascript(Highlights.unmarkScript(item.mark.id), null)
    }

    /** Finds the article on the page; [done] gets it, or null if the page isn't one. */
    fun readArticle(tab: Tab, done: (org.json.JSONObject?) -> Unit) {
        val v = tab.view
        if (v == null || !Navigation.isWeb(tab.url)) return done(null)
        v.evaluateJavascript(Reader.script(app)) { raw -> done(Reader.parse(raw)) }
    }

    fun isBookmarked(url: String) = bookmarks.any { it.url == url }
    fun isSavedForLater(url: String) = readLater.any { it.url == url }

    /** Adds the page, or takes it out if it's there already. Returns whether it's in the list now. */
    fun toggle(list: MutableList<HistoryEntry>, tab: Tab): Boolean {
        val url = Navigation.fromEngine(tab.url)
        if (list.removeAll { it.url == url }) { saveLater(); return false }
        list.add(0, HistoryEntry(url, tab.title.ifBlank { Navigation.label(url) }, System.currentTimeMillis()))
        saveLater()
        return true
    }

    fun forget(list: MutableList<HistoryEntry>, entry: HistoryEntry) { list.remove(entry); saveLater() }
    fun clearHistory() { history.clear(); recentlyClosed.clear(); saveLater() }

    fun step(dir: Int) {
        val list = tabsIn()
        val i = list.indexOf(active)
        if (list.size < 2 || i < 0) return
        activate(list[(i + dir + list.size) % list.size])
    }

    // ---- Private tabs ----

    /** Private tabs need WebView's profiles (WebView 123 and newer); without them Mica doesn't offer them. */
    val privateSupported get() = WebViewFeature.isFeatureSupported(WebViewFeature.MULTI_PROFILE)

    /**
     * The WebView profile of this private session. Each session gets a new one, so one session never sees
     * another's cookies. A profile stays in use until the process ends, so it's emptied when the session
     * closes and its folder deleted the next time Mica starts.
     */
    private var privateProfile: String? = null

    fun openPrivate(url: String = Navigation.HOME): Tab? {
        if (!privateSupported) return null
        if (spaces.none { it.id == PRIVATE }) spaces.add(Space(PRIVATE, "", Color.parseColor("#4A4453")))
        if (privateProfile == null) privateProfile = PRIVATE_PROFILE + System.currentTimeMillis().toString(36)
        return open(url, activate = true, spaceId = PRIVATE)
    }

    /** Closes every private tab; their cookies, storage and cache go with them. */
    fun closePrivate() {
        if (tabs.none { it.isPrivate }) return
        // The session's HTTP cache, through any of its views that is still alive.
        tabs.firstOrNull { it.isPrivate && it.view != null }?.view?.clearCache(true)
        tabs.filter { it.isPrivate }.forEach { destroyView(it) }
        tabs.removeAll { it.isPrivate }
        endPrivate()
    }

    private fun endPrivate() {
        Https.forgetPrivateCertificates()
        spaces.removeAll { it.id == PRIVATE }
        groups.removeAll { it.space == PRIVATE }
        if (activeSpaceId == PRIVATE) {
            val back = spaces.firstOrNull { it.id == lastSpaceId } ?: spaces[0]
            activeSpaceId = back.id
            val next = tab(back.activeTab)?.takeIf { it.space == back.id } ?: tabsIn(back.id).maxByOrNull { it.lastUsed }
            if (next != null) activate(next) else open(Navigation.HOME, activate = true, spaceId = back.id)
        } else host?.onTabsChanged()
        // Empty the session's profile now: cookies and site storage (its cache went with each view).
        privateProfile?.let { name ->
            try {
                val p = androidx.webkit.ProfileStore.getInstance().getProfile(name)
                p?.cookieManager?.removeAllCookies(null)
                p?.cookieManager?.flush()
                p?.webStorage?.deleteAllData()
            } catch (e: Exception) {}
        }
        privateProfile = null
        saveLater()
    }

    /** At start, before any WebView: the folders of earlier private sessions. */
    private fun wipePrivate() {
        try {
            val store = androidx.webkit.ProfileStore.getInstance()
            // Private sessions, and jars deleted last time (a profile in use can't be deleted until Mica restarts).
            val gone = app.prefs.deletedJars
            store.allProfileNames.filter { it.startsWith("mica-private") || it.removePrefix(JAR_PROFILE) in gone && it.startsWith(JAR_PROFILE) }.forEach { store.deleteProfile(it) }
            app.prefs.deletedJars = emptySet()
        } catch (e: Exception) {}
    }

    /** The WebView profile for [tab]'s pages: the private session's, its jar's, or null for the default. */
    private fun profileName(tab: Tab): String? = when {
        tab.isPrivate -> privateProfile ?: (PRIVATE_PROFILE + System.currentTimeMillis().toString(36)).also { privateProfile = it }
        tab.jar != null && privateSupported -> JAR_PROFILE + tab.jar
        else -> null
    }

    private fun useProfile(v: WebView, tab: Tab) {
        val name = profileName(tab) ?: return
        androidx.webkit.ProfileStore.getInstance().getOrCreateProfile(name)
        WebViewCompat.setProfile(v, name)
    }

    private fun profileOf(tab: Tab): androidx.webkit.Profile? {
        val name = (if (tab.isPrivate) privateProfile else profileName(tab)) ?: return null
        return runCatching { androidx.webkit.ProfileStore.getInstance().getProfile(name) }.getOrNull()
    }

    // ---- Cookie jars ----

    private fun defaultJars() = mutableListOf(
        Jar(newJarId(), app.getString(R.string.jar_work), Color.parseColor("#4A7FD9")),
        Jar(newJarId(), app.getString(R.string.jar_shopping), Color.parseColor("#E0A526")),
        Jar(newJarId(), app.getString(R.string.jar_banking), Color.parseColor("#3E9B5F")),
        Jar(newJarId(), app.getString(R.string.jar_social), Color.parseColor("#D45C9B")))

    private fun newJarId() = java.util.UUID.randomUUID().toString().replace("-", "").take(12)

    fun jar(id: String?) = jars.firstOrNull { it.id == id }

    fun newJar(name: String): Jar {
        val used = jars.map { it.color }.toSet()
        return Jar(newJarId(), name.take(24), PALETTE.firstOrNull { it !in used } ?: PALETTE[jars.size % PALETTE.size]).also { jars.add(it); saveLater() }
    }

    /** Puts [tab] in [jar] (null: Mica's own cookies). Its page starts again there, signed in as that jar is. */
    fun moveToJar(tab: Tab, jar: Jar?) {
        if (tab.isPrivate || tab.jar == jar?.id) return
        tab.jar = jar?.id
        val wasShown = tab.view != null
        destroyView(tab)
        tab.saved = null
        if (tab === active || inSplit(tab)) show(active) else if (wasShown) { /* loads when you open it */ }
        host?.onTabsChanged()
        saveLater()
    }

    /** The jar goes, with its cookies; its tabs go back to Mica's own. */
    fun deleteJar(jar: Jar) {
        tabs.filter { it.jar == jar.id }.forEach { moveToJar(it, null) }
        jars.remove(jar)
        try {
            val store = androidx.webkit.ProfileStore.getInstance()
            val name = JAR_PROFILE + jar.id
            if (name in store.allProfileNames) {
                if (!runCatching { store.deleteProfile(name) }.getOrDefault(false)) {
                    store.getProfile(name)?.let { p -> p.cookieManager.removeAllCookies(null); p.cookieManager.flush(); p.webStorage.deleteAllData() }
                    app.prefs.deletedJars = app.prefs.deletedJars + jar.id
                }
            }
        } catch (e: Exception) {}
        saveLater()
    }

    // ---- Tab groups ----

    fun group(id: String?) = groups.firstOrNull { it.id == id }
    fun groupsIn(spaceId: String = activeSpaceId) = groups.filter { it.space == spaceId }
    fun members(g: TabGroup) = tabs.filter { it.group == g.id }

    fun newGroup(tab: Tab, name: String, color: Int): TabGroup {
        val g = TabGroup("g" + System.currentTimeMillis().toString(36), name, color, tab.space)
        groups.add(g)
        addToGroup(tab, g)
        return g
    }

    /** Joins the group, placed after its last tab so a group's tabs stay together. */
    fun addToGroup(tab: Tab, g: TabGroup) {
        if (tab.space != g.space) return
        tab.group = g.id
        tab.pinned = false
        val last = tabs.indexOfLast { it.group == g.id && it !== tab }
        if (last >= 0) { tabs.remove(tab); tabs.add(tabs.indexOfLast { it.group == g.id } + 1, tab) }
        normalizeGroups(); host?.onTabsChanged(); saveLater()
    }

    fun leaveGroup(tab: Tab) {
        val g = group(tab.group) ?: return
        tab.group = null
        // Out of the middle of its group, to just after it.
        tabs.remove(tab); tabs.add(tabs.indexOfLast { it.group == g.id } + 1, tab)
        normalizeGroups(); host?.onTabsChanged(); saveLater()
    }

    fun ungroup(g: TabGroup) { members(g).forEach { it.group = null }; groups.remove(g); host?.onTabsChanged(); saveLater() }
    fun closeGroup(g: TabGroup) { members(g).forEach { close(it) }; groups.remove(g); host?.onTabsChanged(); saveLater() }
    fun setCollapsed(g: TabGroup, collapsed: Boolean) { g.collapsed = collapsed; host?.onTabsChanged(); saveLater() }

    /** Empty groups go; tabs that point at a missing group or another space's leave it; members sit together. */
    fun normalizeGroups() {
        for (t in tabs) if (t.group != null && group(t.group)?.space != t.space) t.group = null
        groups.removeAll { g -> tabs.none { it.group == g.id } }
        for (g in groups) {
            val first = tabs.indexOfFirst { it.group == g.id }
            val ms = tabs.filter { it.group == g.id }
            tabs.removeAll { it.group == g.id }
            tabs.addAll(first.coerceAtMost(tabs.size), ms)
        }
    }

    fun switchSpace(id: String) {
        if (id == activeSpaceId) return
        val space = spaces.firstOrNull { it.id == id } ?: return
        val target = tab(space.activeTab)?.takeIf { it.space == id } ?: tabsIn(id).maxByOrNull { it.lastUsed }
        if (target != null) activate(target) else { activeSpaceId = id; open(Navigation.HOME, activate = true, spaceId = id) }
    }

    fun addSpace(name: String, color: Int): Space {
        val space = Space("s" + System.currentTimeMillis().toString(36), name, color)
        spaces.add(space)
        switchSpace(space.id)
        return space
    }

    fun deleteSpace(space: Space) {
        if (spaces.size < 2) return
        val into = spaces.first { it !== space }
        tabs.filter { it.space == space.id }.forEach { it.space = into.id }
        spaces.remove(space)
        if (activeSpaceId == space.id) { activeSpaceId = into.id; activate(active ?: tabsIn(into.id).first()) }
        host?.onTabsChanged(); saveLater()
    }

    fun moveTo(tab: Tab, spaceId: String) {
        // A private tab's page lives in another profile; it can't cross over either way.
        if (tab.space == spaceId || tab.isPrivate || spaceId == PRIVATE) return
        tab.group = null
        val wasActive = tab === active
        if (wasActive) {
            val rest = tabsIn(tab.space).filter { it !== tab }
            tab.space = spaceId
            if (rest.isNotEmpty()) activate(rest.maxBy { it.lastUsed }) else { open(Navigation.HOME, spaceId = activeSpaceId) }
        } else tab.space = spaceId
        host?.onTabsChanged(); saveLater()
    }

    fun setPinned(tab: Tab, pinned: Boolean) { tab.pinned = pinned; host?.onTabsChanged(); saveLater() }

    fun setDesktop(tab: Tab, desktop: Boolean) {
        tab.desktop = desktop
        tab.view?.let { applyUa(it, tab); it.reload() }
        saveLater()
    }

    // ---- Battery and memory ----

    // Desktop's BrowserMemory, for a phone. A tab you leave is paused at once; 15 seconds later its page memory
    // goes back (WebView purges, nothing reloads). After a few minutes out of sight it rests: the page is dropped and
    // comes back from its history when opened, behind a picture of how you left it. Away from Mica for a minute,
    // every hidden tab rests. Tabs that play, load, or hold text you typed are left alone.
    private val lowRam = app.getSystemService(ActivityManager::class.java).let { am ->
        am.isLowRamDevice || ActivityManager.MemoryInfo().also { am.getMemoryInfo(it) }.totalMem < (4L shl 30)
    }
    /** Live tabs besides the one you're on (and its split partner). */
    private val liveExtra = if (lowRam) 0 else 1
    private val freezeAfter = 15_000L
    private val restAfter = (if (lowRam) 2L else 3L) * 60_000
    /** A tab you left this recently is spared by the budget, so flicking between a few never reloads them. */
    private val budgetSpare = 45_000L
    private val awayAfter = 60_000L
    private var lastPurge = 0L
    private val balanceSoon = Runnable { balance() }
    private val awaySoon = Runnable { away() }

    /** [tab] goes out of sight but stays live: pictured (for when it comes back from rest), paused and timed. */
    private fun hide(tab: Tab) {
        val v = tab.view ?: return
        if (v.parent != null && !inBackground && host?.pageCovered == false) snapshot(tab, v)
        if (Navigation.isWeb(tab.url)) v.evaluateJavascript(TYPED_INPUT_SCRIPT) { tab.typed = it == "true" }
        (v.parent as? ViewGroup)?.removeView(v)
        if (!tab.playing) v.onPause()
        tab.hiddenAt = System.currentTimeMillis()
        tab.frozen = false
        main.removeCallbacks(balanceSoon)
        main.postDelayed(balanceSoon, freezeAfter + 500)
    }

    private fun idle(t: Tab) = t.view != null && t !== active && !inSplit(t) && !t.playing && !t.typed && t.progress >= 100

    /** Every 15 seconds while a hidden tab is live: freeze, rest, budget. */
    private fun balance() {
        main.removeCallbacks(balanceSoon)
        if (inBackground) return
        val now = System.currentTimeMillis()
        var froze = false
        val live = tabs.count { it.view != null }
        for (t in tabs.toList()) {
            if (t.view != null && t !== active && !inSplit(t) && t.hiddenAt == 0L) t.hiddenAt = now
            if (!idle(t)) continue
            if (now - t.hiddenAt >= restAfter) { rest(t); continue }
            if (!t.frozen && now - t.hiddenAt >= freezeAfter) { t.frozen = true; froze = true }
        }
        enforceBudget(now)
        // A page that rests leaves its memory in WebView's renderer until it's told to give it back.
        if (froze || tabs.count { it.view != null } < live) purge(now)
        snapshots.keys.retainAll(tabs.mapTo(HashSet()) { it.id })
        if (tabs.any { it.view != null && it.hiddenAt > 0 }) main.postDelayed(balanceSoon, 15_000)
    }

    /** At most the active tab plus [liveExtra] more stay live; past that, the ones used longest ago rest. */
    private fun enforceBudget(now: Long = System.currentTimeMillis()) {
        val act = active
        tabs.filter { it.view != null && it !== act && !it.playing && !inSplit(it) }
            .sortedByDescending { it.lastUsed }
            .drop(liveExtra)
            .filter { idle(it) && it.hiddenAt > 0 && now - it.hiddenAt >= budgetSpare }
            .forEach { rest(it) }
    }

    /** Frozen pages give their memory back, at most once a minute and never while something plays (it can hitch the sound). */
    private fun purge(now: Long) {
        if (now - lastPurge < 60_000 || tabs.any { it.playing }) return
        lastPurge = now
        app.purgeEngine()
    }

    /** A minute in another app (or with the screen off): hidden tabs rest and WebView gives back what it can. */
    private fun away() {
        if (!inBackground) return
        tabs.filter { idle(it) }.forEach { rest(it) }
        if (tabs.none { it.playing }) { lastPurge = System.currentTimeMillis(); app.purgeEngine() }
        main.postDelayed(longAwaySoon, longAwayAfter - awayAfter)
    }

    /**
     * Gone for a while (desktop's minimized window): the tab you were on rests too. With no page left, WebView's
     * renderer ends and everything it held goes back to the phone; the page reloads behind its picture when you return.
     */
    private val longAwayAfter = 5 * 60_000L
    private val longAwaySoon = Runnable { longAway() }

    private fun longAway() {
        if (!inBackground || tabs.any { it.playing }) return
        tabs.filter { it.view != null && !it.typed && it.progress >= 100 }.forEach { rest(it) }
    }

    /** Leaving Mica: a picture of the page you're on (for if it rests while you're away), and whether you typed into it. */
    fun onLeaving() {
        val t = active ?: return
        val v = t.view ?: return
        if (!inSplit(t) && v.parent != null && host?.pageCovered == false) snapshot(t, v)
        if (Navigation.isWeb(t.url)) v.evaluateJavascript(TYPED_INPUT_SCRIPT) { t.typed = it == "true" }
    }

    // ---- Pictures of tabs (desktop's snapshots) ----

    /** How each tab looked when you left it, as a small JPEG, shown while a resting tab loads again. */
    private val snapshots = LinkedHashMap<String, ByteArray>()
    private val pictures by lazy { java.util.concurrent.Executors.newSingleThreadExecutor { r -> Thread(r, "mica-pictures").apply { priority = Thread.MIN_PRIORITY } } }
    private var cover: ImageView? = null

    private fun snapshot(tab: Tab, v: WebView) {
        val window = host?.activity?.window ?: return
        if (!Navigation.isWeb(tab.url) || v.width < 50 || v.height < 50 || !v.isAttachedToWindow) return
        val at = IntArray(2).also { v.getLocationInWindow(it) }
        val bmp = Bitmap.createBitmap(v.width / 2, v.height / 2, Bitmap.Config.RGB_565)
        try {
            PixelCopy.request(window, Rect(at[0], at[1], at[0] + v.width, at[1] + v.height), bmp, { result ->
                if (result != PixelCopy.SUCCESS) { bmp.recycle(); return@request }
                pictures.execute {
                    val out = java.io.ByteArrayOutputStream()
                    bmp.compress(Bitmap.CompressFormat.JPEG, 70, out)
                    bmp.recycle()
                    main.post {
                        if (tabs.none { it.id == tab.id }) return@post
                        snapshots.remove(tab.id)
                        snapshots[tab.id] = out.toByteArray()
                        while (snapshots.size > 12) snapshots.remove(snapshots.keys.first())
                    }
                }
            }, main)
        } catch (e: IllegalArgumentException) { bmp.recycle() }
    }

    private fun coverWhileLoading(tab: Tab) {
        val h = host ?: return
        val jpeg = snapshots[tab.id] ?: return
        val bmp = BitmapFactory.decodeByteArray(jpeg, 0, jpeg.size) ?: return
        val iv = ImageView(h.activity).apply {
            setImageBitmap(bmp); scaleType = ImageView.ScaleType.FIT_XY; tag = tab.id
            importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO
        }
        h.webHost.addView(iv, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        cover = iv
        // A page that never shows still gets uncovered.
        main.postDelayed({ if (cover === iv) dropCover() }, 8000)
    }

    private fun dropCover() {
        val c = cover ?: return
        cover = null
        c.animate().alpha(0f).setDuration(160).withEndAction { (c.parent as? ViewGroup)?.removeView(c) }
    }

    fun rest(tab: Tab) {
        val v = tab.view ?: return
        if (tab.playing || tab === active && host != null && !inBackground) return
        if (inSplit(tab) && host != null && !inBackground) return
        tab.saved = Bundle().also { v.saveState(it) }
        destroyView(tab)
        host?.onTabsChanged()
    }

    // ---- Media in the background (MediaService) ----

    /** The tab the media notification is for: playing, or paused from the notification. */
    var mediaTab: Tab? = null
        private set
    /** Paused from the notification, lock screen or a headset. */
    var mediaHeld = false
        private set

    private fun onMediaChanged(tab: Tab) {
        if (tab.playing) {
            if (mediaTab !== tab) { mediaTab = tab; mediaHeld = false; MediaService.update(app) }
            else { mediaHeld = false; MediaService.update(app) }
        } else if (mediaTab === tab) MediaService.update(app)
    }

    fun mediaPause() { mediaTab?.view?.evaluateJavascript(MEDIA_PAUSE, null); mediaHeld = true }
    fun mediaPlay() { mediaTab?.view?.evaluateJavascript(MEDIA_PLAY, null); mediaHeld = false }
    fun mediaStop() { mediaTab?.view?.evaluateJavascript(MEDIA_PAUSE, null); mediaTab = null; mediaHeld = false }

    private fun destroyView(tab: Tab) {
        if (mediaTab === tab) { mediaTab = null; MediaService.update(app) }
        val v = tab.view ?: return
        tab.view = null
        tab.hiddenAt = 0
        tab.frozen = false
        tab.playingFrames.clear()
        (v.parent as? ViewGroup)?.removeView(v)
        v.stopLoading()
        v.destroy()
    }

    fun onBackground() {
        inBackground = true
        var anyPlaying = false
        for (t in tabs) {
            val v = t.view ?: continue
            if (t.playing) anyPlaying = true else v.onPause()
        }
        if (!anyPlaying) tabs.firstNotNullOfOrNull { it.view }?.pauseTimers()
        main.removeCallbacks(balanceSoon)
        main.removeCallbacks(awaySoon)
        main.removeCallbacks(longAwaySoon)
        main.postDelayed(awaySoon, awayAfter)
        save()
    }

    fun onForeground() {
        if (!inBackground) return
        inBackground = false
        main.removeCallbacks(awaySoon)
        main.removeCallbacks(longAwaySoon)
        // The page you were on rested while you were away: it comes back now, behind its picture.
        active?.let { if (!it.isHome && it.view == null) show(it) }
        tabs.firstNotNullOfOrNull { it.view }?.resumeTimers()
        active?.view?.onResume()
        balance()
    }

    fun trim(level: Int) {
        @Suppress("DEPRECATION")
        if (level < ComponentCallbacks2.TRIM_MEMORY_RUNNING_LOW || level == ComponentCallbacks2.TRIM_MEMORY_UI_HIDDEN) return
        val act = active
        tabs.filter { it.view != null && it !== act && !it.playing }.forEach { rest(it) }
    }

    // ---- Saving ----

    fun saveLater() { main.removeCallbacks(saveSoon); main.postDelayed(saveSoon, 2000) }

    fun save() {
        main.removeCallbacks(saveSoon)
        store.save(Store.State(spaces, tabs, if (activeSpaceId == PRIVATE) lastSpaceId else activeSpaceId, history, bookmarks, readLater, recentlyClosed, groups, jars))
    }

    private fun remember(url: String, title: String) {
        if (!Navigation.isWeb(url) && Navigation.fromEngine(url) == url) return
        val shown = Navigation.fromEngine(url)
        if (Reader.isReader(shown)) return
        val was = history.firstOrNull { it.url == shown }
        history.removeAll { it.url == shown }
        history.add(0, HistoryEntry(shown, title.take(1024), System.currentTimeMillis(), (was?.visits ?: 0) + 1))
        while (history.size > 500) history.removeAt(history.size - 1)
        saveLater()
    }

    /**
     * The sites you visit most (desktop's TopSites): history grouped by origin, each scored by its pages'
     * visits, most recent first on a tie, shown by the page you were on last.
     */
    fun topSites(count: Int): List<HistoryEntry> = history.filter { Navigation.isWeb(it.url) }
        .groupBy { origin(it.url) }
        .map { (_, pages) -> Triple(pages.maxBy { it.at }, pages.sumOf { it.visits.coerceAtLeast(1) }, pages.maxOf { it.at }) }
        .sortedWith(compareByDescending<Triple<HistoryEntry, Int, Long>> { it.second }.thenByDescending { it.third }.thenBy { it.first.url })
        .take(count).map { it.first }

    /** Takes a site off the home page: its pages leave history. */
    fun forgetSite(url: String) {
        val o = origin(url)
        history.removeAll { origin(it.url) == o }
        saveLater()
    }

    private fun origin(url: String) = android.net.Uri.parse(url).let { u -> (u.scheme + "://" + u.host + (if (u.port > 0) ":" + u.port else "")).lowercase() }

    // ---- Clearing data ----

    /**
     * Settings → Clear browsing data (desktop's dialog). History goes by [since] (null: all of it, recently closed
     * too); WebView keeps cookies, storage and cache without dates, so those always go whole. Private tabs keep theirs.
     */
    fun clearData(since: Long?, history: Boolean, cookies: Boolean, cache: Boolean, forms: Boolean) {
        if (history) {
            if (since == null) { this.history.clear(); recentlyClosed.clear() } else this.history.removeAll { it.at >= since }
            saveLater()
        }
        if (cookies) {
            android.webkit.CookieManager.getInstance().removeAllCookies(null)
            android.webkit.CookieManager.getInstance().flush()
            android.webkit.WebStorage.getInstance().deleteAllData()
        }
        if (cache) {
            // The cache belongs to the whole profile, so any of its WebViews clears it; borrow one if none is open.
            val v = tabs.firstOrNull { it.view != null && !it.isPrivate }?.view
            if (v != null) v.clearCache(true) else WebView(context).apply { clearCache(true); destroy() }
        }
        @Suppress("DEPRECATION")
        if (forms) android.webkit.WebViewDatabase.getInstance(app).clearFormData()
    }

    // ---- Saved sessions ----

    /** Every website tab outside private browsing, for Sessions.save. */
    fun sessionTabs() = tabs.filter { !it.isPrivate && Navigation.isWeb(it.url) }

    /**
     * A saved session comes back beside what's open, never instead of it: one folded group per space, named
     * after the session, its tabs resting until you open them.
     */
    fun restore(session: Sessions.Saved): Int {
        var n = 0
        val used = groups.map { it.color }.toMutableSet()
        for ((spaceId, pages) in session.tabs.filter { Navigation.isWeb(it.url) }.groupBy { p -> p.space.takeIf { id -> spaces.any { it.id == id && id != PRIVATE } } ?: spaces.first { it.id != PRIVATE }.id }) {
            val color = PALETTE.firstOrNull { it !in used } ?: PALETTE[groups.size % PALETTE.size]
            used.add(color)
            val g = TabGroup("g" + System.currentTimeMillis().toString(36) + n, session.name, color, spaceId, collapsed = true)
            groups.add(g)
            for (p in pages) {
                tabs.add(Tab(space = spaceId, url = p.url, title = p.title).apply { group = g.id; lastUsed = System.currentTimeMillis() - 60_000 })
                n++
            }
        }
        normalizeGroups()
        host?.onTabsChanged()
        saveLater()
        return n
    }

    // ---- WebViews ----

    /** A tab's WebView, with Highlight added to the menu that comes up over selected text. */
    @SuppressLint("ViewConstructor")
    private inner class PageView(c: Context, val tab: Tab) : WebView(c) {
        override fun startActionMode(callback: android.view.ActionMode.Callback, type: Int): android.view.ActionMode? =
            super.startActionMode(if (Navigation.isWeb(tab.url) || Reader.isReader(tab.url)) WithHighlight(callback) else callback, type)

        private inner class WithHighlight(val inner: android.view.ActionMode.Callback) : android.view.ActionMode.Callback2() {
            override fun onCreateActionMode(mode: android.view.ActionMode, menu: android.view.Menu): Boolean {
                val ok = inner.onCreateActionMode(mode, menu)
                if (menu.findItem(HIGHLIGHT_ITEM) == null) menu.add(android.view.Menu.NONE, HIGHLIGHT_ITEM, 0, R.string.highlight)
                return ok
            }
            override fun onPrepareActionMode(mode: android.view.ActionMode, menu: android.view.Menu) = inner.onPrepareActionMode(mode, menu)
            override fun onActionItemClicked(mode: android.view.ActionMode, item: android.view.MenuItem): Boolean {
                if (item.itemId != HIGHLIGHT_ITEM) return inner.onActionItemClicked(mode, item)
                highlight(tab) { text -> host?.announce(app.getString(if (text.isNullOrBlank()) R.string.highlight_nothing else R.string.highlighted)) }
                mode.finish()
                return true
            }
            override fun onDestroyActionMode(mode: android.view.ActionMode) = inner.onDestroyActionMode(mode)
            override fun onGetContentRect(mode: android.view.ActionMode, view: View, outRect: android.graphics.Rect) {
                if (inner is android.view.ActionMode.Callback2) inner.onGetContentRect(mode, view, outRect) else super.onGetContentRect(mode, view, outRect)
            }
        }
    }

    @SuppressLint("SetJavaScriptEnabled")
    private fun createView(tab: Tab): WebView {
        val v = PageView(context, tab)
        // First, before anything else touches the WebView: a private tab's or a jar's own cookies, storage and cache.
        useProfile(v, tab)
        tab.view = v
        v.setBackgroundColor(app.getColor(R.color.mica_canvas))
        v.settings.apply {
            javaScriptEnabled = true
            domStorageEnabled = true
            mediaPlaybackRequiresUserGesture = true
            setSupportMultipleWindows(true)
            // On, so a window opened without a tap reaches onCreateWindow, where Mica blocks it and says so.
            javaScriptCanOpenWindowsAutomatically = true
            builtInZoomControls = true
            displayZoomControls = false
            loadWithOverviewMode = true
            useWideViewPort = true
            allowFileAccess = false
            allowContentAccess = false
            // Sites still ask first (SitePermissions), and Android's own permission is needed too.
            setGeolocationEnabled(true)
        }
        applyUa(v, tab)
        applyAutofill(v)
        // Passkeys for every site, as a browser: WebView hands navigator.credentials to Android's Credential
        // Manager with the page's origin. Providers (Google Password Manager, Bitwarden…) only answer apps on
        // their privileged-browser list; until Mica is on it, sites see an error and offer their usual sign-in.
        if (WebViewFeature.isFeatureSupported(WebViewFeature.WEB_AUTHENTICATION))
            WebSettingsCompat.setWebAuthenticationSupport(v.settings, WebSettingsCompat.WEB_AUTHENTICATION_SUPPORT_FOR_BROWSER)
        if (WebViewFeature.isFeatureSupported(WebViewFeature.DOCUMENT_START_SCRIPT) && WebViewFeature.isFeatureSupported(WebViewFeature.WEB_MESSAGE_LISTENER)) {
            WebViewCompat.addWebMessageListener(v, "micaMedia", setOf("*")) { _, message, _, _, reply ->
                val was = tab.playing
                if (message.data == "1") tab.playingFrames.add(reply) else tab.playingFrames.remove(reply)
                if (was != tab.playing) onMediaChanged(tab)
                host?.onTabsChanged()
            }
            WebViewCompat.addDocumentStartJavaScript(v, MEDIA_SCRIPT, setOf("*"))
            // Login forms, submitted passwords and 2FA boxes (desktop's scripts): top-level HTTPS pages only.
            // Not in private tabs: nothing is offered for saving there, so nothing is kept.
            if (!tab.isPrivate) {
                WebViewCompat.addWebMessageListener(v, "micaLogin", setOf("*")) { _, message, origin, isMainFrame, _ ->
                    val data = message.data ?: return@addWebMessageListener
                    if (isMainFrame && origin.scheme == "https" && data.length < 80_000) host?.onLoginMessage(tab, origin.toString(), data)
                }
                WebViewCompat.addDocumentStartJavaScript(v, loginScripts, setOf("*"))
            }
            // A file the page made itself, handed over in pieces (Blobs); only messages carrying a token Mica gave count.
            WebViewCompat.addDocumentStartJavaScript(v, Blobs.NAME_SCRIPT, setOf("*"))
            WebViewCompat.addWebMessageListener(v, "micaBlob", setOf("*")) { _, message, _, _, _ -> message.data?.let { if (it.length < 600_000) blobs.onMessage(it) } }
            // Reading mode's own buttons (size, typeface, save, the original): only Mica's reader page can send these.
            WebViewCompat.addWebMessageListener(v, "micaReader", setOf("https://" + Reader.HOST)) { _, message, _, isMainFrame, _ ->
                val data = message.data ?: return@addWebMessageListener
                if (isMainFrame && data.length < 100) host?.onReaderMessage(tab, data)
            }
        }
        v.webViewClient = Client(tab)
        v.webChromeClient = Chrome(tab)
        v.setDownloadListener { url, ua, cd, mime, _ ->
            if (url.startsWith("blob:", true) || url.startsWith("data:", true)) blobs.start(v, url, cd, mime) else host?.download(url, ua, cd, mime)
        }
        var lastY = 0
        v.setOnScrollChangeListener { _, _, y, _, _ ->
            if (tab === active) host?.onScrolled(y - lastY, y == 0)
            lastY = y
        }
        v.setOnLongClickListener { host?.onLinkLongPress(tab, v.hitTestResult) ?: false }
        return v
    }

    // ---- Peek ----

    /**
     * The page inside a peek (desktop's BrowserPeek): ads blocked as in tabs, but no permissions, downloads,
     * windows or apps, and it only goes to another page when you tap. Peeking from a private tab stays private.
     */
    fun peekView(source: Tab, changed: (WebView) -> Unit): WebView {
        val peek = Tab(space = source.space, url = "")
        val v = WebView(context)
        useProfile(v, source)
        v.setBackgroundColor(app.getColor(R.color.mica_canvas))
        v.settings.apply {
            javaScriptEnabled = true
            domStorageEnabled = true
            mediaPlaybackRequiresUserGesture = true
            setSupportMultipleWindows(false)
            javaScriptCanOpenWindowsAutomatically = false
            builtInZoomControls = true
            displayZoomControls = false
            loadWithOverviewMode = true
            useWideViewPort = true
            allowFileAccess = false
            allowContentAccess = false
            setGeolocationEnabled(false)
        }
        applyUa(v, source)
        applyAutofill(v)
        v.webViewClient = object : WebViewClient() {
            override fun shouldInterceptRequest(view: WebView, request: WebResourceRequest): WebResourceResponse? {
                if (request.isForMainFrame) { peek.pageHost = request.url.host?.lowercase().orEmpty(); peek.blocked.set(0) }
                return Blocker.check(peek, request, ::blockedPage)
            }
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
                val scheme = request.url.scheme?.lowercase()
                if (scheme != "http" && scheme != "https") return true
                return request.isForMainFrame && !request.hasGesture() && !request.isRedirect
            }
            private var failure: Failure? = null
            override fun onReceivedError(view: WebView, request: WebResourceRequest, error: android.webkit.WebResourceError) { failed(view, request, error)?.let { failure = it } }
            override fun onPageStarted(view: WebView, url: String, favicon: Bitmap?) { failure = null; applyZoom(view, Domains.hostOf(url)); changed(view) }
            override fun onPageCommitVisible(view: WebView, url: String) { view.setBackgroundColor(Color.WHITE) }
            override fun onPageFinished(view: WebView, url: String) { failure?.let { failure = null; showFailure(view, it) }; changed(view) }
            override fun onRenderProcessGone(view: WebView, detail: RenderProcessGoneDetail) = true
        }
        v.webChromeClient = object : WebChromeClient() {
            override fun onReceivedTitle(view: WebView, title: String?) = changed(view)
            override fun onPermissionRequest(request: android.webkit.PermissionRequest) = request.deny()
            override fun onGeolocationPermissionsShowPrompt(origin: String, callback: android.webkit.GeolocationPermissions.Callback) = callback.invoke(origin, false, false)
        }
        return v
    }

    // ---- Site card ----

    /** Text size for this site, as a percentage, applied as pages start (desktop keeps zoom per site too). */
    fun applyZoom(v: WebView, host: String) {
        val z = app.prefs.zoomFor(host)
        if (v.settings.textZoom != z) v.settings.textZoom = z
    }

    fun setZoom(tab: Tab, percent: Int) {
        val host = tab.pageHost ?: return
        app.prefs.setZoom(host, percent)
        tabs.forEach { t -> if (t.pageHost == host) t.view?.let { applyZoom(it, host) } }
    }

    /** Cookies and stored data for the tab's site (in the private profile for a private tab), then a reload. */
    fun clearSiteData(tab: Tab) {
        val host = tab.pageHost ?: return
        val profile = profileOf(tab)
        if ((tab.isPrivate || tab.jar != null && privateSupported) && profile == null) return
        val cookies = profile?.cookieManager ?: android.webkit.CookieManager.getInstance()
        val storage = profile?.webStorage ?: android.webkit.WebStorage.getInstance()
        val site = Domains.registrable(host)
        for (scheme in listOf("https://", "http://")) {
            val names = cookies.getCookie(scheme + host)?.split(';')?.mapNotNull { it.substringBefore('=').trim().takeIf { n -> n.isNotEmpty() } }.orEmpty()
            for (n in names) for (domain in listOf("", "; Domain=" + host, "; Domain=." + site)) cookies.setCookie(scheme + host, "$n=; Max-Age=0; Path=/$domain")
            storage.deleteOrigin(scheme + host)
        }
        cookies.flush()
        tab.view?.reload()
    }

    private fun applyUa(v: WebView, tab: Tab) {
        v.settings.userAgentString = if (tab.desktop) desktopUa else mobileUa
        v.settings.useWideViewPort = true
    }

    /**
     * One password manager at a time: while Mica saves passwords, pages are hidden from Android's autofill
     * service (Google, Bitwarden…), so you aren't asked twice; turned off, that service takes over.
     */
    private fun applyAutofill(v: WebView) {
        if (android.os.Build.VERSION.SDK_INT >= 26)
            v.importantForAutofill = if (app.prefs.micaPasswords) View.IMPORTANT_FOR_AUTOFILL_NO_EXCLUDE_DESCENDANTS else View.IMPORTANT_FOR_AUTOFILL_AUTO
    }

    fun applyAutofillEverywhere() = tabs.forEach { t -> t.view?.let { applyAutofill(it) } }

    /** After a theme change: flip open game pages in place, like ThemeGames on desktop. */
    fun themeGames() {
        val script = Games.THEME_SCRIPT.format(if (Games.dark) "true" else "false")
        tabs.forEach { t -> t.view?.let { v -> if (Navigation.isGameHost(Domains.hostOf(v.url ?: ""))) v.evaluateJavascript(script, null) } }
    }

    private fun blockedPage(host: String): String {
        val title = app.getString(R.string.blocked_page_title)
        val text = app.getString(R.string.blocked_page_text, host).replace("<", "&lt;")
        return "<!doctype html><meta name=viewport content='width=device-width'><title>$title</title>" +
            "<body style='font:16px sans-serif;margin:0;display:grid;place-items:center;min-height:100vh;background:#FAF9F6;color:#2A2530'>" +
            "<div style='max-width:320px;padding:24px'><h2 style='color:#71608C;font-weight:500'>$title</h2><p>$text</p></div>"
    }

    /** A main-frame load that failed, so the page that finishes next gets Mica's error screen instead. */
    private class Failure(val url: String, val code: Int, val description: String, val insecure: String? = null, val cert: Int? = null)

    private fun failed(view: WebView, request: WebResourceRequest, error: android.webkit.WebResourceError): Failure? {
        if (!request.isForMainFrame) return null
        val d = error.description?.toString() ?: ""
        // A navigation that was replaced or cancelled isn't an error you need to see.
        if (d.contains("ERR_ABORTED") || error.errorCode == WebViewClient.ERROR_UNSUPPORTED_SCHEME) return null
        return Failure(request.url.toString(), error.errorCode, d)
    }

    private fun showFailure(view: WebView, f: Failure) = view.evaluateJavascript(ErrorPage.script(app, f.url, f.code, f.description, f.insecure, f.cert), null)

    private inner class Client(val tab: Tab) : WebViewClient() {
        private var failure: Failure? = null
        // Shown on onPageFinished, but some WebViews commit their own error page just after; it goes over that too.
        private var shown: Failure? = null

        override fun onReceivedError(view: WebView, request: WebResourceRequest, error: android.webkit.WebResourceError) {
            failed(view, request, error)?.let { f ->
                // The https:// twin of an address you typed as http:// didn't load: offer the plain one.
                val was = tab.upgraded
                failure = if (was != null && Domains.hostOf(was) == request.url.host?.lowercase()) Failure(f.url, f.code, f.description, was) else f
            }
        }

        override fun onReceivedHttpAuthRequest(view: WebView, handler: android.webkit.HttpAuthHandler, host: String, realm: String?) {
            if (tab !== active || this@Browser.host == null) handler.cancel() else this@Browser.host?.onHttpAuth(tab, host, handler)
        }

        override fun onReceivedSslError(view: WebView, handler: android.webkit.SslErrorHandler, error: android.net.http.SslError) {
            // A certificate that doesn't check out stops the page, unless you chose Go anyway for that host this session.
            // If it's the https:// twin Mica tried, the plain address is offered instead.
            val host = Uri.parse(error.url).host?.lowercase()
            if (host != null && Https.certificateAllowed(host, tab.isPrivate)) { handler.proceed(); return }
            handler.cancel()
            val was = tab.upgraded
            failure = Failure(error.url, ERROR_FAILED_SSL_HANDSHAKE, "ERR_CERT_INVALID", was?.takeIf { Domains.hostOf(it) == host }, error.primaryError)
        }
        override fun shouldInterceptRequest(view: WebView, request: WebResourceRequest): WebResourceResponse? {
            val url = request.url.toString()
            val host = request.url.host?.lowercase().orEmpty()
            if (request.isForMainFrame) { tab.pageHost = host; tab.blocked.set(0) }
            if (Navigation.isGameHost(host) && url.startsWith("https://")) return Games.serve(app, url, host, request.method)
            if (BuildConfig.DEBUG && host == "login.mica.test" && url.startsWith("https://")) return testPage(request.url.path)
            if (host == Reader.HOST && url.startsWith("https://") && request.isForMainFrame && request.method == "GET")
                return Reader.serve(app, url, app.prefs.readerSize, app.prefs.readerSerif)
            return Blocker.check(tab, request, ::blockedPage)
        }

        override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
            val url = request.url.toString()
            val scheme = request.url.scheme?.lowercase()
            // "Continue without HTTPS" on Mica's error page: only from a tap, and only to a plain web address.
            if (request.url.host == Https.CONTINUE_HOST) {
                val plain = request.url.getQueryParameter("u")
                if (request.hasGesture() && plain != null && plain.startsWith("http://", true)) { Https.allow(Domains.hostOf(plain)); tab.upgraded = null; view.loadUrl(plain) }
                return true
            }
            // "Go to … anyway" on the certificate error page: only from a tap, and only to an https:// address.
            if (request.url.host == Https.CERT_HOST) {
                val target = request.url.getQueryParameter("u")
                val h = target?.let { Uri.parse(it).host }
                if (request.hasGesture() && target != null && h != null && target.startsWith("https://", true)) { Https.allowCertificate(h, tab.isPrivate); view.loadUrl(target) }
                return true
            }
            if (scheme == "http" && request.isForMainFrame) {
                val host = request.url.host?.lowercase().orEmpty()
                // The secure address sent you back to the plain one: the site has no HTTPS, so stop trying.
                val was = tab.upgraded
                if (request.isRedirect && was != null && Domains.registrable(Domains.hostOf(was)) == Domains.registrable(host)) {
                    Https.allow(host); tab.upgraded = null
                    host.let { h -> this@Browser.host?.announce(app.getString(R.string.no_https_redirect, h)) }
                    return false
                }
                val secure = Https.upgrade(url) ?: return false
                tab.upgraded = url
                view.loadUrl(secure)
                return true
            }
            if (scheme == "http" || scheme == "https" || scheme == "about" || scheme == "data" || scheme == "blob" || scheme == "javascript") return false
            if (scheme == "mica") { view.loadUrl(Navigation.engineAddress(url)); return true }
            // Apps only open from a tap, so a page can't bounce you into one on its own.
            if (!request.hasGesture()) return true
            return host?.openExternal(url) { fallback -> view.loadUrl(fallback) } ?: true
        }

        override fun onPageStarted(view: WebView, url: String, favicon: Bitmap?) {
            // A reload that fails reports the error before its error page starts, so that failure stays.
            if (failure?.url != url) failure = null
            shown = null
            (view.webChromeClient as? Chrome)?.reset()
            tab.url = Navigation.fromEngine(url)
            tab.pageHost = Domains.hostOf(url)
            applyZoom(view, tab.pageHost ?: "")
            tab.tint = 0
            tab.icon = favicon
            host?.onPageChanged(tab)
        }

        // Mica's canvas while a page loads (no white flash in dark mode); once it shows, a page that paints
        // no background of its own gets white, as in any browser, so its dark text stays readable.
        override fun onPageCommitVisible(view: WebView, url: String) {
            shown?.let { f -> shown = null; if (f.url == url) showFailure(view, f) }
            if (cover?.tag == tab.id) dropCover()
            view.setBackgroundColor(if (Navigation.isGameHost(Domains.hostOf(url))) app.getColor(R.color.mica_canvas) else Color.WHITE)
        }

        override fun onPageFinished(view: WebView, url: String) {
            failure?.let { f -> failure = null; shown = f; showFailure(view, f); tab.url = Navigation.fromEngine(url); host?.onPageChanged(tab); return }
            if (url.startsWith("https://")) tab.upgraded = null
            tab.url = Navigation.fromEngine(url)
            view.title?.let { if (it.isNotBlank() && !it.startsWith("http")) tab.title = it }
            if (!tab.isPrivate) remember(url, tab.title)
            if (Navigation.isWeb(url)) view.evaluateJavascript(TINT_SCRIPT) { value -> tab.tint = parseColor(value); if (tab === active) host?.onPageChanged(tab) }
            val marks = if (tab.isPrivate) emptyList() else highlights.on(pageKey(tab))
            if (marks.isNotEmpty()) view.evaluateJavascript(Highlights.restoreScript(marks), null)
            val n = tab.blocked.get()
            if (n > 0 && tab === active) host?.announce(app.getString(R.string.island_blocked, n))
            host?.onPageChanged(tab)
            saveLater()
        }

        override fun doUpdateVisitedHistory(view: WebView, url: String, isReload: Boolean) {
            tab.url = Navigation.fromEngine(url)
            host?.onPageChanged(tab)
        }

        override fun onRenderProcessGone(view: WebView, detail: RenderProcessGoneDetail): Boolean {
            // The renderer crashed or was killed for memory: drop this WebView and reload the tab when shown.
            if (tab.view === view) {
                tab.view = null
                tab.playingFrames.clear()
                (view.parent as? ViewGroup)?.removeView(view)
                view.destroy()
                if (tab === active) show(tab)
            }
            return true
        }
    }

    private inner class Chrome(val tab: Tab) : WebChromeClient() {
        override fun onProgressChanged(view: WebView, newProgress: Int) { tab.progress = newProgress; host?.onProgress(tab) }
        override fun onReceivedTitle(view: WebView, title: String?) {
            if (!title.isNullOrBlank() && !title.startsWith("http")) { tab.title = title; host?.onPageChanged(tab) }
        }
        override fun onReceivedIcon(view: WebView, icon: Bitmap?) {
            tab.icon = icon?.let { if (it.width > 48) Bitmap.createScaledBitmap(it, 48, 48 * it.height / it.width, true) else it }
        }
        override fun onShowCustomView(view: View, callback: CustomViewCallback) { host?.showCustomView(view, callback) ?: callback.onCustomViewHidden() }
        override fun onHideCustomView() { host?.hideCustomView() }
        override fun onShowFileChooser(webView: WebView, callback: ValueCallback<Array<Uri>>, params: FileChooserParams): Boolean =
            host?.chooseFiles(callback, params) ?: false

        override fun onCreateWindow(view: WebView, isDialog: Boolean, isUserGesture: Boolean, resultMsg: Message): Boolean {
            if (!isUserGesture) {
                // Blocked, but say where it wanted to go: a WebView that may not touch the network learns the
                // address and is thrown away.
                if (tab !== active || host == null) return false
                val probe = WebView(context)
                probe.settings.blockNetworkLoads = true
                probe.settings.javaScriptEnabled = false
                var seen = false
                fun got(u: String) {
                    if (seen) return
                    seen = true
                    main.post { probe.stopLoading(); probe.destroy() }
                    if (Navigation.isWeb(u)) host?.onPopupBlocked(tab, u)
                }
                probe.webViewClient = object : WebViewClient() {
                    override fun shouldOverrideUrlLoading(v: WebView, r: WebResourceRequest): Boolean { got(r.url.toString()); return true }
                    override fun onPageStarted(v: WebView, url: String, favicon: Bitmap?) { if (url != "about:blank") got(url) }
                }
                main.postDelayed({ if (!seen) { seen = true; probe.destroy() } }, 3000)
                (resultMsg.obj as WebView.WebViewTransport).webView = probe
                resultMsg.sendToTarget()
                return true
            }
            val child = Tab(space = tab.space, url = "")
            child.parent = tab.id
            child.desktop = tab.desktop
            child.jar = tab.jar
            val at = tabs.indexOf(tab) + 1
            tabs.add(at, child)
            val v = createView(child)
            (resultMsg.obj as WebView.WebViewTransport).webView = v
            resultMsg.sendToTarget()
            activate(child)
            return true
        }

        override fun onPermissionRequest(request: android.webkit.PermissionRequest) { host?.onSitePermission(tab, request) ?: request.deny() }
        override fun onPermissionRequestCanceled(request: android.webkit.PermissionRequest) { host?.onSitePermissionCanceled(request) }
        override fun onGeolocationPermissionsShowPrompt(origin: String, callback: android.webkit.GeolocationPermissions.Callback) {
            host?.onGeolocation(tab, origin, callback) ?: callback.invoke(origin, false, false)
        }

        // The page's own dialogs, as Mica cards: never from a tab you can't see, and at most 3 in 10 s.
        private val dialogTimes = ArrayDeque<Long>()
        private var dialogsMuted = false

        private fun dialog(view: WebView, url: String?, kind: Int, message: String?, default: String?, result: android.webkit.JsResult): Boolean {
            val h = host
            val origin = Domains.hostOf(url ?: view.url ?: "")
            if (h == null || dialogsMuted) { result.cancel(); return true }
            if (tab !== active && !inSplit(tab)) { result.cancel(); h.announce(app.getString(R.string.dialog_background, origin)); return true }
            val now = System.currentTimeMillis()
            while (dialogTimes.isNotEmpty() && now - dialogTimes.first() > 10_000) dialogTimes.removeFirst()
            dialogTimes.addLast(now)
            if (dialogTimes.size > 3) { dialogsMuted = true; result.cancel(); h.announce(app.getString(R.string.dialogs_stopped, origin)); return true }
            h.onPageDialog(tab, kind, origin, message ?: "", default) { ok, text ->
                if (!ok) result.cancel() else if (result is android.webkit.JsPromptResult) result.confirm(text ?: "") else result.confirm()
            }
            return true
        }

        fun reset() { dialogsMuted = false; dialogTimes.clear() }

        override fun onJsAlert(view: WebView, url: String?, message: String?, result: android.webkit.JsResult) = dialog(view, url, DIALOG_ALERT, message, null, result)
        override fun onJsConfirm(view: WebView, url: String?, message: String?, result: android.webkit.JsResult) = dialog(view, url, DIALOG_CONFIRM, message, null, result)
        override fun onJsPrompt(view: WebView, url: String?, message: String?, defaultValue: String?, result: android.webkit.JsPromptResult) = dialog(view, url, DIALOG_PROMPT, message, defaultValue, result)
        override fun onJsBeforeUnload(view: WebView, url: String?, message: String?, result: android.webkit.JsResult) = dialog(view, url, DIALOG_LEAVE, message, null, result)

        override fun onCloseWindow(window: WebView) { tabs.firstOrNull { it.view === window }?.let { close(it) } }
    }

    /** Debug builds only: a sign-in form on https://login.mica.test/ for trying autofill without a real site. */
    private fun testPage(path: String?): WebResourceResponse? {
        val name = when (path) { "/done" -> "done"; "/perm" -> "perm"; "/blob" -> "blob"; "/dialog" -> "dialog"; else -> "login" }
        return try { WebResourceResponse("text/html", "utf-8", app.assets.open("test/$name.html")) } catch (e: Exception) { null }
    }

    companion object {
        const val DIALOG_ALERT = 0
        const val DIALOG_CONFIRM = 1
        const val DIALOG_PROMPT = 2
        const val DIALOG_LEAVE = 3
        private const val HIGHLIGHT_ITEM = 0x6d1ca
        const val PRIVATE = "private"
        private const val PRIVATE_PROFILE = "mica-private-"
        private const val JAR_PROFILE = "mica-jar-"

        // Reports whether audible media plays in a frame. Muted autoplay (previews, ads) doesn't count.
        const val MEDIA_SCRIPT = """(()=>{if(window.__micaMedia||typeof micaMedia==='undefined')return;window.__micaMedia=1;let last='0';
const post=()=>{let on='0';try{for(const m of document.querySelectorAll('video,audio'))if(!m.paused&&!m.ended&&!m.muted&&m.volume>0&&m.readyState>2){on='1';break}}catch(e){}
if(on!==last){last=on;micaMedia.postMessage(on)}};
for(const t of ['play','playing','pause','ended','volumechange','emptied'])document.addEventListener(t,()=>setTimeout(post,0),true);
addEventListener('pagehide',()=>{if(last==='1'){last='0';micaMedia.postMessage('0')}});})()"""

        const val MEDIA_PAUSE = """(()=>{const l=[];for(const m of document.querySelectorAll('video,audio'))if(!m.paused){m.pause();l.push(m)}window.__micaHeld=l})()"""
        const val MEDIA_PLAY = """(()=>{let l=window.__micaHeld;if(!l||!l.length)l=[...document.querySelectorAll('video,audio')].filter(m=>m.currentTime>0&&!m.ended).slice(0,1);for(const m of l)m.play().catch(()=>{});window.__micaHeld=null})()"""

        // The page's own colour for the island and Spotlight glass: theme-color, else the body background.
        /** Desktop's TypedInputScript: the page holds text you typed and haven't sent. */
        const val TYPED_INPUT_SCRIPT = "(()=>{for(const e of document.querySelectorAll('input,textarea')){if(/^(hidden|checkbox|radio|submit|button|reset|image|file|range|color)$/.test(e.type))continue;if(e.value!==e.defaultValue)return true;}const a=document.activeElement;return !!(a&&a.isContentEditable);})()"

        const val TINT_SCRIPT = """(()=>{const m=document.querySelector('meta[name="theme-color"]');let c=m&&m.content;
if(!c||c==='transparent'){const b=document.body;c=b?getComputedStyle(b).backgroundColor:''}return c||''})()"""

        fun parseColor(value: String?): Int {
            val s = value?.trim('"', ' ')?.lowercase() ?: return 0
            fun rgb(r: Int, g: Int, b: Int) = (0xFF shl 24) or (r.coerceIn(0, 255) shl 16) or (g.coerceIn(0, 255) shl 8) or b.coerceIn(0, 255)
            try {
                if (s.startsWith("#")) {
                    val hex = if (s.length == 4) s.drop(1).map { "$it$it" }.joinToString("") else s.drop(1).take(6)
                    if (hex.length != 6) return 0
                    val v = hex.toInt(16)
                    return rgb(v shr 16 and 0xFF, v shr 8 and 0xFF, v and 0xFF)
                }
                val m = Regex("rgba?\\(([\\d.]+)[, ]+([\\d.]+)[, ]+([\\d.]+)(?:[, /]+([\\d.]+))?").find(s) ?: return 0
                val a = m.groupValues[4].toFloatOrNull() ?: 1f
                if (a < 0.5f) return 0
                return rgb(m.groupValues[1].toFloat().toInt(), m.groupValues[2].toFloat().toInt(), m.groupValues[3].toFloat().toInt())
            } catch (e: Exception) { return 0 }
        }
    }
}

/** The Activity behind a Context, if any. */
fun Context.findActivity(): android.app.Activity? {
    var c: Context? = this
    while (c is ContextWrapper) { if (c is android.app.Activity) return c; c = c.baseContext }
    return null
}
