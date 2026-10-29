# Mica on macOS and Linux: porting plan

Status (2026-10-29): the shell in `cross\` runs on **CEF 134 in Chrome style**, with CefGlue built from source in `cross\vendor\CefGlue`. On Windows it passes its self-test, including:
- the built-in ad blocker;
- **Claude for Chrome** in the AI panel: installed, bound to the active tab, and reaching the page through `chrome.debugger`. Sign-in was not tested.

It has not been run on macOS or Linux yet. `cross/test.sh` does that.

## Decision

- **Windows stays as it is**: WPF + WebView2, built by `build.ps1`. It is lighter than anything we could ship instead (WebView2 is shared with the OS, no bundled engine), and it is the product today.
- **macOS and Linux get a new shell** in `cross\`: Avalonia UI 11 + Chromium through CEF 134 (CefGlue, built from source), in Chrome style so Chrome extensions run. It is one code base for both, not one per OS.
- Both shells share engine-neutral code by **linking the same files** (`<Compile Include="..\src\Core.cs">`), not by copying them. A fix in `src\Core.cs` lands in both.

## Why Avalonia + CEF

| Option | For | Against |
|---|---|---|
| **Avalonia + CefGlue on CEF 134 (chosen)** | Real Chrome code (Chrome style), so pages, MV3 extensions (uBlock Origin Lite, Mica blocker, Claude for Chrome) and DevTools behave like Chrome. Same C#/.NET, a XAML-like UI. One shell for macOS and Linux. | Bundles Chromium (~300–450 MB unpacked per platform). No CefGlue *release* supports CEF 128+, so Mica builds the draft CEF 134 branch from source (see Risks). Chromium 134 is still behind current Chrome. |
| CefGlue 120 (latest release) | Released package | Alloy-only, so Chrome extensions don't load: no ad blocker, no Claude for Chrome. Ruled out. |
| System WebKit (WKWebView on macOS, WebKitGTK on Linux) | Very light: the engine comes with the OS and the OS updates it. Best for battery. | Not Chromium, so no Chrome extensions: the ad blocker would have to be rebuilt on WebKit content-rule lists, and Claude for Chrome wouldn't work. Two engine APIs to wrap (Cocoa and GTK). |
| WebView2 on macOS/Linux | Would be ideal | Microsoft closed both as "not planned" in July 2024 (MicrosoftEdge/WebView2Feedback#1314, #645). |
| Electron / Tauri | Mature packaging | Rewrites everything in JS/Rust. Tauri uses the system WebKit. Electron supports only part of the extension API (no `chrome.debugger`, which Claude needs). |

## What carries over unchanged

| Code | How |
|---|---|
| `src\Core.cs`: session model, tabs, spaces, groups, cookie jars, address-bar resolution (now with local files), search, history, closed tabs, tab resting rules | **Linked into `cross\`.** Its only non-portable dependency, `JavaScriptSerializer`, is provided by `cross\src\JsonShim.cs` (System.Text.Json). The shim writes dates as `\/Date(ms)\/`, so `session.json` reads the same on both. |
| `src\ExtensionPackage.cs`: verified Chrome Web Store download and unpack | **Linked into `cross\`**, unchanged. |
| `src\AiExtensions.cs`: the AI extensions Mica hosts and the `mica-sidepanel.js` worker shim | **Moved out of `BrowserClaudeExtension.cs`** (no code changes) so both builds compile it. `build.ps1` lists it. |
| Game pages (`GameHub`, `Tetris`, `Snake`, `2048`, ...: 12 HTML files) | **Embedded**, same resource names (`Mica.<file>`), served on the same hosts `tetris.mica.invalid` / `games.mica.invalid` with the same headers and CSP, dark theme applied the same way. |
| `extensions\` (uBlock Origin Lite, Mica blocker) | **Loaded in `cross\`** via `--load-extension`. On Windows the self-test shows a Google ad script being blocked. |
| Page scripts: `Reader.js`, `Ambient.js`, `StoreBridge.js` | Plain JS injected into pages. Engine-neutral; need only the injection call (CEF: `ExecuteJavaScript` / a render-process handler). Not ported yet. |
| `Totp.cs`, `ReleaseKey.cs`, `DownloadModel.cs`, `AppUpdater.cs` (update-manifest logic) | No WPF/WebView2/Win32 use; should link as-is (JSON via the shim). Not linked yet. |
| `VaultModel.cs` | Model is portable, but PBKDF2 goes through `bcrypt.dll` P/Invoke. Replace with `Rfc2898DeriveBytes.Pbkdf2` (same algorithm and output) behind a small `#if`/partial. |
| `Strings.vi.tsv` | Data is portable. `Localization.cs` binds to WPF and needs a small Avalonia twin. |

## Claude for Chrome in `cross\`

On Windows (WebView2), Mica hosts the extension's `sidepanel.html?tabId=N` in its AI panel, adds `mica-sidepanel.js` to its worker, and finds the tab ID through DevTools targets. In `cross\` (`src\AiPanel.cs`):
- **Install:** the same verified Web Store download (`ExtensionPackage`), unpacked into the profile and loaded with `--load-extension`. CEF has no API to add an extension while running, so the panel offers a restart.
- **Panel page shim:** CEF can't add a script to every page the way WebView2 does, so `sidepanel.html` loads `mica-panel.js` from the extension folder. It contains the same tabs shim and `getCurrent` fix as Windows.
- **Tab binding:** every Mica tab is a real Chrome tab here, so the panel asks `chrome.tabs` which tab shows the active page.
- **New windows:** windows the extension or a page opens become Mica tabs (`LifeSpanHandler.OnBeforePopup`), including `open.mica.invalid` requests from the shim.
- **ChatGPT for Chrome:** not wired yet. It also needs Windows' `FollowPanelPath` script.

## What needs a replacement per platform

| Windows piece | macOS | Linux |
|---|---|---|
| WPF windows, XAML (`Shell.xaml`, `ContextMenus.xaml`, `Library.xaml`, `Extensions.xaml`), 68 of 80 files | Avalonia (shared) | Avalonia (shared) |
| WebView2 (42 files: navigation, request filters, downloads, permissions, DevTools, extensions, memory saver) | CefGlue handlers: `RequestHandler`, `DownloadHandler`, `LifeSpanHandler` (popups), `JSDialogHandler`, `DisplayHandler`, scheme factories. Memory saver: there is no `TrySuspend` in CEF; use `WasHidden` + background throttling, and "rest" by disposing the view (Core's `TabLifecycle` already decides when). | same |
| Win32 P/Invoke (89 calls, 21 files): DWM corners/backdrop, taskbar/Jump Lists, AppUserModelID, window placement, foreground, power/memory status | Mostly Avalonia APIs (window chrome, `ExtendClientAreaToDecorationsHint`). Dock menu instead of Jump List (`NativeMenu`). Memory pressure: `sysctl`/`host_statistics`. | Avalonia/X11. `.desktop` actions instead of Jump Lists. Memory: `/proc/meminfo`. |
| DPAPI (`ProtectedData`) for vault and autofill keys | Keychain (`security` CLI or Security.framework P/Invoke) | libsecret / Secret Service (D-Bus via `Tmds.DBus`, already pulled in by Avalonia); fall back to a passphrase-only vault when no keyring is available |
| Windows Hello (`WindowsHello.cs`) | Touch ID through `LocalAuthentication` (LAContext) | none; passphrase only |
| Windows Sandbox (`BrowserSandbox.cs`, opens a file in `WindowsSandbox.exe`) | No equivalent. Hide the feature, or open in a throwaway macOS user/VM later | Optional: `firejail`/`bwrap` when installed; otherwise hidden |
| Installer (`installer\`), registry file associations, App Paths | `.app` bundle + `.dmg`, `Info.plist` URL types for http/https | `.deb`/`.rpm`/AppImage/Flatpak, `.desktop` file with `x-scheme-handler/http(s)` |
| `EngineUpdater.cs`, `BundledRuntime.cs` (fixed WebView2 runtime) | CEF ships inside the app; updates come with app updates | same |
| Signed updates (`AppUpdater.cs` + release key) | Same manifest and signature check; plus Apple notarization | Same; distro packages can rely on the repo's signature |

## Order of work

1. **Shell skeleton (done)**: Avalonia window, tabs, address bar, back/forward/reload, games hosts, session via `Core.cs`, off-screen `--self-test`.
2. **Engine with extensions (done on Windows)**: CEF 134 Chrome style loads uBO Lite, Mica blocker and Claude for Chrome with Mica's shims.
3. **Run it on real macOS and Linux**: `cross/test.sh --claude` (see `cross/README.md`). Fix what breaks: on macOS, Chrome-style browsers in an NSView and the app bundle layout; on Linux, X11/XWayland and the sandbox. Then sign in to Claude and run a real task on each OS.
4. **Get a current Chromium**: CEF 134 is from spring 2025. Move to an official CefGlue release on a current CEF when one ships, or regenerate CefGlue's interop for a current CEF ourselves (the generator and `CEF-UPGRADE.md` are in the CefGlue repo). *Nothing ships before this.*
5. **Port the everyday features**, highest value first: new-tab/home page, spotlight (Core already has the search), bookmarks/history/downloads library, downloads, permissions, find in page, zoom, keyboard shortcuts inside the page (CEF `KeyboardHandler`), context menus, extensions page, ChatGPT for Chrome.
6. **Efficiency features**: tab freezing/resting, memory saver, the perf probe and memory bench ported to measure them (Mica's goal: low RAM/CPU/GPU without stutter).
7. **Security features**: Chromium sandbox on, vault + autofill with Keychain/libsecret, Touch ID, tracking protection, signed updates.
8. **Packaging and CI**: `.app`/`.dmg` with signing + notarization, AppImage and `.deb`, a GitHub Actions matrix (macOS arm64 and x64, Ubuntu) running `cross/test.sh` (under `xvfb-run` on Linux).
9. **Polish**: native-feeling chrome per platform, import from other browsers (paths differ per OS), localization.

## Risks

- **Chromium age**: CEF 134 (Chromium 134) misses the security fixes since spring 2025. Fine for a preview, not for users.
- **Draft dependency**: `cross\vendor\CefGlue` is draft PR OutSystems/CefGlue#208, not a release. Its own review (`CEF134-PR208-REVIEW.md` there) lists open issues: off-screen rendering is untested, and macOS/Linux haven't been retested since its last fixes.
- **macOS**: CefGlue hosts Chromium in a native NSView (not off-screen). Whether Chrome-style browsers embed cleanly there is the main thing the first Mac run will show. Apple Silicon is native (CEF 134 has an arm64 package).
- **Sandbox**: CefGlue starts Chromium with `NoSandbox` on every OS. It must run sandboxed before release.
- **Claude Code integration**: Claude Code reaches the extension through native messaging, which it registers for Chrome and Edge only, so `/chrome` from Claude Code won't find Mica. The extension itself (signed in to claude.ai) doesn't need it.
- **Linux**: windowed CEF needs X11 (Wayland sessions go through XWayland).
- **Size**: about 390–440 MB unpacked per platform, compared with the WPF build that uses the shared WebView2.
