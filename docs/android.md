# Mica for Android: design and plan

Status (2026-10-26): first version built in `android\`. The design (island, spaces rail and Spotlight
with the deck) is implemented, the debug APK builds and the unit tests pass. It has been smoke-tested on a
Samsung phone and on LDPlayer; see section 9.

Mica for Android is a separate app in `android\`. It uses the system WebView through
`androidx.webkit`, not GeckoView and not a bundled Chromium. It doesn't touch `src\` (Windows)
or `cross\` (macOS and Linux). Two things are shared with desktop, and both are copied into the
APK when Gradle builds it rather than duplicated by hand:

| Shared file | Where it comes from | How Android uses it |
|---|---|---|
| Blocker rule lists | `extensions\mica-blocker\rules\*.json`, `default.json` | Loaded from assets and matched natively |
| Game pages | `src\GameHub.html`, `Tetris.html`, `Snake.html` and the rest (`Navigation.Games` in `src\Core.cs`) | Served from assets under `games.mica.invalid` and `tetris.mica.invalid` |
| Vietnamese text | `src\Strings.vi.tsv` (English, tab, Vietnamese) | `res\values-vi\strings.xml` is generated from it |

The standing goal is the same as desktop: use as little memory, CPU and battery as possible,
stay smooth, and never cut off media that's playing.

## 1. What carries over and what's rebuilt

**Carries over unchanged**
- The rule lists and the default choice of lists (`default.json`). A single tools script
  (`tools\build-blocker-rules.ps1`) updates desktop and Android together.
- The blocker's behaviour:
  - allow rules win over block rules at the same priority;
  - a rule that has only `requestDomains` and blocks never blocks a site's own requests (the
    first-party twin in `blocker.js`);
  - excluded sites are allowed everything.
- The game pages, their hosts, their paths and their dark-mode contract. A dark page is served
  as `<html lang="en" class="dark">`, and a theme change runs
  `classList.toggle('dark', …); dispatchEvent(new Event('micatheme'))`. This copies
  `BrowserGames.cs`, including the CSP header.
- The `mica://` addresses: `mica://games`, `mica://snake`, `mica://tetris` and the rest map to
  the engine addresses the same way as `Navigation.EngineAddress` / `FromEngine`.
- The Vietnamese translations, keyed by their English source text.

**Rebuilt for Android and touch**
- **Blocker engine.** WebView has no extensions and no declarativeNetRequest, so the matcher is
  written in Kotlin and runs in `shouldInterceptRequest` (section 2).
- **Shell UI.** WPF windows become one Activity. The page fills the screen, under three touch
  surfaces taken from desktop: the island, the spaces rail and Spotlight with the deck
  (section 4).
- **Tabs and resting.** WebView2's memory-target and suspend APIs are replaced with
  `WebView.onPause()` and by destroying resting tabs (section 3).
- **Downloads** go through `DownloadManager` instead of the desktop download island.
- **File uploads** use `onShowFileChooser` and the system document picker.
- **Settings.** A short settings page instead of desktop's settings window.

**Left out of v1:** sign-in and sync, vault and autofill, extensions, split view, AI panel, reader
view, tab groups,
DevTools and cosmetic filtering. The lists are network rules only anyway.

## 2. Blocker

### Loading
- Rules are copied from `extensions\mica-blocker\rules` into `assets/blocker/` at build time
  Only the lists `default.json` turns on are copied, about 4.6 MB. They're stored uncompressed so
  they can be read straight from the APK.
- At startup a background thread reads `default.json` and only the lists it names, using a small
  streaming JSON parser written in plain Kotlin. Plain Kotlin keeps it unit-testable on the JVM
  with no Android dependency.
- Requests that arrive before loading finishes are allowed. Loading is expected to take around
  200–400 ms on a mid-range phone; this will be measured.
- The compiled engine is immutable, so the WebView's IO threads can read it without locks.

### Conditions supported
`requestDomains`, `excludedRequestDomains`, `initiatorDomains`, `excludedInitiatorDomains`,
`urlFilter` (with `||`, `|`, `^` and `*`), `domainType`, `resourceTypes`,
`excludedResourceTypes`, `requestMethods`, and `priority`.

Some rules can't be applied before a response arrives:
- rules with `responseHeaders` (6 in uBO filters) are skipped rather than over-blocking;
- `regexFilter` doesn't appear in the current lists, so rules that use it are skipped and logged.

### Indexing: avoid scanning every rule
Each rule is filed in exactly one bucket, chosen in this order:

| Bucket | Key | Lookup |
|---|---|---|
| Host | Each entry of `requestDomains`, or the host at the start of a `\|\|host…` url filter | Walk the request host's suffixes (`a.b.ads.com` → `b.ads.com` → `ads.com` → `com`); each step is one `HashMap` lookup |
| Token | The longest run of at least 3 letters or digits in the url filter, without a wildcard on either side | Split the URL into tokens and look each one up |
| Generic | Anything else (there are few) | Checked for every request |

A request checks only the rules in the buckets it hits, so the cost per request is small and
doesn't grow with the size of the lists. Domain sets inside a rule (initiator and excluded) are
`HashSet<String>`, and are tested with the same suffix walk.

### Decision
1. If the page's site is on the excluded list (or the blocker is off), allow.
2. Collect the rules that match.
   - A block rule with only `requestDomains` is ignored when the request is first-party to the
     page (the desktop exception).
   - The winner is the highest priority, and at equal priority allow beats block, as in
     declarativeNetRequest.
3. A blocked request gets an empty `WebResourceResponse` (status 204, no body), which costs
   less than a network error page. Main-frame requests are blocked only by rules that list
   `main_frame`, and the user then sees a small "Blocked by Mica" page.

### What WebView doesn't tell us, and the approximations
- **Initiator.** `shouldInterceptRequest` doesn't say which page made a request. The matcher
  uses the host of the tab's top-level page, tracked in `onPageStarted` and stored in a volatile
  field on that tab's client. For requests from iframes this is a little looser than Chromium,
  which uses the frame's origin. The `Origin` header is used instead when it's present.
- **Resource type.** This is inferred, in order, from:
  - `isForMainFrame`;
  - the `Sec-Fetch-Dest` header, when WebView sends it;
  - the `Accept` header (`text/css`, `image/*`);
  - the file extension (`.js`, `.css`, `.png`, `.woff2`, `.mp4` …);
  - `X-Requested-With`, which means xhr.

  Anything still unknown is `other`. A rule with no `resourceTypes` matches everything except
  `main_frame`, as in declarativeNetRequest.
- **First party / third party** compares registrable domains (eTLD+1). v1 ships a compact table
  of the common multi-label public suffixes (`co.uk`, `com.vn`, `com.au` …) and treats every
  other domain as a single-label TLD. The full public suffix list is a later step if the table
  proves too rough.

### Count
Each tab keeps an atomic count of the requests blocked since its last main-frame load. The menu
shows "N blocked on this page", the same number as desktop's site card.

### Tests
JVM unit tests (`app/src/test`) cover:
- url-filter anchors and `^`;
- matching a domain and its subdomains;
- `domainType`, initiator include and exclude, and resource types;
- allow beating block, and the first-party exception;
- excluded sites;
- a load of the real `easylist.json` and `easyprivacy.json`, with known ad and tracker URLs
  blocked and ordinary URLs allowed, plus a rough timing check (target: under 50 µs per request
  on the build machine).

## 3. Tabs, resting and battery

**Model.** A `Tab` holds an id, URL, title, a `space` (always "default" for now), and either a live
`WebView` or nothing.

**Saving.** The tab list is written to `files/tabs.json`, at most once every 2 seconds and
whenever the app goes to the background. When it starts again, only the active tab is loaded;
the others come back as resting cards that load when opened.

**At most a few live WebViews** (desktop's `BrowserMemory`, adapted for a phone; see "Memory, measured" below).
- The active tab is live and resumed.
- A tab you leave is paused at once. Mica also takes a small picture of it (a half-size JPEG, about
  60 KB, via `PixelCopy`, at most 12 kept), and notes whether you typed into it.
- 15 seconds later the tab freezes. Mica tells WebView to give memory back (`MicaApp.purgeEngine()`
  passes `TRIM_MEMORY_RUNNING_CRITICAL` to WebView's own memory-pressure listener). Nothing reloads.
  - This runs at most once a minute.
  - It never runs while something plays.
- One hidden tab stays live (none on a low-RAM phone), so switching back to it is instant. A tab
  left in the last 45 seconds is spared, so flicking between a few tabs never reloads them.
- Other hidden tabs rest after 3 minutes (2 on a low-RAM phone). The WebView is destroyed and only
  `saveState()` is kept. When you open the tab again, its picture covers it while it reloads.
- Resting is followed by a purge, because a page's memory otherwise stays in WebView's renderer.
- Tabs that play, are loading, or hold text you typed never rest.

**Away from Mica.**
- After 1 minute away, hidden tabs rest and WebView purges.
- After 5 minutes away, the tab you were on rests too. With no page left, the renderer shrinks to
  about 70 MB. The page reloads behind its picture when you come back.
- Neither step runs while something plays.

**Memory, measured.** On a Samsung (Android 12, 1440×3040), with YouTube, VnExpress, GitHub, Reddit
and Wikipedia open and Wikipedia shown:

| Moment | Before | Now |
|---|---|---|
| In use, about a minute later | about 470 MB | about 470 MB |
| 1 minute away | 367 MB | 357 MB |
| 5 minutes away | 367 MB | 162 MB |

- While Mica is in use, most memory belongs to the page on screen. Reddit with video takes about
  130–170 MB of in-process GPU memory alone; Wikipedia takes about 60 MB.
- WebView's single renderer barely shrinks while any page is open.

**When the app goes to the background**, `pauseTimers()` and `onPause()` are called on every
tab, *unless a tab is playing media*.
- A document-start script, added with `WebViewCompat.addDocumentStartJavaScript`, reports
  `play`, `pause` and `ended` from media elements through `addWebMessageListener`.
- A playing tab is never paused, rested or dropped.

**Memory pressure.** `onTrimMemory(RUNNING_LOW)` and above rests every non-active tab that isn't
playing media. `onRenderProcessGone` rebuilds the tab from its saved state instead of crashing
the app.

**Background audio after the screen locks** needs a media foreground service with a
`MediaSession`. That's v2. v1 only promises never to pause media itself.

**One WebView profile** is used in v1. Later, private tabs get their own short-lived profile
through `ProfileStore` where the WebView supports `MULTI_PROFILE`. Spaces can do the same if
they ever need separate cookies.

## 4. UI (one Activity, plain Views)

The look comes from desktop Mica rather than a generic phone browser:
- the colours are muted lavender (`#71608C`, or `#C8B4E0` when dark), charcoal ink and a warm
  ivory canvas (`#FAF9F6`);
- the shapes are frosted, rounded glass;
- three pieces of desktop carry over: the island, the sidebar of spaces, and Spotlight.

The page fills the whole screen. There's no toolbar, and three surfaces sit on top of it.

### The island (from desktop's status island)
- **What it is.** A frosted pill floating 16 dp above the bottom edge, centred. From left to
  right it shows:
  - a dot in the space's colour;
  - the page's host;
  - one small dot per tab in the space, with the active one filled;
  - loading progress, drawn as a thin fill along the pill.
- **Gestures.**
  - Tap: opens Spotlight.
  - Swipe left or right: previous or next tab in the space.
  - Swipe up: opens the deck.
  - Long press: a small row of actions (forward, reload, share, find in page, desktop site).
- **System back** goes back in the page, then closes the tab if it was opened from a link.
- **While reading.** The pill shrinks to the space dot when you scroll down and grows back when
  you scroll up. It widens for a few seconds when there's news, as on desktop: "12 blocked",
  "Downloading report.pdf", "Saved for offline".
- **Ambient tint.** When a page finishes loading, a one-shot script reads its `theme-color`
  (or, failing that, the body background). The island, rail and Spotlight glass are tinted
  with that colour blended 25% into lavender. It costs one script call per page load, with no
  timers.

### The spaces rail (from desktop's sidebar)
- **Opening it.** Drag from the left edge (`DrawerLayout`, kept clear of Android's back
  gesture), or tap the space dot on the island.
- **What's in it, from top to bottom:**
  - space chips (Personal and Work to start; "+" adds one with a name and colour);
  - up to six pinned tabs as tiles;
  - the space's tabs as a list, with a ♪ on a playing tab and resting tabs in muted italics;
  - "+ New tab".
- **Gestures.** Swipe a row to close the tab, and long press it to pin or unpin it or move it to
  another space.
- **Switching space** opens the tab that space last had active and re-tints the frame.

### Spotlight and the deck (from desktop's Spotlight)
- **Spotlight** is a sheet that rises from the island with the keyboard already up. It has:
  - the field, which shows the full URL for editing;
  - while you type: results from tabs in every space, then history, then "Search for …".
    Results are ranked like desktop's `Navigation` search;
  - with an empty field: a row of actions (back, forward, reload, share, downloads, settings),
    the games strip, and recent tabs.
- **A new tab** is ivory with Spotlight open. There's no separate start page.
- **The deck** is the tab switcher: the space's tabs as a fanned stack of cards, newest at the
  front.
  - It's built from a `RecyclerView` whose cards overlap (a negative item offset), with the top
    three slightly rotated.
  - Tap a card to open it. Swipe it sideways to close it (`ItemTouchHelper`).
  - Space chips across the top change space. Sideways swipes are taken by closing cards.
  - Cards show the title, host and favicon, with a ♪ when playing and "resting" when the tab
    has no live WebView. To save memory there are no live thumbnails.

### Settings
A short page opened from Spotlight's action row. It has:
- Dark mode: system, light or dark;
- Ad blocker: on or off, a list of allowed sites, and which lists are loaded;
- Language: system, English or Tiếng Việt;
- Desktop site by default.
- **Dark mode.** The app uses an AppCompat DayNight theme.
  - Websites use `prefers-color-scheme` through WebView's algorithmic-darkening rules (target
    SDK 33+), so a site goes dark only if it supports it.
  - Games get the `dark` class as described in section 1.
  - Changing the mode flips any open game in place.
- **Language.** English is the base. `values-vi/strings.xml` is generated at build time: each
  English string in `values/strings.xml` is looked up in `Strings.vi.tsv`, and strings the TSV
  doesn't have are listed in the build log and fall back to a hand-written
  `values-vi/strings_android.xml`. The language is switched per app with
  `AppCompatDelegate.setApplicationLocales`, and `locales_config.xml` lets Android 13+ change
  it from system settings.

### Library and site permissions

- **Library** (`Library.kt`): bookmarks, history by day, read later and recently closed (25), with a search over the section shown. Like the deck, it grows out of the island. Open it from Spotlight (▤), from the rail, or from Back when it's in front. Bookmarks and read later are added from the long-press menu (or ☆ in Spotlight). Everything lives in `state.json` next to tabs and history, and Spotlight's results include bookmarks.
- **Reading mode** (`Reader.kt`): desktop's `src\Reader.js` (copied in at build time) finds the article.
  - Mica draws it as its own page, `mica://read/<site>/<id>`, served on `read.mica.invalid` with a strict CSP, in the same tab, so Back returns to the page.
  - The bar on top has Original page, A−/A+ (14–30 px, kept in Prefs), Aa (serif) and Read later. These buttons post to `micaReader`, which only the reader's origin can reach.
  - Open it from the long-press menu.
- **Read later** keeps an article whole in `files/ReadLater/<id>.json`, so it reads offline (pictures still need a connection). A page that isn't an article is kept as its address. Articles that were only read sit in the cache for two days.
- **Highlights** (`Highlights.kt`, from `BrowserHighlights.cs`): select text and choose Highlight in the menu over the selection.
  - Highlights are kept per page in `files/highlights.json` and put back on every load. In reading mode they're keyed by the article's address, so they show on both.
  - The library has a Highlights section; ✕ removes one, from open pages too.
- **Site permissions** (`SitePermissions.kt`): camera, microphone, location, protected media and MIDI.
  - Only the tab in front of an https page (or localhost) can ask. The island opens into Allow / Block, and the answer is kept per site and kind.
  - Android's own permission is asked only after a site is allowed, and only for what that site needs. If Android refuses, the island says so.
  - Settings → Site permissions lists the kept answers; tap one to forget it.
- **Private tabs** live in a space that's never saved. Each private session gets its own WebView profile (androidx.webkit `MULTI_PROFILE`, WebView 123+); without it, Mica doesn't offer private tabs.
  - Private tabs get no history, no recently closed entry, no login scripts, highlights, reading mode, read later or send to device. The window is FLAG_SECURE while one is in front.
  - Chromium keeps a used profile until the process ends. So when the last private tab closes, its cookies, site storage and cache are cleared, and the profile folder is deleted the next time Mica starts.
  - Open one from the deck ("Private") or the rail ("New private tab").
- **Tab groups** (`TabGroup` in `state.json`, as desktop's): a group's tabs sit together under a header in the deck. Tap the header to fold the group; hold it to rename, recolour, ungroup or close the group.
  - Hold a card (deck or rail) for the tab's menu: pin, add to a group (or a new one), leave the group, move to a space, close.
- **Split view** (`SplitLayout.kt`, from `BrowserSplit.cs`): two web tabs of one space, stacked in portrait and side by side in landscape. Drag the divider to resize the panes (each gets 20–80% of the room).
  - The pane you touch becomes the active tab, outlined in accent. Opening another website replaces that pane; a new tab page, another space or closing a pane ends the split.
  - Both panes stay awake. The tab budget and resting skip them.
  - To start one, use the long-press menu ("Split view…"), a tab's menu ("Split with current tab") or a link's menu ("Open in split view"). The second pane slides in. "End split view" grows the active pane back over the whole screen.
- **Peek** (`Peek.kt`, from `BrowserPeek.cs`): "Peek" in a link's menu opens the link in a card that grows out of the island.
  - Its WebView is separate and short-lived: ads are blocked, and there are no permissions, downloads, windows or app links. It only navigates on a tap, and a private tab's peek uses the private profile.
  - Back steps back inside the peek, then closes it. A tap outside closes it too. "Open as tab" keeps the page.
- **Site info** (long-press menu; desktop's `BrowserSiteCard.cs`): a card with the connection and certificate (issuer, expiry) and the text size (A−/A+ in steps of 10%, kept per host in Prefs `siteZoom` and applied as pages start). It also has ads on this site, this site's permission decisions (Forget) and Clear site data. Clear site data expires the host's cookies and deletes its web storage, in the private profile for a private tab, then reloads.
- **Error page** (`ErrorPage.kt`, desktop's ErrorPanel): when a main frame fails, Mica writes its own screen over WebView's.
  - Normally it reads "A small detour." with the error code. With no connection it reads "Oh no, no internet."
  - It has Try again and, for connection errors, a game. It retries by itself when the connection comes back.
  - Cancelled loads (`ERR_ABORTED`) are left alone. The address and Back stay the page's own.
- **Home** (`Home.kt`, desktop's RenderHome): a new tab shows the time, the date and a greeting. Below that are the top sites: 8 tiles, 4 per row on a phone.
  - Ranking (`Browser.topSites`) is desktop's: history grouped by origin, scored by the sum of its pages' visits (`HistoryEntry.visits`, saved as `v`), with the most recent first on a tie.
  - History keeps 500 entries.
  - Hold a tile to remove the site, which takes its pages out of history.
  - Up to 3 recently closed pages show as chips.
  - It animates in, piece by piece. The clock ticks only while the page is showing. A private tab shows only where you are.
- **Sessions** (`Sessions.kt`, desktop's BrowserSessions): the rail → Sessions saves every website tab outside private browsing to `files/Sessions/<name>.json`. The name defaults to "Session d MMM HH:mm" and is capped at 60 characters.
  - Restoring adds the tabs beside what's open, as one folded group per space named after the session. They rest until opened. A session can be deleted from its menu.
- **Clear browsing data** (Settings → Privacy): last hour, 24 hours, 7 days or all time. You can clear history (plus recently closed for all time), cookies and site data, cache, and form data.
  - WebView keeps cookies, storage and cache without dates, so those always go whole. The sheet says so.
  - Bookmarks, passwords, jars and private tabs aren't touched.
- **HTTPS first** (`Https.kt`, desktop's BrowserProtection): always on. An `http://` address loads as `https://`, unless it's local: a non-default port, no dot, localhost, `.local`/`.test`/`.lan`/…, or a private or loopback IP.
  - If the secure address fails (including a bad certificate), Mica's error page says "No secure connection" with a Continue without HTTPS button. That button goes through `https://continue-http.mica.invalid/`, which Mica catches, only on a tap.
  - A site whose https address redirects back to http is let through automatically.
  - Exceptions are per registrable domain and last until Mica closes.
  - WebView has no tracker-prevention levels like WebView2, so protection stays with the ad blocker.
- **Page notices**:
  - A pop-up opened without a tap is blocked, and a card says where it wanted to go, with Keep blocking or Open it. A throwaway WebView that can't use the network learns the address. `javaScriptCanOpenWindowsAutomatically` is on so these reach `onCreateWindow`. At most one card shows every 3 s.
  - `alert`, `confirm`, `prompt` and "Leave this page?" are island cards. A page in a background tab can't show one. More than 3 in 10 s silences the page until it navigates.
  - Back or another card answers Cancel; an alert counts as read.
  - HTTP sign-in (basic auth) is a sheet you type into. Mica never fills it.
- **Cookie jars** (desktop's BrowserJars): a tab's menu → Cookie jar puts it in Work, Shopping, Banking, Social or a jar you make. Each jar is its own WebView profile (`mica-jar-<id>`), with separate cookies, storage and sign-ins.
  - Links and windows opened from a jar tab stay in it, and so does a peek.
  - Moving a tab starts its page again there.
  - Deleting a jar sends its tabs back to Mica's own cookies and deletes the profile. If WebView still holds the profile, its cookies and storage are cleared now and the folder goes at the next start.
  - Jars are saved in `state.json` (`jars`, and `jar` per tab).
- **`blob:` and `data:` downloads** (`Blobs.kt`): the page reads the file and hands it over in pieces through `micaBlob`. Each piece carries a one-time token from Mica, the size limit is 200 MB, and the file lands in Downloads (MediaStore).
  - The link's `download="name"` is remembered by a document-start script, because Android's download listener never sees it.
- **Public suffix list**: `assets/psl.dat` (publicsuffix.org, MPL 2.0, comments removed) is read on first use. `Domains.registrable` follows it, including wildcard and exception rules and the private section (`github.io` sites are separate).
  - Unit tests without the asset use the short built-in list.
- **Blocker cache** (`RuleCache.kt`): the parsed rules are written once per app build to `noBackupFilesDir/blocker-<build>.bin`.
  - On the Samsung, startup load went from about 610 ms (JSON) to about 305 ms. The first start after an update takes about 1.2 s while it writes the file.
- Spotlight, the deck, the library, peek and cards each hide the pill while they're open. Whichever closes last brings it back (`MainActivity.islandBusy`).

## 5. Downloads and uploads

**Downloads.**
- `setDownloadListener` builds a `DownloadManager.Request` with the page's cookies, user agent
  and referer, and a file name from `URLUtil.guessFileName`.
- Files go to the public Downloads folder, which needs no permission on API 29+. On API 26–28
  the app asks for `WRITE_EXTERNAL_STORAGE` the first time.
- `blob:` and `data:` downloads are v2. They need a JS bridge to read the bytes.

**Uploads.** `onShowFileChooser` launches `FileChooserParams.createIntent()` through the
Activity Result API and returns the chosen URIs. Camera capture is v2.

## 5b. Account, passwords and sync

The code is in `android/app/src/main/java/app/mica/account/`. It uses the same service as desktop (`https://the-api.lessnoise.online`), and the same formats byte for byte, so a vault synced from Windows opens on Android with the same master password, and the other way round.

- **Crypto:** `VaultCrypto.kt` uses its own PBKDF2-HMAC-SHA256 (600,000 rounds, 64 bytes), AES-256-CBC, and HMAC-SHA256 over desktop's canonical string. It also handles vault keys (`\u0001MicaVaultKey1:`) and PKCE.
- **Payload:** `Vault.kt` keeps desktop's PascalCase JSON. It passes `Snapshot`, `InboxPrivate` and unknown fields through untouched, and writes `/Date(ms)/` back in desktop's escaped `\/Date(ms)\/` form.
- **Other formats:** `Totp.kt` is a port of `src/Totp.cs` (RFC 6238, normalized `otpauth://` links). `PasswordCsv.kt` reads password exports from Chrome, Edge, Firefox and Bitwarden.
- **Tests:** `VaultTest` decrypts a vault written by desktop's own code (`src/test/resources/desktop-vault.json`, made with `VaultModel.cs` and `Totp.cs`). It also checks TOTP codes, PKCE, RFC vectors and CSV parsing. In the other direction, desktop's `VaultCrypto.Decrypt` opens a vault made by Android, both from the unit test and from LDPlayer.
- **Device secrets:** `Sealed.kt` holds the sign-in token, this device's vault key (for silent autofill) and logins saved while the vault is locked. It uses AES-GCM with an Android Keystore key, which plays the role of desktop's DPAPI.
- **`Account.kt`:**
  - **Discord sign-in:** the code is shown in a bubble and Discord opens in a Mica tab. It polls every 2 s on its own thread.
  - **Vault:** silent open with the device key, unlock or create with a master password (12+ characters), and lock.
  - **First saved login:** this creates the vault with a device key, and the master password is chosen later, as on desktop.
  - **Sync:** one "Sync now" step does what desktop's Pull and Push buttons do, in a safe order:
    - The cloud copy is fetched and opened first.
    - A newer cloud copy replaces the local one, but logins saved or changed only on this device are kept and pushed.
    - Local changes are pushed with `Revision = max(seen, Revision) + 1`.
    - On a 409, it pulls again.
    - A cloud copy older than one this device has seen is overwritten by the local copy.
    - It runs by itself 3 s after a change, and on return to the app if the last sync was 15+ minutes ago.
  - **Settings:** `/v1/settings` syncs `dark` (the theme). `ambientYoutube` and `strictTracking` are kept as received; the newest `updatedAt` wins.
  - **Send tab:** this uses `/v1/devices` and `/v1/tabs`. It checks on launch, on return to the app, and every 2 minutes while Mica is open. The page menu has "Send to device". Arriving tabs are added as resting tabs.
- **Autofill:** `Autofill.kt` runs desktop's `LoginScript` and `TotpScript`, copied into `assets/scripts` with the bridge swapped to `micaLogin`. The in-page dropdown and the passkey shim are dropped.
  - Saved logins are offered in Mica's own bubble (`Bubble.kt`), which a page can't press.
  - "Save password?" is a dialog with editable fields and a Never option.
  - A 2FA setup key shown on a page is offered for saving, and codes are filled by themselves.
- **Screens:** `PasswordsActivity` uses FLAG_SECURE. It covers the account, the master password, the logins list with live 2FA codes, copy (marked sensitive, cleared after 30 s), edit, delete, and CSV import. It's reached from the rail and from Settings.
- **Debug only:** `https://login.mica.test/` serves a fake sign-in form from `src/debug/assets/test` for trying autofill.
- **Passkeys:** WebViews run in WebAuthn browser mode (`WEB_AUTHENTICATION_SUPPORT_FOR_BROWSER`, androidx.webkit 1.14), and the manifest declares `CREDENTIAL_MANAGER_SET_ORIGIN`. A site's `navigator.credentials` request then goes to Android with the site's origin:
  - On Android 14+ it goes to Credential Manager, which reaches Google Password Manager, Samsung Pass, Bitwarden and other providers.
  - On older Android it goes to Play services' FIDO2 privileged API.
  - **Blocker:** both paths serve only browsers that the providers have approved. Tested on the Samsung (Android 12, WebView 157): Play services answers `FIDO2_PRIVILEGED_API is not available … RESTRICTED`.
  - **Getting approved:**
    - Google Password Manager: approval comes through Google's request form (see "Privileged apps" in Android's Credential Manager docs). The form needs the package name and the release certificate's SHA-256.
    - Bitwarden, 1Password and others keep their own lists.
  - **Until then:** `assets/scripts/passkey.js` notices an instant `NotAllowedError` and Mica explains it once per session in a bubble.
- **Not ported:** Windows Hello unlock (biometric unlock could come later), the desktop-only locked-vault inbox, and snapshot restore (Android keeps `Snapshot` but doesn't apply it).

## 6. Project shape

```
android\
  settings.gradle.kts, build.gradle.kts, gradle.properties, gradlew(.bat), gradle\wrapper\
  app\build.gradle.kts           copyBlockerRules, copyGames, generateViStrings tasks
  app\src\main\java\app\mica\
    MainActivity.kt              lifecycle, wiring, back handling
    Island.kt                    the floating pill and its gestures
    Rail.kt                      spaces drawer
    Spotlight.kt, Deck.kt        search sheet, card-deck switcher
    SettingsActivity.kt
    Tabs.kt                      Tab, Space, TabStore (tabs.json), resting policy
    MicaWebClient.kt             shouldInterceptRequest → games, blocker
    MicaChromeClient.kt          uploads, progress, fullscreen video
    Games.kt                     mica:// ↔ engine addresses, themed pages
    Downloads.kt
    blocker\Json.kt, Rules.kt, UrlFilter.kt, Engine.kt, Domains.kt   (no Android imports)
  app\src\test\java\app\mica\blocker\EngineTest.kt
```

- **Versions.** Kotlin 2.x, AGP 8.x, compile and target SDK 36, minimum SDK 26 (Android 8.0).
- **Dependencies:** `androidx.webkit`, `androidx.appcompat`, `androidx.recyclerview`,
  `androidx.drawerlayout`, `androidx.activity`. No Material and no Compose, so the APK and memory stay small.
- **Application id:** `online.lessnoise.mica` (the name Google and other passkey providers approve). The Kotlin namespace stays `app.mica`. The debug build is signed with the debug key only.
  Desktop's release signing key isn't used or referenced.
- **Play release:** `./gradlew bundleRelease` builds `app/build/outputs/bundle/release/app-release.aab`. It's signed with the Play upload key, whose keystore and password live outside the repo in `D:\creds\mica-upload.properties` (or pass `-Pmica.upload=<file>`).
  - Without that file, the release build is unsigned.
  - Play App Signing holds the real app key; the upload key can be reset through Play support if it's lost.
  - The mapping file for crash reports ships inside the bundle.
  - Store art is in `android/store`: `icon-512.png` and `feature-graphic.png` (1024×500).
- **R8:** `proguard-rules.pro` repackages everything, allows access modification, strips `Log.d/v/i` and Kotlin's parameter null checks, and keeps line numbers. `android.r8.optimizedResourceShrinking` is on.
  - Nothing needs `-keep`: Mica uses no reflection or `@JavascriptInterface`, and pages talk to it through WebMessageListener strings.
  - `./gradlew assembleR8test` builds the same R8 output as `online.lessnoise.mica.r8test` with the debug key, to try beside the debug app. The R8 APK is 5.7 MB against 9.5 MB for debug.

## 7. Order of work

1. **Scaffold.** Gradle wrapper, an empty Activity, and the copy and generate tasks. Done when
   `assembleDebug` passes.
2. **Blocker engine and unit tests.** Pure Kotlin, so it's testable before any UI exists.
3. **One-tab browser.** WebView, the island, Spotlight (address and search, back, forward,
   reload), dark mode.
4. **Games host.** `mica://games` and the 11 game pages, themed.
5. **Tabs and spaces.** The deck, the rail, saving, pausing and resting, the media guard,
   `onTrimMemory`, ambient tint.
6. **Blocker wiring.** `shouldInterceptRequest`, the count, per-site allow, an on/off switch.
7. **Downloads, uploads, Vietnamese strings, settings.**
8. **Smoke test on a device or emulator.** Load a page, open a game, check that a known ad host
   (`doubleclick.net`) is blocked, then measure memory with three tabs.

Later: media foreground service and `MediaSession`, private tabs on a separate profile,
reader view (a port of desktop's reader script), `blob:` downloads, the full public
suffix list, a cached binary rule index for faster startup, and Play-store release signing.

## 8. Toolchain on this machine

- JDK 17 (Temurin) is at `D:\Android\jdk17`.
- The Android SDK is at `D:\Android\Sdk`, with platform 36, build-tools 36.0.0, platform-tools
  and cmdline-tools.
- Gradle 8.14.3 is unpacked under `%USERPROFILE%\.gradle\dist-manual`, used only to generate
  the wrapper.
- No emulator or system image is installed. LDPlayer (`D:\LDPlayer`) can serve as an `adb`
  target for the smoke test, but it opens a visible window.
- D: has about 8 GB free, so Gradle caches stay on C: (`%USERPROFILE%\.gradle`).

## 9. Build and test

Build and test from `android\`, with `JAVA_HOME` set to `D:\Android\jdk17`:

```
gradlew assembleDebug          -> app\build\outputs\apk\debug\app-debug.apk
gradlew testDebugUnitTest      -> blocker matcher and Navigation tests (JVM, no device)
```

**Results on 2026-10-26: 16 of 16 tests pass.**
- The real default lists give 17,907 rules, which load in about 0.5 s on the build PC, with 88
  rules left in the generic bucket.
- The index takes about 12.5 MB of heap. Hosts are kept as 64-bit hashes in flat arrays, and the
  first-party twin is a flag on the rule rather than a second rule. The first version used
  HashMaps of strings and took 28 MB.
- Matching averages 18–25 µs per request on the JVM, including building the `Request`. The
  target was under 50 µs.
- Known ad and tracker URLs are blocked: `doubleclick.net`, `googlesyndication.com`,
  `google-analytics.com`, `connect.facebook.net` and `ads-twitter.com`. Ordinary URLs (Wikipedia,
  GitHub, jsDelivr, Google Fonts) are allowed.

**The debug APK is 15.9 MB.** 8.4 MB of that is debug code, which isn't shrunk; release builds
turn on R8.

**Smoke test on two devices:**
- Samsung Galaxy Note10+ (SM-N975F): Android 12, WebView 157, 1440×3040.
- LDPlayer 14: Android 14, Chrome 124 as the WebView, 1600×900.

| Check | Samsung | LDPlayer |
|---|---|---|
| Cold start to first screen | 0.8–0.9 s | 4.3–4.7 s |
| Rules loaded | 0.5–1.5 s | 7.5 s |
| Page from a typed address or a link | vnexpress.net loads | theverge.com loads |
| Blocker on a real page | 15 blocked on vnexpress (doubleclick, GTM, GA, eclick.vn) | 15 blocked on The Verge (doubleclick, amazon-adsystem, criteria, rubicon…); island shows "15 blocked" |
| Games | Snake is dark and follows the theme | Hub is light; the island shows `mica://games` |
| Vietnamese | Follows the phone's language: Spotlight, actions and island | — |
| Audio keeps playing in the background | — | Android reports `state:started` after Home |

**Memory on the Samsung, three tabs live:**
- The app process is about 250 MB PSS. Of that, 106 MB is graphics for the 1440×3040 screen and
  34 MB is Java heap (26 MB after the host-table change).
- The shared WebView renderer is about 195 MB.

**Fixed during the smoke test:**
- When Spotlight opened together with the app, the keyboard came up but the field didn't take
  input. Spotlight now focuses the field once the window has focus.
- On a cold start the first page's requests could arrive before the rules loaded. They now wait
  for the rules for up to 3 s, on WebView's network threads.

**Passwords and sync (2026-10-26), on LDPlayer and the Samsung:**
- **Tested on the test form** (`https://login.mica.test/`):
  - A 2FA setup key is spotted on the page, saved, and the code filled by itself. Codes were checked against an independent HMAC calculation.
  - "Save password?" appears, then the login is offered and filled on the next visit.
  - A master password chosen for a vault made with the device key: desktop's `VaultCrypto.Decrypt` opens the result.
  - Sign-in against the live service: the code is shown and Discord's login page opens in a tab.
- **Fixed:**
  - Pages with no background of their own were drawn on Mica's dark canvas, so their dark text was unreadable. They now get white once they show; the canvas colour is used only while loading.
  - Android's autofill service (Google Password Manager) asked to save as well as Mica. Pages are now hidden from it while "Mica saves and fills passwords" is on. That's a toggle on the Passwords screen; turning it off hands over to Google or Bitwarden.
  - The media pause/play scripts were missing the quotes around `'video,audio'`.
- **Not yet checked:** a full sync round-trip, which needs a Discord login.

**Known gaps:**
- On a slow device (LDPlayer takes 7.5 s to parse the JSON) the first page after a cold start
  can still let some requests through. Next step: a cached binary index written on first load.
- Keeping audio going after the screen locks needs a media foreground service. A few seconds in
  the background were checked, not a long session.
- Debug builds log every blocked URL under the `MicaBlock` tag.
