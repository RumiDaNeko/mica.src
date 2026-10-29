# Mobile engines: Chromium on Android, WebKit extensions on iOS

Status (2026-10-29): research and plan only. Nothing here is built yet, and the Chromium work waits for the user's OK.

The ask: the mobile apps should do everything desktop Mica does, including Chrome extensions: the built-in uBlock
Origin Lite and Mica blocker, installs from the Chrome Web Store, and Claude for Chrome and ChatGPT for Chrome in the
AI panel. On Windows those two need `debugger`, `tabs`, `tabGroups`, `sidePanel` and (for the desktop apps' bridges)
`nativeMessaging`; `src/BrowserClaudeExtension.cs` shows how Mica hosts them.

## Android

### Where upstream Chromium is

Chromium has a second Android build, "desktop Android" (`is_desktop_android = true`), meant for Android laptops. It's
the same `chrome/android` app with extensions compiled in. Checked against Chromium's `main` today:

- `extensions/docs/buildflags.md`: `ENABLE_EXTENSIONS_CORE` covers Windows, Mac, Linux, ChromeOS and desktop Android;
  new extension code is written against it. Desktop Android is MV3 only.
- `chrome/browser/extensions/api/BUILD.gn`, group "APIs supported on Win/Mac/Linux and desktop Android", includes
  **debugger, side_panel, tab_groups, context_menus, commands, history, web_navigation, tab_capture, page_capture,
  runtime, extension_action, notifications** and more. `tabs`/`windows` have Android platform delegates, and
  `webstore_private` (the "Add to Chrome" button) is built. `declarativeNetRequest` and `scripting` live in
  `//extensions` and work everywhere.
- `_permission_features.json` lists `sidePanel` for `desktop_android`; `debugger`, `tabs` and `tabGroups` have no
  platform limit. What's left out on desktop Android is mostly Chrome-internal (`*Private`, PDF viewer, settings
  overrides).
- `nativeMessaging` has no use on a phone anyway: there's no Claude Desktop or ChatGPT desktop app on the other end.
  Claude for Chrome works without it; ChatGPT for Chrome on Windows gets its browser control from the ChatGPT desktop
  app, so on Android it would be chat only.
- Google publishes nightly builds: `chromium-browser-snapshots/AndroidDesktop_arm64/` (build 1730199 today,
  `chrome-android-desktop.zip`, 467 MB). Reported state: Web Store installs work (on a phone only with "Smallest
  width" ≥ 600 or desktop mode), extensions persist, some popups don't scale and the extensions menu is rough.
- Regular Chrome for phones has no extensions. Vivaldi 8.2 (September 2026) shipped Web Store extensions on Android
  phones, so a Chromium fork can do it today. Anthropic says Claude for Chrome isn't supported on mobile or on other
  Chromium browsers; nobody has shown it working on Android yet.

### What a Mica build on Chromium takes

**Shape.** It has to be a fork of Chromium's own Android app (`chrome/android`, "ChromePublic") built as desktop
Android, with Mica's UI put on top. The content layer alone (or the old WebLayer, removed in 2024) has no extensions:
the extension system depends on `//chrome`. Igalia is moving it to `//extensions` for embedders (Web Store installs
landed in M152), but that runs on Linux only and still leans on `//chrome` for permissions and cookies. It isn't
usable on Android yet. Brave, Vivaldi, Cromite, Vanadium and Kiwi all forked `chrome/android` the same way.

**What carries over from today's Android app.** The Kotlin UI and features (`android/app`) don't drop in: the
Chromium app is about 1 million lines of Java with its own toolbar, tab model and settings. Mica's island,
spotlight, rail, deck, vault, sync and reader would be rewritten as patches to that UI or as new Java classes in it.
The shared HTML pages, scripts, blocker lists and strings carry over. This is the biggest cost: the port took weeks
on a WebView app, and on Chromium it's a larger rewrite.

**Machine.** Chromium for Android builds only on x86-64 Linux:

| | Needs | This PC (i7-10510U, 4 cores, 31 GB, ~70 GB free) |
|---|---|---|
| Disk | ~30 GB git cache + source + one Android output dir: plan for 250 GB SSD | No |
| RAM | 16 GB minimum, 32–64 GB to link without swapping | Borderline |
| CPU | First build ~2–3 h on 32 cores, ~5–6 h on 16 (estimate); after a rebase, 0.5–1.5 h | ~15–20 h |

GitHub-hosted runners don't fit: 4 vCPU, 16 GB, ~14 GB free disk (about 70 GB after deleting preinstalled tools)
and a 6-hour job limit. Larger runners need a paid org plan. Options:

- **A rented Linux box, kept** (recommended): for example a dedicated server with 16 cores, 128 GB and 2 TB NVMe,
  around €100–130 a month. It keeps the checkout and build cache warm, so rebases are quick. It can also be a GitHub
  self-hosted runner for the private repo.
- **A cloud VM on demand**: 32 vCPU and 256 GB disk, started only for builds, a few dollars per build on spot pricing
  plus disk storage. It's cheaper if builds are rare, but the cache must live on a kept disk.

**Size.** ChromePublic for arm64 is about 120–180 MB installed, against a few MB for the WebView app. The app can't
share the system's Trichrome library (that's signed by Google), so the engine is its own copy.

**Memory and battery.** The WebView app shares the system's WebView, which is often already in memory. Our own
Chromium is a full browser: expect about Chrome's numbers (a browser process plus GPU plus renderers), likely
+100–200 MB at rest compared with today. Each extension's service worker also costs memory and wakes up. The
desktop-style tricks (freezing, discarding, purging) are built into Chromium and become easier to use.

**Keeping it patched.** Chromium stable ships every 4 weeks, and security fixes every week. A fork has to rebase
its patches on every stable release and rebuild for the weekly fixes. Forks like Cromite and Vanadium do this within
days, with a patch stack kept small on purpose. For Mica: roughly 1–3 days of work per 4-week rebase while the patch
stack is large, plus a build for each security update. Shipping an engine that falls behind is a real security risk,
so this cost is permanent.

**Store.** Play allows apps with their own engine (Vivaldi, Brave, Edge). The app id and signing key carry over, so
it can update the current Mica install.

### Alternatives

| | Extensions | Claude / ChatGPT for Chrome | Web Store | Cost | Memory |
|---|---|---|---|---|---|
| **Chromium fork (desktop Android)** | Chrome MV3, almost all of desktop's APIs | Likely; needs testing (it says mobile is unsupported, and may check the platform) | Yes | Highest: build box, UI rewrite, rebase every 4 weeks | Higher |
| **GeckoView** (Firefox's engine, prebuilt from Mozilla's Maven) | WebExtensions; full uBlock Origin (not just Lite) runs | No: no `chrome.debugger`, no side panel API on Android, neither extension exists for Firefox | No; addons.mozilla.org instead | Medium: no engine build; UI rewrite onto GeckoView; Mozilla ships every 4 weeks | Similar to Chromium |
| **System WebView (today)** | None; Mica's own blocker | No (the AI panel is the chat site) | No | Lowest | Lowest |

## iOS

Third-party engines are allowed only in the EU, with Apple's BrowserEngineKit entitlement, which needs the paid
developer account the user doesn't have. So iOS stays on WebKit.

### What WebKit's extensions can do

`WKWebExtension`, `WKWebExtensionContext` and `WKWebExtensionController` (iOS 18.4+) run Safari-style web extensions
inside Mica's own `WKWebView`s. WebKit's source (`Source/WebKit/UIProcess/Extensions/Cocoa/API`) implements action,
commands, cookies, declarativeNetRequest, menus, notifications, offscreen, permissions, runtime, scripting, storage,
tabs, webNavigation and windows. The app supplies the tabs and windows through the controller's delegate.
In `PlatformEnableCocoa.h`, **sidebar** (`WK_WEB_EXTENSIONS_SIDEBAR`) and **bookmarks** are compiled off. There's no
`debugger`, `tabGroups` or `nativeMessaging` (to another app), and `webRequest` can only observe, not block.

- **uBlock Origin Lite** is MV3 and declarativeNetRequest only, and it's already on the App Store for Safari (iOS 18,
  after Apple fixed declarativeNetRequest in 18.6). Its extension should load in Mica through `WKWebExtension`. Mica
  would bundle uBOL's GPLv3 code, which works for a separate bundled component, but needs its licence text and source
  link.
- **Mica blocker** already runs as a `WKContentRuleList` (same lists, compiled once). It can stay that way, which
  is cheaper than an extension.
- **Web Store installs**: technically a CRX can be downloaded, its header stripped, and the folder loaded.
  Chrome-only APIs then fail. Orion (Kagi) ships Chrome and Firefox extension installs on the iOS App Store, so
  Apple has allowed it, but review risk stays (guideline 2.5.2).

### Desktop vs iOS

| Desktop feature | iOS | Why |
|---|---|---|
| uBlock Origin Lite | Yes (iOS 18.4+) | MV3 declarativeNetRequest is supported |
| Mica blocker | Yes (already) | `WKContentRuleList` |
| Chrome Web Store installs | Partly | Simple extensions work; ones needing Chrome-only APIs don't |
| Extension popups and context menus | Yes | action and menus APIs |
| Extension side panel | No | WebKit's sidebar API is compiled off |
| Claude for Chrome / ChatGPT for Chrome | No | No `chrome.debugger`, no side panel; the AI panel stays the chat site |
| Tab groups API for extensions | No | Not in WebKit |
| Blocked-request counts | No | WebKit blocks inside its process |
| Default browser | Needs entitlement | Apple's browser entitlement, developer account |

Raising the minimum from iOS 17 to 18.4 for extensions costs few users (18.4 shipped in March 2025). Alternatively,
keep 17 and turn extensions on only where available.

## Recommendation

1. **First, a one-day test with Google's prebuilt desktop-Android Chromium** on the Samsung: install uBOL, Mica
   blocker and Claude for Chrome from the Web Store, then check that Claude can read and click a page. That shows,
   before any money or months go in, whether the main reason for Chromium works on a phone. It needs the user's OK
   to download the 467 MB snapshot from `storage.googleapis.com/chromium-browser-snapshots`, and the user to sign in
   to Claude themselves.
2. **If it works: the Chromium fork for Android**, in stages:
   1. a Linux build box (below) and a reproducible build of stock desktop-Android Chromium with Mica's app id, name
      and icon, signed with the upload key, plus CI that rebuilds on each stable release;
   2. Mica's look (island, spotlight, rail, deck) as patches on the Chromium UI;
   3. features ported from the WebView app (vault, sync, reader, games, split, peek, sessions, jars);
   4. built-in uBOL and Mica blocker, and the AI panel hosting Claude's and ChatGPT's side panels.
   Rough size: months, not weeks, and then 1–3 days every 4 weeks for rebases. Keep the WebView app on Play until
   the Chromium one reaches parity.
   **If it doesn't work:** stay on WebView (or GeckoView if full uBlock Origin matters more than Web Store
   extensions), and the AI panel stays the chat site.
3. **iOS: add `WKWebExtension`** to the current app (iOS 18.4+): bundled uBOL, extension popups and menus, and a
   Web Store install that loads what WebKit can run. This is weeks of work, with no build machine needed (it
   builds on the existing macOS CI).

### What the user needs to provide

- The OK for step 1 (download and test the snapshot on the Samsung) and for step 2.
- For step 2, a Linux build machine: a rented dedicated server (about €100–130 a month) or a cloud account for
  on-demand VMs. This PC can't do it (4 cores, ~70 GB free disk).
- Nothing new for iOS. A paid Apple account would still be needed to ship either app to a store.
