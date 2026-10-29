# Mica cross-platform shell (macOS, Linux)

Mica's shell for macOS and Linux: Avalonia UI + Chromium 156 (CEF 156.0.14) through CefGlue, in Chrome style, so Chrome extensions run. The plan behind it is in `docs/porting-mac-linux.md`. Windows keeps shipping WPF + WebView2 from `build.ps1`. This folder only builds on Windows for testing.

## What works

| Feature | Windows (tested) | macOS / Linux |
|---|---|---|
| Tabs, address bar (Mica's own rules from `Core.cs`), back/forward/reload, light/dark | ✅ | not run yet |
| Built-in games on `games.mica.invalid` / `tetris.mica.invalid`, offline notice with *Play games* | ✅ | not run yet |
| Session saved through the shared `src/Core.cs`, same `session.json` format as Windows | ✅ | not run yet |
| Built-in ad blocking (uBlock Origin Lite + Mica blocker) | ✅ blocks a Google ad script | not run yet |
| **Claude for Chrome** in the AI panel (✦ button, Ctrl/Cmd+Shift+Space): installed from the Chrome Web Store, shown as its side panel, bound to the tab in front, reaches pages through `chrome.debugger` | ✅ up to sign-in (not signed in) | not run yet |
| Links opening new windows (target=_blank, window.open, Claude's tabs) open as Mica tabs | written, not exercised by the test | not run yet |
| Engine updates: newer CEF 156 builds from the CEF CDN, verified, used from the next launch, with fallback | ✅ chooser, install, fallback (`--engine-self-test`; no real download run) | not run yet; Mac app bundles need an opt-in, see below |

Shortcuts: Ctrl/Cmd+T, W, L, R, F5, Alt+←/→, Ctrl/Cmd+Shift+Space. They work while focus is in Mica's toolbar, not yet inside a page.

## Run the test on a Mac (or Linux)

1. Copy the project to the Mac's own disk. macOS can only read NTFS, so the build can't write into `D:\project\browser`. Only `cross`, `src` and `extensions` are needed, without the Windows build output. In Terminal, with the Windows partition mounted (its name under `/Volumes` may differ):

   ```bash
   mkdir -p ~/mica && for d in cross src extensions; do rsync -a --exclude bin --exclude obj /Volumes/D/project/browser/$d ~/mica/; done
   ```

   Then `cd ~/mica`.
2. Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0 (Apple Silicon: Arm64 installer).
3. In Terminal, from the copied folder:

```bash
chmod +x cross/test.sh cross/package.sh
```

```bash
./cross/test.sh --claude
```

The first build downloads Chromium for your Mac (CEF's minimal distribution, about 135 MB, unpacked into `cross/cef/`) and builds CefGlue, so it takes a few minutes. The engine self-test runs first, without a window (`cross/out/engine-self-test.txt`). Then the test window opens off-screen. The results print at the end and are kept in `cross/out/self-test.txt`, with the engine log in `cross/out/test-profile/engine.log`. Leave out `--claude` to skip installing Claude for Chrome.

To use it normally, run:

```bash
dotnet run --project cross/Mica.Cross.csproj
```

Your profile goes to `~/Library/Application Support/Mica`. To try Claude, click ✦ → *Get Claude for Chrome* → *Restart Mica* → sign in inside the panel.

## Files

- `Mica.Cross.csproj`: links `../src/Core.cs`, `ExtensionPackage.cs` (verified Chrome Web Store installs) and `AiExtensions.cs` (the AI extension list and side-panel shim), embeds the game pages, and copies `../extensions`. Nothing is copied by hand.
- `vendor/CefGlue/`: CefGlue built from source, its interop regenerated for CEF 156 (it started from draft OutSystems/CefGlue#208 on CEF 134). See `VENDOR.md`.
- `cef.lock`, `fetch-cef.ps1`, `fetch-cef.sh`: the CEF build the shell ships with, downloaded from Spotify's CEF build CDN (`cef-builds.spotifycdn.com`) and checked against the SHA-1 in `cef.lock`. The build runs the right script when `cross/cef/<rid>/` is missing or older. NuGet's `cef.redist.*` packages stop at 134, so they aren't used any more.
- `src/CefEngine.cs`, `src/CefEngineUpdater.cs`, `src/CefEngineSelfTest.cs`: which engine runs, the daily engine update, and `--engine-self-test` (see *Engine updates*).
- `src/JsonShim.cs`: stands in for `JavaScriptSerializer`, so the shared files compile unchanged.
- `src/BrowserWindow.cs`, `src/AiPanel.cs`, `src/Extensions.cs`, `src/BuiltInPages.cs`: the window, the Claude panel, extension loading, the games and new tab hosts.
- `src/Theme.axaml`, `src/Home.html`: Mica's look, matching the Windows build: its colors (light and dark), the sidebar with vertical tabs, the page in a rounded frame, and the new tab page.
- `src/SelfTest.cs`: `--self-test`. `src/Preview.cs`: `--preview <folder>` saves screenshots of a few scenes from the off-screen window. `test.sh`: build and test on macOS/Linux (`test.ps1` on Windows). `package.sh`: packages.

Profile folder: `%LOCALAPPDATA%\MicaCross` (Windows, kept apart from the real Mica profile), `~/Library/Application Support/Mica` (macOS), `~/.local/share/Mica` (Linux). `--profile <dir>` overrides it. `--install-extension <store id or URL>` installs a Chrome Web Store extension before start.

## Engine updates

Like the Windows build's `EngineUpdater`, the shell keeps Chromium patched between app updates:

- Once a day (3 minutes after start, then while running; never during `--self-test`), `CefEngineUpdater` reads the CDN's `index.json`, only when it changed (ETag). When there is a newer **stable build of the same CEF major** (156), it downloads the minimal distribution over HTTPS, checks the SHA-1 the index lists, and unpacks it into the per-user `Engines` folder: `~/Library/Application Support/Mica/Engines` (macOS), `~/.local/share/Mica/Engines` (Linux), `%LOCALAPPDATA%\MicaCross\Engines` (Windows, development only). The app folder is never changed.
- New majors don't come this way: CefGlue's bindings are generated for one CEF API version (15600), so a new major needs regenerated bindings and ships with an app update.
- At the next launch `CefEngine` picks the newest engine that loads and reports CefGlue's API hash, and points CEF at it (libcef, resources, locales; the helper processes inherit it through `CEFGLUE_LIBCEF_DIR`). An engine that fails that check is set aside at once. One that twice didn't stay up for 20 seconds is set aside on the next launch. Either way the next one, and in the end the bundled engine, runs instead. Older and set-aside engines are deleted, and a set-aside version isn't downloaded again. `Engines/engines.log` records the decisions.
- `Mica --engine-self-test <file> [saved index.json]` checks all of this with fake engines, a tiny archive and a made-up index, without a 300 MB download.
- **macOS app bundles:** a signed, notarized `Mica.app` runs with the hardened runtime. Its library validation refuses a libcef that isn't signed with Mica's Team ID, and CDN builds never are. So inside an `.app` the updater stays off unless `Info.plist` has `<key>MicaEngineUpdates</key><true/>`. See *macOS signing* in `docs/porting-mac-linux.md` for the options and their trade-offs.
- **Linux:** the minimal distribution keeps debug symbols (`libcef.so` is about 1 GB unpacked). The updater and `fetch-cef.sh` strip it when `strip` (binutils) is installed.

## How extensions work here

- **Loading:** Chromium loads extensions itself, from `--load-extension` at start: the built-in blockers next to Mica, plus store extensions installed into the profile. CEF can't add one while running, so a new extension needs a restart (the panel offers it).
- **Claude for Chrome:** installed through Mica's own verified store download, unpacked, with Mica's shims added:
  - `mica-sidepanel.js` in its worker, shared with Windows;
  - `mica-panel.js` in `sidepanel.html`, because CEF can't inject scripts into every page the way WebView2 does.

  The panel finds the tab ID by asking `chrome.tabs`, since every Mica tab is a real Chrome tab here.

## Build on Windows

Needs the .NET 8 SDK:

```bash
dotnet build cross/Mica.Cross.csproj
```

The output lands in `cross/bin/x64/Debug/net8.0/win-x64/`. For a per-user SDK install, set `DOTNET_ROOT` before starting `Mica.exe`.

```bash
cross/bin/x64/Debug/net8.0/win-x64/Mica.exe --self-test out.txt --install-extension fcoeoabgfenejglbffodgkkbkcdhcgfn
```

## Packages (on the target OS)

```bash
./cross/package.sh macos
```

```bash
./cross/package.sh linux
```

- **macOS:** writes `cross/out/Mica.app` and `Mica-<version>-<arch>.dmg` for the Mac's own architecture (Apple Silicon is native now). `SIGN_ID="Developer ID Application: …"` signs with the hardened runtime. Then notarize with `xcrun notarytool submit … --wait` and `xcrun stapler staple`.
- **Linux:** writes `mica-<version>-linux-<arch>.tar.gz` with a `mica.desktop` (http/https handler), plus an AppImage when `appimagetool` is installed. It needs X11 or XWayland and the usual Chromium libraries (`libnss3`, `libatk-bridge2.0-0`, `libgbm1`, `libxkbcommon0`, `libasound2`).

Publishing for macOS/Linux also works from Windows (`dotnet publish -r osx-arm64 -p:Platform=ARM64`), but the files lose their executable bit. Build packages on the target OS.

## Known gaps

- **Not run on macOS or Linux yet.** The CEF 156 bindings have only run on Windows (the CefGlue branch's author reported macOS working on CEF 134). In Chrome style on macOS, Chromium is hosted in a native NSView. That's the part most likely to need work.
- **Engine updates on macOS** need a signing decision first (see *Engine updates*).
- **Not tested end to end:** signing in to Claude and Claude actually doing a task, and Claude Code's connection to the extension. Claude Code talks to it over native messaging, which it registers for Chrome and Edge, not Mica.
- **Missing from Windows Mica:** home page, history/downloads pages, settings, vault, and most menus.
- **No real sandbox:** CefGlue runs Chromium with `NoSandbox`. This must be fixed before release.
