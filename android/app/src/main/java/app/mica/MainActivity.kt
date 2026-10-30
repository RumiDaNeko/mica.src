package app.mica

import android.Manifest
import android.app.DownloadManager
import android.app.SearchManager
import android.content.ActivityNotFoundException
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Intent
import android.content.pm.PackageManager
import android.content.res.Configuration
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.text.InputType
import android.view.Gravity
import android.view.KeyEvent
import android.view.View
import android.view.inputmethod.EditorInfo
import android.webkit.CookieManager
import android.webkit.URLUtil
import android.webkit.ValueCallback
import android.webkit.WebChromeClient
import android.webkit.WebView
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.LinearLayout
import androidx.activity.OnBackPressedCallback
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.core.view.ViewCompat
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.drawerlayout.widget.DrawerLayout

class MainActivity : AppCompatActivity(), Browser.Host {
    val browser get() = (application as MicaApp).browser
    override val activity get() = this

    private lateinit var drawer: DrawerLayout
    private lateinit var content: FrameLayout
    override lateinit var webHost: FrameLayout
    private lateinit var home: View
    private lateinit var homePage: Home
    lateinit var island: Island
        private set
    private lateinit var spotlight: Spotlight
    private lateinit var deck: Deck
    private lateinit var library: Library
    private lateinit var peek: Peek
    private lateinit var sitePermissions: SitePermissions
    private lateinit var rail: Rail
    private lateinit var findBar: LinearLayout
    private lateinit var findField: EditText
    private lateinit var findCount: android.widget.TextView
    private lateinit var fullscreen: FrameLayout
    private var customView: View? = null
    private var customCallback: WebChromeClient.CustomViewCallback? = null
    private var fileCallback: ValueCallback<Array<Uri>>? = null
    private var pendingDownload: (() -> Unit)? = null
    private var scrollAcc = 0
    lateinit var bubble: Bubble
        private set
    private lateinit var autofill: Autofill
    private val account get() = (application as MicaApp).account
    private val accountChanged: () -> Unit = { showSignInCode() }
    private var shownSignIn: Any? = null
    private var signInTab: Tab? = null
    private var remindedAt: String? = null
    private val sentTabsTick = object : Runnable { override fun run() { account.checkSentTabs(force = false); island.postDelayed(this, 120_000) } }

    private val pickFiles = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        fileCallback?.onReceiveValue(WebChromeClient.FileChooserParams.parseResult(result.resultCode, result.data))
        fileCallback = null
    }
    private val askStorage = registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (granted) pendingDownload?.invoke()
        pendingDownload = null
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        WindowCompat.setDecorFitsSystemWindows(window, false)
        val night = isNight()
        WindowInsetsControllerCompat(window, window.decorView).apply { isAppearanceLightStatusBars = !night; isAppearanceLightNavigationBars = !night }
        Games.dark = night
        build()
        browser.attach(this)
        browser.themeGames()
        onActiveChanged()
        handleIntent(intent, savedInstanceState == null)
        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() { back() }
        })
        account.onTabsArrived = { tabsArrived(it) }
        account.onSignedIn = { u, e ->
            if (u != null) { bubble.hide(); announce(getString(R.string.signed_in_as, u.username)) }
            else if (e != null) bubble.show(getString(R.string.sign_in_failed), e.message, listOf(CardAction(getString(R.string.ok), true) {}))
        }
        if (savedInstanceState == null) account.pullSettings()
    }

    private fun isNight() = (resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK) == Configuration.UI_MODE_NIGHT_YES

    private fun build() {
        drawer = DrawerLayout(this)
        content = FrameLayout(this)
        content.setBackgroundColor(col(R.color.mica_canvas))
        webHost = FrameLayout(this)
        content.addView(webHost, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))

        // A new tab is ivory with Spotlight on top; with Spotlight closed, a quiet mark to tap.
        homePage = Home(this)
        home = homePage.view
        content.addView(home, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))

        island = Island(this, object : Island.Actions {
            override fun onTap() = spotlight.show(browser.active?.takeIf { !it.isHome }?.url?.let { Navigation.fromEngine(it) } ?: "")
            override fun onSpaceDot() = openRail()
            override fun onSwipe(dir: Int) = browser.step(dir)
            override fun onSwipeUp() = deck.show()
            override fun onLongPress() = pageMenu()
        })
        content.addView(island, FrameLayout.LayoutParams(FrameLayout.LayoutParams.WRAP_CONTENT, FrameLayout.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM or Gravity.CENTER_HORIZONTAL))

        buildFindBar()

        // The island opens into this card, so it sits where the island is.
        bubble = Bubble(this, island)
        content.addView(bubble.outside, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
        content.addView(bubble.view, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM).apply { setMargins(dp(10 - Bubble.SHADOW_ROOM), 0, dp(10 - Bubble.SHADOW_ROOM), 0) })
        bubble.held = { spotlight.showing || deck.showing || library.showing || peek.showing }
        autofill = Autofill(this, bubble)

        deck = Deck(this)
        content.addView(deck.view, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
        library = Library(this)
        content.addView(library.view, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
        peek = Peek(this)
        content.addView(peek.view, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
        sitePermissions = SitePermissions(this)
        spotlight = Spotlight(this)
        content.addView(spotlight.view, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
        fullscreen = FrameLayout(this).apply { setBackgroundColor(0xFF000000.toInt()); visibility = View.GONE }
        content.addView(fullscreen, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))

        drawer.addView(content, DrawerLayout.LayoutParams(DrawerLayout.LayoutParams.MATCH_PARENT, DrawerLayout.LayoutParams.MATCH_PARENT))
        rail = Rail(this)
        drawer.addView(rail.view, DrawerLayout.LayoutParams(dp(300).coerceAtMost((resources.displayMetrics.widthPixels * 0.85f).toInt()), DrawerLayout.LayoutParams.MATCH_PARENT, Gravity.START))
        drawer.setScrimColor(col(R.color.mica_scrim))
        // No edge swipe: the system Back gesture owns that edge. The space dot opens the rail; once open, it swipes shut.
        drawer.setDrawerLockMode(DrawerLayout.LOCK_MODE_LOCKED_CLOSED)
        drawer.addDrawerListener(object : DrawerLayout.SimpleDrawerListener() {
            override fun onDrawerOpened(drawerView: View) { drawer.setDrawerLockMode(DrawerLayout.LOCK_MODE_UNLOCKED); rail.refresh() }
            override fun onDrawerClosed(drawerView: View) = drawer.setDrawerLockMode(DrawerLayout.LOCK_MODE_LOCKED_CLOSED)
        })
        setContentView(drawer)

        ViewCompat.setOnApplyWindowInsetsListener(drawer) { _, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars() or WindowInsetsCompat.Type.displayCutout())
            val ime = insets.getInsets(WindowInsetsCompat.Type.ime())
            val bottom = maxOf(bars.bottom, ime.bottom)
            webHost.setPadding(bars.left, bars.top, bars.right, bars.bottom)
            (island.layoutParams as FrameLayout.LayoutParams).bottomMargin = bars.bottom + dp(14)
            island.requestLayout()
            (bubble.view.layoutParams as FrameLayout.LayoutParams).bottomMargin = bottom + dp(14 - Bubble.SHADOW_ROOM)
            bubble.view.requestLayout()
            (findBar.layoutParams as FrameLayout.LayoutParams).topMargin = bars.top + dp(8)
            spotlight.setInsets(bars.top, bottom)
            deck.setInsets(bars.top, bars.bottom)
            library.setInsets(bars.top, bottom)
            peek.setInsets(bars.top, bars.bottom)
            rail.setInsets(bars.top, bars.bottom)
            insets
        }
    }

    private fun buildFindBar() {
        findField = EditText(this).apply {
            hint = getString(R.string.find_in_page); setSingleLine(); textSize = 15f
            inputType = InputType.TYPE_CLASS_TEXT
            imeOptions = EditorInfo.IME_ACTION_SEARCH
            background = null
            setTextColor(col(R.color.mica_ink)); setHintTextColor(col(R.color.mica_faint))
            addTextChangedListener(object : android.text.TextWatcher {
                override fun beforeTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
                override fun onTextChanged(s: CharSequence?, a: Int, b: Int, c: Int) {}
                override fun afterTextChanged(s: android.text.Editable?) { browser.active?.view?.findAllAsync(s.toString()) }
            })
            setOnEditorActionListener { _, _, _ -> browser.active?.view?.findNext(true); true }
        }
        findCount = text("", 13f, col(R.color.mica_muted))
        fun btn(g: String, desc: Int, run: () -> Unit) = text(g, 18f).apply { pad(10, 6); contentDescription = getString(desc); background = pressable(0, 12); setOnClickListener { run() } }
        findBar = row(findField, findCount,
            btn("↑", R.string.previous) { browser.active?.view?.findNext(false) },
            btn("↓", R.string.next) { browser.active?.view?.findNext(true) },
            btn("✕", R.string.close) { hideFind() })
        findField.layoutParams = lp(0, WRAP, 1f)
        findBar.pad(14, 4)
        findBar.background = rounded(col(R.color.mica_panel), 22, col(R.color.mica_line))
        findBar.elevation = dp(8).toFloat()
        findBar.visibility = View.GONE
        content.addView(findBar, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.WRAP_CONTENT, Gravity.TOP).apply { setMargins(dp(10), dp(8), dp(10), 0) })
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleIntent(intent, true)
    }

    private fun handleIntent(intent: Intent?, fresh: Boolean) {
        if (!fresh || intent == null) return
        if (intent.action == ACTION_SIGN_IN) { intent.action = null; shownSignIn = null; showSignInCode(); return }
        val url = when (intent.action) {
            Intent.ACTION_VIEW -> intent.dataString?.takeIf { Navigation.isWeb(it) }
            Intent.ACTION_WEB_SEARCH -> intent.getStringExtra(SearchManager.QUERY)?.let { Navigation.resolve(it) }
            else -> null
        } ?: run { if (browser.active?.isHome == true && intent.action == Intent.ACTION_MAIN) home.post { if (browser.active?.isHome == true) spotlight.show() }; return }
        intent.action = null
        val active = browser.active
        if (active != null && active.isHome) browser.navigate(active, url) else browser.open(url)
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus && spotlight.showing && currentFocus == null) spotlight.focusField()
    }

    override fun onStart() {
        super.onStart()
        browser.onForeground()
        peek.resume()
        account.listen(accountChanged)
        account.onForeground()
        island.removeCallbacks(sentTabsTick)
        island.postDelayed(sentTabsTick, 120_000)
        showSignInCode()
    }

    // Leaving Mica during full-screen video (or with the playing tab in front) continues it in a small window.
    override fun onUserLeaveHint() {
        super.onUserLeaveHint()
        if (customView != null || (browser.active?.playing == true && browser.active === browser.mediaTab && isVideoFullscreenLike())) enterPip()
    }

    private fun isVideoFullscreenLike() = customView != null

    fun enterPip() {
        if (!packageManager.hasSystemFeature(PackageManager.FEATURE_PICTURE_IN_PICTURE)) return
        try {
            enterPictureInPictureMode(android.app.PictureInPictureParams.Builder().setAspectRatio(android.util.Rational(16, 9)).build())
        } catch (e: IllegalStateException) { }
    }

    override fun onPictureInPictureModeChanged(isInPictureInPictureMode: Boolean, newConfig: Configuration) {
        super.onPictureInPictureModeChanged(isInPictureInPictureMode, newConfig)
        val chrome = if (isInPictureInPictureMode) View.GONE else View.VISIBLE
        if (isInPictureInPictureMode) { spotlight.cut(); deck.cut(); library.cut(); peek.cut(); bubble.hide(); if (findBar.visibility == View.VISIBLE) hideFind() }
        island.visibility = chrome
    }

    private var askedNotifications = false
    private val askNotifications = registerForActivityResult(ActivityResultContracts.RequestPermission()) { }
    private var androidAnswer: ((Map<String, Boolean>) -> Unit)? = null
    private val askAndroid = registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { r -> androidAnswer?.let { androidAnswer = null; it(r) } }

    /** Android's own permission dialog, for what a site was just allowed (camera, microphone, location). */
    fun requestAndroidPermissions(names: Array<String>, done: (Map<String, Boolean>) -> Unit) {
        androidAnswer?.invoke(emptyMap())
        androidAnswer = done
        askAndroid.launch(names)
    }

    /** The media notification needs this on Android 13+; asked once, the first time something plays. */
    private fun askNotificationsOnce() {
        if (askedNotifications || Build.VERSION.SDK_INT < 33) return
        askedNotifications = true
        val prefs = getSharedPreferences("mica", MODE_PRIVATE)
        if (prefs.getBoolean("askedNotifications", false)) return
        if (ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED) return
        prefs.edit().putBoolean("askedNotifications", true).apply()
        askNotifications.launch(Manifest.permission.POST_NOTIFICATIONS)
    }
    override fun onPause() {
        super.onPause()
        browser.onLeaving()
    }

    override fun onStop() {
        super.onStop()
        browser.onBackground()
        peek.pause()
        account.unlisten(accountChanged)
        island.removeCallbacks(sentTabsTick)
    }

    override fun onDestroy() {
        if (customView != null) hideCustomView()
        browser.detach(this)
        super.onDestroy()
    }

    // ---- Back ----

    private fun back() {
        val tab = browser.active
        when {
            customView != null -> hideCustomView()
            drawer.isDrawerOpen(Gravity.START) -> drawer.closeDrawer(Gravity.START)
            spotlight.showing -> spotlight.hide()
            bubble.showing -> bubble.hide()
            deck.showing -> deck.hide()
            library.showing -> library.hide()
            peek.showing -> peek.back()
            findBar.visibility == View.VISIBLE -> hideFind()
            tab?.view?.canGoBack() == true -> tab.view?.goBack()
            tab != null && tab.parent != null && browser.tab(tab.parent) != null -> browser.close(tab)
            else -> moveTaskToBack(true)
        }
    }

    // ---- Browser.Host ----

    override fun onActiveChanged() {
        val tab = browser.active
        home.visibility = if (tab == null || tab.isHome) View.VISIBLE else View.GONE
        // Private: no screenshots or app-switcher preview, and the new-tab page says where you are.
        val private = tab?.isPrivate == true
        if (private) window.addFlags(android.view.WindowManager.LayoutParams.FLAG_SECURE) else window.clearFlags(android.view.WindowManager.LayoutParams.FLAG_SECURE)
        if (home.visibility == View.VISIBLE) { homePage.render(private); homePage.onShown() }
        if (findBar.visibility == View.VISIBLE) hideFind()
        island.setShrunk(false)
        onTabsChanged()
        // Switching back to the sign-in tab brings the code back.
        if (tab != null && tab === signInTab) { remindedAt = null; remindSignInCode(tab) }
    }

    override fun onTabsChanged() {
        updateIsland()
        if (browser.mediaTab != null) askNotificationsOnce()
        if (drawer.isDrawerOpen(Gravity.START)) rail.refresh()
        deck.refresh()
    }

    override fun onProgress(tab: Tab) { if (tab === browser.active) island.progress = tab.progress }

    override fun onPageChanged(tab: Tab) {
        if (tab !== browser.active) return
        val was = home.visibility
        home.visibility = if (tab.isHome) View.VISIBLE else View.GONE
        if (was != View.VISIBLE && tab.isHome) { homePage.render(tab.isPrivate); homePage.onShown() }
        updateIsland()
        remindSignInCode(tab)
    }

    private fun updateIsland() {
        val tab = browser.active
        val tabs = browser.tabsIn()
        island.label = when {
            tab == null || tab.isHome -> getString(R.string.new_tab)
            tab.url.isEmpty() -> "…"
            else -> Navigation.label(tab.url)
        }
        island.spaceColor = browser.activeSpace.color
        island.tabCount = tabs.size
        island.activeIndex = tabs.indexOf(tab).coerceAtLeast(0)
        island.playing = browser.tabs.any { it.playing }
        island.progress = tab?.progress ?: 100
        island.tint = if (tab?.isPrivate == true) PRIVATE_TINT else tab?.tint ?: 0
    }

    override fun onScrolled(dy: Int, atTop: Boolean) {
        if (atTop) { scrollAcc = 0; island.setShrunk(false); return }
        scrollAcc = if ((scrollAcc > 0) == (dy > 0)) scrollAcc + dy else dy
        if (scrollAcc > dp(24)) island.setShrunk(true) else if (scrollAcc < -dp(24)) island.setShrunk(false)
    }

    override fun announce(text: String) = island.announce(text)

    override fun showCustomView(view: View, callback: WebChromeClient.CustomViewCallback) {
        if (customView != null) { callback.onCustomViewHidden(); return }
        customView = view; customCallback = callback
        fullscreen.addView(view, FrameLayout.LayoutParams(FrameLayout.LayoutParams.MATCH_PARENT, FrameLayout.LayoutParams.MATCH_PARENT))
        fullscreen.visibility = View.VISIBLE
        WindowInsetsControllerCompat(window, window.decorView).apply {
            hide(WindowInsetsCompat.Type.systemBars())
            systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        }
    }

    override fun hideCustomView() {
        val v = customView ?: return
        fullscreen.removeView(v)
        fullscreen.visibility = View.GONE
        customView = null
        WindowInsetsControllerCompat(window, window.decorView).show(WindowInsetsCompat.Type.systemBars())
        customCallback?.onCustomViewHidden()
        customCallback = null
    }

    override fun chooseFiles(callback: ValueCallback<Array<Uri>>, params: WebChromeClient.FileChooserParams): Boolean {
        fileCallback?.onReceiveValue(null)
        fileCallback = callback
        return try {
            pickFiles.launch(Intent.createChooser(params.createIntent(), getString(R.string.choose_file)))
            true
        } catch (e: ActivityNotFoundException) {
            fileCallback = null
            false
        }
    }

    override fun download(url: String, userAgent: String, contentDisposition: String?, mime: String?) {
        if (!Navigation.isWeb(url)) { island.announce(getString(R.string.no_app_for_link)); return }
        val name = URLUtil.guessFileName(url, contentDisposition, mime)
        val run = {
            val req = DownloadManager.Request(Uri.parse(url))
                .setMimeType(mime)
                .addRequestHeader("User-Agent", userAgent)
                .setTitle(name)
                .setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED)
                .setDestinationInExternalPublicDir(Environment.DIRECTORY_DOWNLOADS, name)
            CookieManager.getInstance().getCookie(url)?.let { req.addRequestHeader("Cookie", it) }
            browser.active?.url?.takeIf { Navigation.isWeb(it) }?.let { req.addRequestHeader("Referer", it) }
            (getSystemService(DOWNLOAD_SERVICE) as DownloadManager).enqueue(req)
            island.announce(getString(R.string.downloading_file, name))
        }
        if (Build.VERSION.SDK_INT < 29 && ContextCompat.checkSelfPermission(this, Manifest.permission.WRITE_EXTERNAL_STORAGE) != PackageManager.PERMISSION_GRANTED) {
            pendingDownload = run
            askStorage.launch(Manifest.permission.WRITE_EXTERNAL_STORAGE)
        } else run()
    }

    override fun openExternal(url: String, fallback: (String) -> Unit): Boolean {
        try {
            val intent = if (url.startsWith("intent:", true)) {
                Intent.parseUri(url, Intent.URI_INTENT_SCHEME).apply {
                    // Only what a link may open: no explicit components, no selectors.
                    addCategory(Intent.CATEGORY_BROWSABLE); component = null; selector = null
                }
            } else Intent(Intent.ACTION_VIEW, Uri.parse(url)).addCategory(Intent.CATEGORY_BROWSABLE)
            try {
                startActivity(intent)
            } catch (e: ActivityNotFoundException) {
                val alt = intent.getStringExtra("browser_fallback_url")
                if (alt != null && Navigation.isWeb(alt)) fallback(alt) else island.announce(getString(R.string.no_app_for_link))
            }
        } catch (e: Exception) {
            island.announce(getString(R.string.no_app_for_link))
        }
        return true
    }

    override fun onLinkLongPress(tab: Tab, hit: WebView.HitTestResult): Boolean {
        val type = hit.type
        val extra = hit.extra ?: return false
        if (type != WebView.HitTestResult.SRC_ANCHOR_TYPE && type != WebView.HitTestResult.SRC_IMAGE_ANCHOR_TYPE && type != WebView.HitTestResult.IMAGE_TYPE) return false
        if (type == WebView.HitTestResult.SRC_IMAGE_ANCHOR_TYPE) {
            // The link behind an image comes from the focus node.
            val msg = android.os.Handler(mainLooper) { m -> linkMenu(tab, m.data.getString("url") ?: extra, extra); true }.obtainMessage()
            tab.view?.requestFocusNodeHref(msg)
            return true
        }
        if (type == WebView.HitTestResult.IMAGE_TYPE) linkMenu(tab, null, extra) else linkMenu(tab, extra, null)
        return true
    }

    private fun linkMenu(tab: Tab, link: String?, image: String?) {
        val labels = ArrayList<String>(); val runs = ArrayList<() -> Unit>()
        fun add(label: Int, run: () -> Unit) { labels.add(getString(label)); runs.add(run) }
        if (link != null && Navigation.isWeb(link)) {
            add(R.string.peek) { peek.show(tab, link) }
            add(R.string.open_in_new_tab) { browser.open(link, parent = tab) }
            if (Navigation.isWeb(tab.url) && !browser.inSplit(tab)) add(R.string.open_in_split) { browser.startSplit(tab, browser.open(link, activate = false, parent = tab)) }
            add(R.string.open_in_background) { browser.open(link, activate = false, parent = tab); island.announce(Navigation.label(link)) }
            add(R.string.copy_link) {
                (getSystemService(CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(ClipData.newPlainText(link, link))
                if (Build.VERSION.SDK_INT < 33) island.announce(getString(R.string.link_copied))
            }
            add(R.string.share_link) { share(link) }
        }
        if (image != null && Navigation.isWeb(image)) add(R.string.download_image) { download(image, tab.view?.settings?.userAgentString ?: "", null, null) }
        if (labels.isEmpty()) return
        bubble.show(Navigation.label(link ?: image!!), link ?: image, emptyList(), 0, choices(labels.map { it to null }) { i -> bubble.hide(); runs[i]() })
    }

    // ---- Actions used by the island, Spotlight, deck and rail ----

    fun newTab() {
        val tab = browser.active
        if (tab != null && tab.isHome) { spotlight.show(); return }
        browser.open(Navigation.HOME)
        spotlight.show()
    }

    fun navigateActive(url: String) {
        val tab = browser.active ?: browser.open(Navigation.HOME)
        browser.navigate(tab, url)
    }

    fun openHit(hit: Navigation.Hit) {
        val tab = browser.tab(hit.tabId)
        if (hit.kind == Navigation.Kind.TAB && tab != null) {
            // Leaving an untouched new tab behind isn't useful.
            browser.active?.takeIf { it.isHome && it !== tab }?.let { browser.close(it) }
            browser.activate(tab)
        } else navigateActive(hit.url)
    }

    /** Spotlight or the deck shrank back into the island: a card that waited can open now. */
    fun onPanelClosed() = bubble.release()

    /** Something has grown out of the island and covers it, so the pill stays hidden. */
    fun islandBusy(): Boolean = bubble.showing || spotlight.showing || deck.showing || library.showing || peek.showing

    fun onSpotlightHidden() {
        home.visibility = if (browser.active?.isHome != false) View.VISIBLE else View.GONE
    }

    fun openRail() { rail.refresh(); drawer.openDrawer(Gravity.START) }
    fun closeRail() = drawer.closeDrawer(Gravity.START)

    override val pageCovered get() = islandBusy() || drawer.isDrawerOpen(Gravity.START)

    fun sharePage() { browser.active?.takeIf { Navigation.isWeb(it.url) }?.let { share(it.url) } }

    private fun share(url: String) {
        startActivity(Intent.createChooser(Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT, url), null))
    }

    fun showFind() {
        val v = browser.active?.view ?: return
        findBar.visibility = View.VISIBLE
        v.setFindListener { active, count, done -> if (done) findCount.text = if (count == 0) "0" else "${active + 1}/$count" }
        findField.setText("")
        findField.requestFocus()
        findField.post { (getSystemService(INPUT_METHOD_SERVICE) as android.view.inputmethod.InputMethodManager).showSoftInput(findField, 0) }
    }

    private fun hideFind() {
        findBar.visibility = View.GONE
        findCount.text = ""
        browser.active?.view?.clearMatches()
        (getSystemService(INPUT_METHOD_SERVICE) as android.view.inputmethod.InputMethodManager).hideSoftInputFromWindow(findField.windowToken, 0)
    }

    fun openDownloads() {
        try { startActivity(Intent(DownloadManager.ACTION_VIEW_DOWNLOADS)) } catch (e: ActivityNotFoundException) { island.announce(getString(R.string.no_app_for_link)) }
    }

    fun openSettings() = startActivity(Intent(this, SettingsActivity::class.java))

    fun openSpotlight() = spotlight.show()

    // ---- Saved sessions ----

    fun sessionsMenu() {
        closeRail()
        val saved = Sessions.list(this)
        val fmt = java.text.SimpleDateFormat("d MMM HH:mm", java.util.Locale.getDefault())
        val rows = listOf(getString(R.string.save_session) to null) + saved.map { s ->
            s.name to resources.getQuantityString(R.plurals.group_tabs, s.tabs.size, s.tabs.size) + " · " + fmt.format(java.util.Date(s.saved))
        }
        var sheet: Sheet? = null
        sheet = Sheet.show(this, getString(R.string.sessions), getString(if (saved.isEmpty()) R.string.sessions_empty else R.string.sessions_hint),
            choices(rows) { i -> sheet?.dismiss(); if (i == 0) saveSession() else sessionMenu(saved[i - 1]) })
    }

    private fun sessionMenu(s: Sessions.Saved) {
        var sheet: Sheet? = null
        sheet = Sheet.show(this, s.name, resources.getQuantityString(R.plurals.group_tabs, s.tabs.size, s.tabs.size),
            choices(listOf(getString(R.string.restore) to null, getString(R.string.delete) to null)) { i ->
                sheet?.dismiss()
                if (i == 0) { browser.restore(s); island.announce(getString(R.string.session_restored, s.name)); deck.show() }
                else { Sessions.delete(s); sessionsMenu() }
            })
    }

    private fun saveSession() {
        val tabs = browser.sessionTabs()
        if (tabs.isEmpty()) { island.announce(getString(R.string.no_web_tabs)); return }
        val name = field(getString(R.string.session_name), getString(R.string.session_default, java.text.SimpleDateFormat("d MMM HH:mm", java.util.Locale.getDefault()).format(java.util.Date())))
        name.filters = arrayOf(android.text.InputFilter.LengthFilter(60))
        name.post { name.requestFocus(); name.selectAll(); (getSystemService(INPUT_METHOD_SERVICE) as android.view.inputmethod.InputMethodManager).showSoftInput(name, 0) }
        Sheet.show(this, getString(R.string.save_session), getString(R.string.save_session_detail), form(name), listOf(
            CardAction(getString(R.string.cancel)) {},
            CardAction(getString(R.string.save), true) {
                val n = name.text.toString().trim()
                if (n.isNotEmpty()) island.announce(getString(if (Sessions.save(this, n, tabs)) R.string.session_saved else R.string.session_save_failed, n))
            }))
    }

    // ---- Private tabs and tab groups ----

    fun newPrivateTab() {
        closeRail()
        if (browser.openPrivate() == null) island.announce(getString(R.string.private_unsupported)) else spotlight.show()
    }

    /** A tab's menu (held in the deck or the rail): pin, group, move, close. */
    fun tabMenu(tab: Tab) {
        val items = ArrayList<Pair<String, () -> Unit>>()
        if (!tab.isPrivate) items.add(getString(if (tab.pinned) R.string.unpin_tab else R.string.pin_tab) to { browser.setPinned(tab, !tab.pinned); onTabsChanged() })
        items.add(getString(R.string.add_to_group) to { pickGroup(tab) })
        if (tab.group != null) items.add(getString(R.string.remove_from_group) to { browser.leaveGroup(tab) })
        if (!tab.isPrivate) browser.spaces.filter { it.id != tab.space && it.id != Browser.PRIVATE }.forEach { s ->
            items.add(getString(R.string.move_to, browser.spaceName(s)) to { browser.moveTo(tab, s.id); onTabsChanged() })
        }
        val cur = browser.active
        if (cur != null && cur !== tab && cur.space == tab.space && Navigation.isWeb(cur.url) && Navigation.isWeb(tab.url) && !browser.inSplit(tab))
            items.add(getString(R.string.split_with_current) to { browser.startSplit(cur, tab) })
        if (browser.inSplit(tab)) items.add(getString(R.string.end_split) to { browser.endSplit() })
        if (!tab.isPrivate && browser.privateSupported) items.add(getString(R.string.cookie_jar) + (browser.jar(tab.jar)?.let { " · " + it.name } ?: "") to { jarMenu(tab) })
        items.add(getString(R.string.close_tab) to { browser.close(tab) })
        var sheet: Sheet? = null
        sheet = Sheet.show(this, tab.title.ifBlank { if (tab.isHome) getString(R.string.new_tab) else Navigation.label(tab.url) }, null,
            choices(items.map { it.first to null }) { i -> sheet?.dismiss(); items[i].second() })
    }

    private fun pickGroup(tab: Tab) {
        val groups = browser.groupsIn(tab.space).filter { it.id != tab.group }
        if (groups.isEmpty()) return editGroup(null, tab)
        var sheet: Sheet? = null
        sheet = Sheet.show(this, getString(R.string.add_to_group), null,
            choices(groups.map { it.name.ifBlank { getString(R.string.tab_group) } to resources.getQuantityString(R.plurals.group_tabs, browser.members(it).size, browser.members(it).size) } + (getString(R.string.new_group) to null)) { i ->
                sheet?.dismiss()
                if (i < groups.size) browser.addToGroup(tab, groups[i]) else editGroup(null, tab)
            })
    }

    /** A new group for [tab] (group null), or a group's name and colour. */
    private fun editGroup(group: TabGroup?, tab: Tab?) {
        val name = field(getString(R.string.group_name), group?.name ?: "")
        var color = group?.color ?: PALETTE[browser.groupsIn().size % PALETTE.size]
        val swatches = row()
        fun paint() { for (i in 0 until swatches.childCount) swatches.getChildAt(i).background = rounded(PALETTE[i], 15, if (PALETTE[i] == color) col(R.color.mica_ink) else 0, 2) }
        PALETTE.forEach { c -> swatches.addView(View(this).apply { setOnClickListener { color = c; paint() } }, lp(dp(30), dp(30)).apply { marginEnd = dp(10) }) }
        paint()
        if (group == null) name.post { name.requestFocus(); (getSystemService(INPUT_METHOD_SERVICE) as android.view.inputmethod.InputMethodManager).showSoftInput(name, 0) }
        Sheet.show(this, getString(if (group == null) R.string.new_group else R.string.edit_group), null, form(name, swatches), listOf(
            CardAction(getString(R.string.cancel)) {},
            CardAction(getString(R.string.save), true) {
                val n = name.text.toString().trim()
                if (group == null) tab?.let { browser.newGroup(it, n, color) }
                else { group.name = n; group.color = color; browser.saveLater() }
                onTabsChanged()
            }))
    }

    /** Which cookie jar [tab] lives in (desktop's tab menu "Cookie jar"). */
    private fun jarMenu(tab: Tab) {
        val jars = browser.jars.toList()
        val rows = listOf((if (tab.jar == null) "✓  " else "     ") + getString(R.string.jar_none) to null) +
            jars.map { j -> (if (tab.jar == j.id) "✓  " else "     ") + j.name to "      " + resources.getQuantityString(R.plurals.group_tabs, browser.tabs.count { it.jar == j.id }, browser.tabs.count { it.jar == j.id }) } +
            listOf("     " + getString(R.string.new_jar) to null, "     " + getString(R.string.manage_jars) to null)
        var sheet: Sheet? = null
        sheet = Sheet.show(this, getString(R.string.cookie_jars), getString(R.string.cookie_jars_detail), choices(rows) { i ->
            sheet?.dismiss()
            when {
                i == 0 -> { browser.moveToJar(tab, null); island.announce(getString(R.string.jar_left)) }
                i <= jars.size -> { val j = jars[i - 1]; browser.moveToJar(tab, j); island.announce(getString(R.string.jar_moved, j.name)) }
                i == jars.size + 1 -> editJar(null) { j -> browser.moveToJar(tab, j); island.announce(getString(R.string.jar_moved, j.name)) }
                else -> manageJars()
            }
        })
    }

    private fun editJar(jar: Jar?, made: (Jar) -> Unit = {}) {
        val name = field(getString(R.string.jar_name), jar?.name ?: "")
        name.filters = arrayOf(android.text.InputFilter.LengthFilter(24))
        name.post { name.requestFocus(); (getSystemService(INPUT_METHOD_SERVICE) as android.view.inputmethod.InputMethodManager).showSoftInput(name, 0) }
        Sheet.show(this, getString(if (jar == null) R.string.new_jar else R.string.rename).removeSuffix("…"), null, form(name), listOf(
            CardAction(getString(R.string.cancel)) {},
            CardAction(getString(R.string.save), true) {
                val n = name.text.toString().trim()
                if (n.isEmpty()) return@CardAction
                if (jar == null) made(browser.newJar(n)) else { jar.name = n; browser.saveLater() }
            }))
    }

    private fun manageJars() {
        val jars = browser.jars.toList()
        var sheet: Sheet? = null
        sheet = Sheet.show(this, getString(R.string.cookie_jars), getString(R.string.cookie_jars_detail), choices(jars.map { j ->
            j.name to resources.getQuantityString(R.plurals.group_tabs, browser.tabs.count { it.jar == j.id }, browser.tabs.count { it.jar == j.id })
        }) { i ->
            sheet?.dismiss()
            val j = jars[i]
            var inner: Sheet? = null
            inner = Sheet.show(this, j.name, null, choices(listOf(getString(R.string.rename) to null, getString(R.string.delete_jar) to getString(R.string.delete_jar_detail, j.name))) { k ->
                inner?.dismiss()
                if (k == 0) editJar(j) else Sheet.show(this, getString(R.string.delete_jar) + "?", getString(R.string.delete_jar_detail, j.name), null, listOf(
                    CardAction(getString(R.string.cancel)) {},
                    CardAction(getString(R.string.delete), true) { browser.deleteJar(j); island.announce(getString(R.string.jar_deleted, j.name)) }))
            })
        })
    }

    /** A group's menu (held on its header in the deck). */
    fun groupMenu(g: TabGroup) {
        val items = listOf<Pair<String, () -> Unit>>(
            getString(R.string.edit_group) to { editGroup(g, null) },
            getString(R.string.ungroup) to { browser.ungroup(g) },
            getString(R.string.close_group) to { browser.closeGroup(g) })
        var sheet: Sheet? = null
        sheet = Sheet.show(this, g.name.ifBlank { getString(R.string.tab_group) }, resources.getQuantityString(R.plurals.group_tabs, browser.members(g).size, browser.members(g).size),
            choices(items.map { it.first to null }) { i -> sheet?.dismiss(); items[i].second() })
    }
    fun openLibrary(at: Library.Section? = null) { closeRail(); if (at != null) library.show(at) else library.show() }

    /** From the library: in this tab if it's a new tab, else in a new one next to it. */
    fun openFromLibrary(url: String) {
        val tab = browser.active
        if (tab == null || tab.isHome) navigateActive(url) else browser.open(url)
    }

    /** Adds or removes the page in bookmarks or read later, and says which. */
    fun toggleSaved(readLater: Boolean) {
        val tab = browser.active?.takeIf { Navigation.isWeb(it.url) } ?: return
        val now = browser.toggle(if (readLater) browser.readLater else browser.bookmarks, tab)
        if (readLater && !now) Reader.forget(this, Navigation.fromEngine(tab.url))
        if (readLater && now) {
            // An article is kept whole, to read without a connection; another page is kept as its address.
            browser.readArticle(tab) { article ->
                val kept = article != null && try { Reader.keep(this, article, saved = true); true } catch (e: Exception) { false }
                island.announce(getString(if (kept) R.string.saved_offline else R.string.saved_for_later))
            }
            return
        }
        island.announce(getString(when {
            readLater && now -> R.string.saved_for_later
            readLater -> R.string.removed_from_read_later
            now -> R.string.bookmarked
            else -> R.string.bookmark_removed
        }))
    }
    fun openPasswords() = startActivity(Intent(this, PasswordsActivity::class.java))

    // ---- Account ----

    override fun onLoginMessage(tab: Tab, origin: String, json: String) = autofill.onMessage(tab, origin, json)
    override fun onSitePermission(tab: Tab, request: android.webkit.PermissionRequest) = sitePermissions.request(tab, request)
    override fun onSitePermissionCanceled(request: android.webkit.PermissionRequest) = sitePermissions.canceled(request)
    override fun onGeolocation(tab: Tab, origin: String, callback: android.webkit.GeolocationPermissions.Callback) = sitePermissions.geolocation(tab, origin, callback)

    private var popupShownAt = 0L

    /** A window the page tried to open without a tap: one card at most every 3 s. */
    override fun onPopupBlocked(tab: Tab, url: String) {
        val now = System.currentTimeMillis()
        if (now - popupShownAt < 3000 || bubble.showing) return
        popupShownAt = now
        bubble.show(getString(R.string.popup_blocked), getString(R.string.popup_blocked_detail, Navigation.label(url)), listOf(
            CardAction(getString(R.string.keep_blocking)) {},
            CardAction(getString(R.string.open_it), true) { browser.open(url, parent = tab) }), 10)
    }

    /** alert, confirm, prompt and "leave this page?", grown out of the island instead of a system dialog. */
    override fun onPageDialog(tab: Tab, kind: Int, origin: String, message: String, default: String?, answer: (Boolean, String?) -> Unit) {
        var answered = false
        fun reply(ok: Boolean, text: String?) { if (!answered) { answered = true; answer(ok, text) } }
        val leave = kind == Browser.DIALOG_LEAVE
        val title = if (leave) getString(R.string.leave_page) else getString(R.string.page_says, origin.ifEmpty { "Page" })
        val detail = if (leave) getString(R.string.leave_page_detail) else null
        val body = column()
        if (!leave && message.isNotBlank()) {
            val scroll = CappedScroll(this).apply { addView(text(message.take(4000), 15f)) }
            body.addView(scroll, lp(MATCH, WRAP))
        }
        val input = if (kind == Browser.DIALOG_PROMPT) field("", default ?: "").also { f ->
            body.addView(f, lp(MATCH, WRAP).apply { topMargin = dp(10) })
            f.post { f.requestFocus(); f.selectAll(); (getSystemService(INPUT_METHOD_SERVICE) as android.view.inputmethod.InputMethodManager).showSoftInput(f, 0) }
        } else null
        val actions = when (kind) {
            Browser.DIALOG_ALERT -> listOf(CardAction(getString(R.string.ok), true) { reply(true, null) })
            Browser.DIALOG_LEAVE -> listOf(CardAction(getString(R.string.stay)) { reply(false, null) }, CardAction(getString(R.string.leave), true) { reply(true, null) })
            else -> listOf(CardAction(getString(R.string.cancel)) { reply(false, null) }, CardAction(getString(R.string.ok), true) { reply(true, input?.text?.toString()) })
        }
        // An alert closed any other way still counts as read, as on desktop; the others count as Cancel.
        bubble.show(title, detail, actions, 0, if (body.childCount > 0) body else null, stays = true) { reply(kind == Browser.DIALOG_ALERT, null) }
    }

    /** A site's own sign-in box (HTTP authentication): typed by you, never filled in by Mica. */
    override fun onHttpAuth(tab: Tab, host: String, handler: android.webkit.HttpAuthHandler) {
        val user = field(getString(R.string.username))
        val (passBox, pass) = passwordField(getString(R.string.password))
        val secure = tab.url.startsWith("https://", true)
        user.post { user.requestFocus(); (getSystemService(INPUT_METHOD_SERVICE) as android.view.inputmethod.InputMethodManager).showSoftInput(user, 0) }
        Sheet.show(this, getString(R.string.http_auth_title), getString(if (secure) R.string.http_auth_detail else R.string.http_auth_insecure, host), form(user, passBox), listOf(
            CardAction(getString(R.string.cancel)) { handler.cancel() },
            CardAction(getString(R.string.http_auth_title), true) { handler.proceed(user.text.toString(), pass.text.toString()) }),
            unanswered = { handler.cancel() })
    }

    // ---- Reading mode ----

    /** The page as an article, in the same tab (Back returns to the page). */
    fun openReader(tab: Tab) {
        browser.readArticle(tab) { article ->
            if (article == null) return@readArticle island.announce(getString(R.string.not_an_article))
            val address = try { Reader.keep(this, article, saved = false) } catch (e: Exception) { return@readArticle island.announce(getString(R.string.not_an_article)) }
            if (browser.tab(tab.id) != null) browser.navigate(tab, address)
        }
    }

    /** The page this reader tab came from: back if it's the page before, else load it. */
    private fun openOriginal(tab: Tab) {
        val url = browser.pageKey(tab)
        val v = tab.view
        val list = v?.copyBackForwardList()
        val before = list?.let { if (it.currentIndex > 0) it.getItemAtIndex(it.currentIndex - 1)?.url else null }
        if (v != null && before == url) v.goBack() else browser.navigate(tab, url)
    }

    override fun onReaderMessage(tab: Tab, message: String) {
        val prefs = (application as MicaApp).prefs
        when {
            message.startsWith("size:") -> message.removePrefix("size:").toIntOrNull()?.let { prefs.readerSize = it }
            message.startsWith("serif:") -> { prefs.readerSerif = message.endsWith("true"); tab.view?.reload() }
            message == "original" -> openOriginal(tab)
            message == "save" -> {
                val url = browser.pageKey(tab)
                val article = Reader.load(this, Reader.id(tab.url)) ?: return
                if (Reader.isSaved(this, url)) { island.announce(getString(R.string.reader_already_saved)); return }
                try { Reader.keep(this, article, saved = true) } catch (e: Exception) { return }
                if (!browser.isSavedForLater(url)) browser.readLater.add(0, HistoryEntry(url, article.optString("title").ifBlank { tab.title }, System.currentTimeMillis()))
                browser.saveLater()
                island.announce(getString(R.string.saved_offline))
                tab.view?.reload()
            }
        }
    }

    /** A read-later entry: its saved copy (offline) when there is one, else the page. */
    fun openReadLater(url: String) = openFromLibrary(Reader.savedAddress(this, url) ?: url)

    fun removeHighlight(item: Highlights.Item) = browser.removeHighlight(item)

    fun webHostPostDelayed(ms: Long, run: () -> Unit) { webHost.postDelayed(run, ms) }

    /** A sign-in waiting for Discord: its page opens in a tab, and the code stays in a card to type or paste there. */
    private fun showSignInCode() {
        val s = account.signIn ?: return
        if (shownSignIn === s) return
        shownSignIn = s
        signInTab = browser.open(s.url)
        remindedAt = null
        signInCard()
    }

    /**
     * Forgot the code? Once the sign-in tab reaches the service's "type the code" page (or you switch back to
     * it there), the card shows again. Once per page, so closing it sticks.
     */
    private fun remindSignInCode(tab: Tab) {
        if (account.signIn == null || tab !== signInTab || bubble.showing || tab.progress in 1..99) return
        if (tab.pageHost != java.net.URI(app.mica.account.Api.BASE).host || remindedAt == tab.url) return
        remindedAt = tab.url
        signInCard()
    }

    /** The code card. It comes back whenever another card (save password…) closes, until sign-in ends. */
    private fun signInCard() {
        val s = account.signIn ?: return
        bubble.show(getString(R.string.sign_in_code_title, s.code), getString(R.string.sign_in_code_detail), listOf(
            CardAction(getString(R.string.cancel)) { account.cancelSignIn() },
            // Copies and closes; the sign-in tab shows it again if it's needed.
            CardAction(getString(R.string.copy), true) {
                getSystemService(ClipboardManager::class.java).setPrimaryClip(ClipData.newPlainText("code", s.code))
                island.announce(getString(R.string.code_copied))
            }), seconds = 0, stays = true)
    }

    private fun tabsArrived(sent: List<app.mica.account.Account.SentTab>) {
        val added = sent.filter { t -> Navigation.isWeb(t.url) && browser.tabs.none { it.url == t.url } }
        added.forEach { browser.open(it.url, activate = false).title = it.title.ifBlank { Uri.parse(it.url).host ?: it.url } }
        if (added.isEmpty()) return
        val from = added.map { it.from }.distinct().joinToString(", ")
        val title = if (added.size == 1) getString(R.string.tab_arrived, added[0].title.ifBlank { added[0].url }) else getString(R.string.tabs_arrived, added.size)
        bubble.show(title, getString(R.string.from_device, from), listOf(CardAction(getString(R.string.open), true) { browser.tabs.lastOrNull { it.url == added.last().url }?.let { browser.activate(it) } }))
    }

    private fun sendError(e: Exception): String = when {
        e is app.mica.account.ApiException && e.status == 401 -> getString(R.string.signed_out_again)
        e is app.mica.account.ApiException && e.code == "unknown_device" -> getString(R.string.send_unknown_device)
        e is app.mica.account.ApiException && e.code == "too_many_tabs" -> getString(R.string.send_too_many)
        e is app.mica.account.ApiException -> getString(R.string.send_failed, e.status.toString() + (e.code?.let { " $it" } ?: ""))
        else -> getString(R.string.sync_unreachable)
    }

    private fun sendToDevice(tab: Tab) {
        if (account.user == null) {
            bubble.show(getString(R.string.send_to_device), getString(R.string.send_sign_in_first), listOf(
                CardAction(getString(R.string.not_now)) {}, CardAction(getString(R.string.passwords_sync), true) { openPasswords() }), 0, null)
            return
        }
        account.bg({ account.devices() }) { devices, e ->
            if (devices == null) { island.announce(getString(if (e == null) R.string.signed_out_again else R.string.sync_unreachable)); return@bg }
            // One row per name, the most recently seen; older copies of this phone (from before a reinstall) are left out.
            val me = account.deviceName.trim()
            val others = devices.filter { it.id != account.deviceId && it.name.trim() != me }
                .groupBy { it.name.trim() }.map { (_, same) -> same.maxBy { it.seen } }.sortedByDescending { it.seen }
            if (others.isEmpty()) { bubble.show(getString(R.string.send_to_device), getString(R.string.no_other_devices), listOf(CardAction(getString(R.string.ok), true) {}), 0, null); return@bg }
            val day = java.text.DateFormat.getDateInstance(java.text.DateFormat.MEDIUM)
            bubble.show(getString(R.string.send_to_device), tab.title.ifBlank { Navigation.label(tab.url) }, listOf(CardAction(getString(R.string.cancel)) {}), 0,
                choices(others.map { it.name to (if (it.seen > 0) day.format(java.util.Date(it.seen)) else null) }) { i ->
                    bubble.hide()
                    val d = others[i]
                    account.sendTab(d, tab.url, tab.title) { x -> island.announce(if (x == null) getString(R.string.sent_to, d.name) else sendError(x)) }
                })
        }
    }

    /** Long press on the island opens it into the page's menu: forward, reload, desktop site, ads here, the block count. */
    private fun pageMenu() {
        val tab = browser.active ?: return
        val v = tab.view
        val items = ArrayList<Pair<String, () -> Unit>>()
        fun item(label: String, run: () -> Unit) = items.add(label to run)
        fun state(label: Int, on: Boolean) = getString(label) + ": " + getString(if (on) R.string.on else R.string.off)
        if (v?.canGoForward() == true) item(getString(R.string.forward)) { v.goForward() }
        if (v != null) item(getString(if (tab.progress in 1..99) R.string.stop else R.string.reload)) { if (tab.progress in 1..99) v.stopLoading() else v.reload() }
        if (v != null) item(getString(R.string.share)) { sharePage() }
        if (Reader.isReader(tab.url)) item(getString(R.string.reader_original)) { openOriginal(tab) }
        if (Navigation.isWeb(tab.url) && !tab.isPrivate) item(getString(R.string.reader)) { openReader(tab) }
        if (Navigation.isWeb(tab.url)) {
            val url = Navigation.fromEngine(tab.url)
            item(getString(if (browser.isBookmarked(url)) R.string.remove_bookmark else R.string.add_bookmark)) { toggleSaved(false) }
            // A private tab keeps nothing on the device, saved articles included.
            if (!tab.isPrivate) item(getString(if (browser.isSavedForLater(url)) R.string.remove_from_read_later else R.string.read_later_add)) { toggleSaved(true) }
        }
        if (Navigation.isWeb(tab.url)) item(getString(R.string.site_info)) { siteCard(tab) }
        if (browser.split != null) item(getString(R.string.end_split)) { browser.endSplit() }
        else if (Navigation.isWeb(tab.url)) item(getString(R.string.split_view)) { pickSplit(tab) }
        if (v != null) item(getString(R.string.find_in_page)) { showFind() }
        if (v != null) item(state(R.string.desktop_site, tab.desktop)) { browser.setDesktop(tab, !tab.desktop) }
        val host = tab.pageHost
        var detail: String? = null
        if (host != null && Navigation.isWeb(tab.url) && Blocker.enabled) {
            val allowed = Blocker.isAllowedSite(host)
            item(getString(R.string.ads_on_this_site) + ": " + getString(if (allowed) R.string.allowed else R.string.blocked)) { toggleAds(tab) }
            val n = tab.blocked.get()
            detail = when (n) { 0 -> getString(R.string.nothing_blocked); 1 -> getString(R.string.blocked_one); else -> getString(R.string.blocked_many, n) }
        }
        if (tab.playing) item(getString(R.string.picture_in_picture)) { enterPip() }
        if (v != null && Navigation.isWeb(tab.url) && !tab.isPrivate) item(getString(R.string.send_to_device)) { sendToDevice(tab) }
        item(getString(R.string.tab_options)) { tabMenu(tab) }
        item(getString(R.string.close_tab)) { browser.close(tab) }
        val title = tab.title.ifBlank { if (tab.isHome) getString(R.string.new_tab) else Navigation.label(tab.url) }
        bubble.show(title, detail, emptyList(), 0, choices(items.map { it.first to null }) { i -> bubble.hide(); items[i].second() })
    }

    private fun toggleAds(tab: Tab) {
        val host = tab.pageHost ?: return
        val site = app.mica.blocker.Domains.registrable(host)
        val prefs = (application as MicaApp).prefs
        val now = if (Blocker.isAllowedSite(host)) prefs.allowedSites.filterNot { app.mica.blocker.Domains.matches(setOf(it), host) }.toSet() else prefs.allowedSites + site
        prefs.allowedSites = now; Blocker.allowed = now
        tab.view?.reload()
    }

    /** Split view from the page menu: which tab goes beside this one. */
    private fun pickSplit(tab: Tab) {
        val others = browser.tabsIn(tab.space).filter { it !== tab && Navigation.isWeb(it.url) }
        if (others.isEmpty()) { island.announce(getString(R.string.split_none)); return }
        var sheet: Sheet? = null
        sheet = Sheet.show(this, getString(R.string.split_pick), null,
            choices(others.map { it.title.ifBlank { Navigation.label(it.url) } to Navigation.label(it.url) }) { i -> sheet?.dismiss(); browser.startSplit(tab, others[i]) })
    }

    /** What Mica knows about this site and what you can change for it (desktop's site card). */
    fun siteCard(tab: Tab) {
        val host = tab.pageHost ?: return
        val prefs = (application as MicaApp).prefs
        val secure = tab.url.startsWith("https://", true)
        val cert = tab.view?.certificate
        val detail = if (!secure) getString(R.string.site_not_secure)
        else if (Https.certificateAllowed(host, tab.isPrivate)) getString(R.string.site_cert_overridden) else {
            val by = cert?.issuedBy?.oName?.takeIf { it.isNotBlank() } ?: cert?.issuedBy?.cName?.takeIf { it.isNotBlank() }
            val until = cert?.validNotAfterDate?.let { java.text.DateFormat.getDateInstance(java.text.DateFormat.MEDIUM).format(it) }
            getString(R.string.site_secure) + if (by != null && until != null) "\n" + getString(R.string.site_certificate, by, until) else ""
        }
        val box = column()
        fun button(label: String, color: Int = col(R.color.mica_accent)) = text(label, 14f, color, bold = true).apply { pad(12, 8); background = pressable(0, 14) }
        fun line(label: String, vararg tail: View): LinearLayout {
            val name = text(label, 14f)
            return row(name, *tail).also { name.layoutParams = lp(0, WRAP, 1f); box.addView(it, lp(MATCH, WRAP).apply { topMargin = dp(4) }) }
        }

        // Text size, kept for this site.
        val percent = text("", 14f, col(R.color.mica_muted)).apply { gravity = Gravity.CENTER; minWidth = dp(52) }
        fun showZoom() { percent.text = prefs.zoomFor(host).toString() + "%" }
        showZoom()
        val smaller = button("A−").apply { setOnClickListener { browser.setZoom(tab, (prefs.zoomFor(host) - 10).coerceAtLeast(50)); showZoom() } }
        val bigger = button("A+").apply { setOnClickListener { browser.setZoom(tab, (prefs.zoomFor(host) + 10).coerceAtMost(300)); showZoom() } }
        line(getString(R.string.text_size), smaller, percent, bigger)

        if (Blocker.enabled) {
            val ads = button("")
            fun showAds() { ads.text = getString(if (Blocker.isAllowedSite(host)) R.string.allowed else R.string.blocked) }
            showAds()
            ads.setOnClickListener { toggleAds(tab); showAds() }
            line(getString(R.string.ads_on_this_site), ads)
        }

        SitePermissions.decisions(prefs).filter { android.net.Uri.parse(it.origin).host == host }.forEach { d ->
            val forget = button(getString(R.string.forget), col(R.color.mica_muted))
            val l = line(getString(d.kind.label).replaceFirstChar { it.uppercase() } + ": " + getString(if (d.allow) R.string.allowed else R.string.blocked), forget)
            forget.setOnClickListener { SitePermissions.forget(prefs, d); box.removeView(l) }
        }

        val clear = button(getString(R.string.clear_site_data))
        clear.setOnClickListener {
            bubble.show(getString(R.string.clear_site_data_q, host), getString(R.string.clear_site_data_detail), listOf(
                CardAction(getString(R.string.cancel)) {},
                CardAction(getString(R.string.clear), true) { browser.clearSiteData(tab); island.announce(getString(R.string.site_data_cleared)) }), 0, stays = true)
        }
        box.addView(clear, lp(WRAP, WRAP).apply { topMargin = dp(6); marginStart = -dp(12) })
        bubble.show(host, detail, emptyList(), 0, box)
    }

    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        // Ctrl+L / Ctrl+T on a hardware keyboard, as on desktop.
        if (event.action == KeyEvent.ACTION_DOWN && event.isCtrlPressed) {
            when (event.keyCode) {
                KeyEvent.KEYCODE_L, KeyEvent.KEYCODE_K -> { island.performClick(); return true }
                KeyEvent.KEYCODE_T -> { newTab(); return true }
                KeyEvent.KEYCODE_W -> { browser.active?.let { browser.close(it) }; return true }
            }
        }
        return super.dispatchKeyEvent(event)
    }

    companion object {
        const val ACTION_SIGN_IN = "app.mica.SIGN_IN"
        private val PRIVATE_TINT = 0xFF2E2836.toInt()
    }
}
