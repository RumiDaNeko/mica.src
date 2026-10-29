# Mica cross-platform shell (macOS, Linux)

Mica's shell for macOS and Linux: Avalonia UI + Chromium 134 through CefGlue, in Chrome style, so Chrome extensions run. The plan behind it is in `docs/porting-mac-linux.md`. Windows keeps shipping WPF + WebView2 from `build.ps1`. This folder only builds on Windows for testing.

## What works

| Feature | Windows (tested) | macOS / Linux |
|---|---|---|
| Tabs, address bar (Mica's own rules from `Core.cs`), back/forward/reload, light/dark | ✅ | not run yet |
| Built-in games on `games.mica.invalid` / `tetris.mica.invalid`, offline notice with *Play games* | ✅ | not run yet |
| Session saved through the shared `src/Core.cs`, same `session.json` format as Windows | ✅ | not run yet |
| Built-in ad blocking (uBlock Origin Lite + Mica blocker) | ✅ blocks a Google ad script | not run yet |
| **Claude for Chrome** in the AI panel (✦ button, Ctrl/Cmd+Shift+Space): installed from the Chrome Web Store, shown as its side panel, bound to the tab in front, reaches pages through `chrome.debugger` | ✅ up to sign-in (not signed in) | not run yet |
| Links opening new windows (target=_blank, window.open, Claude's tabs) open as Mica tabs | written, not exercised by the test | not run yet |
| Spotlight (Ctrl/Cmd+K, L): tabs, bookmarks, history, the web; `>` for the command palette; tab search (Ctrl+Shift+A) | ✅ | not run yet |
| The island: memory use, short messages, downloads in progress, what a background tab is playing (play/pause, mute); steps aside from the pointer | ✅ | not run yet |
| Sidebar like Windows: spaces (new, rename, remove, move a tab), pinned tabs, site icons, the slim rail (Ctrl/Cmd+Shift+B), tab menu (pin, mute, duplicate, copy, close others) | ✅ | not run yet |
| Menu, site card (zoom, memory saver per site, clear its cookies), extensions card (list, add from the Web Store, remove), Personalize (frame colour), task manager (Shift+Esc), clear browsing data, keyboard shortcuts | ✅ | not run yet |
| Bookmarks (Ctrl/Cmd+D), find in page (Ctrl/Cmd+F), zoom per site, print, DevTools (F12), reading mode (F9), page full screen, private window (Ctrl/Cmd+Shift+N) | ✅ | not run yet |
| History and Downloads pages (`library.mica.invalid`), downloads to the Downloads folder (`downloads.json`, shared format with Windows) | ✅ | not run yet |
| Error page ("A small detour."), resting background tabs (Core's TabLifecycle, never one that's playing) | ✅ | not run yet |

Shortcuts are the Windows set (Cmd for Ctrl on a Mac; Cmd+[ / ] for back and forward, Cmd+Y history, Cmd+Option+L downloads). They work inside pages too: Chromium hands keys to Mica first (`PageKeys`).

Also as on Windows: the AI side panel (ChatGPT, Claude, Gemini, Copilot, Perplexity or your own; with Claude for Chrome or ChatGPT for Chrome it uses the extension), Personalize (text size, home backgrounds and your photo, frosted glass, frame colour), profiles, and Vietnamese (Menu → Language, from `src/Strings.vi.tsv`).

Not in this shell yet: tab groups, split view, sign-in and sync, passwords, import, closing animations, YouTube ambient.

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

The first build downloads Chromium for your Mac (about 300 MB) and builds CefGlue, so it takes a few minutes. The test window opens off-screen. The results print at the end and are kept in `cross/out/self-test.txt`, with the engine log in `cross/out/test-profile/engine.log`. Leave out `--claude` to skip installing Claude for Chrome.

To use it normally, run:

```bash
dotnet run --project cross/Mica.Cross.csproj
```

Your profile goes to `~/Library/Application Support/Mica`. To try Claude, click ✦ → *Get Claude for Chrome* → *Restart Mica* → sign in inside the panel.

## Files

- `Mica.Cross.csproj`: links `../src/Core.cs`, `ExtensionPackage.cs` (verified Chrome Web Store installs) and `AiExtensions.cs` (the AI extension list and side-panel shim), embeds the game pages, and copies `../extensions`. Nothing is copied by hand.
- `vendor/CefGlue/`: CefGlue built from source on CEF 134 (draft OutSystems/CefGlue#208). See `VENDOR.md`.
- `src/JsonShim.cs`: stands in for `JavaScriptSerializer`, so the shared files compile unchanged.
- `src/BrowserWindow.cs`, `src/AiPanel.cs`, `src/Extensions.cs`, `src/BuiltInPages.cs`: the window, the AI side panel, extension loading, the games and new tab hosts.
- `src/Theme.axaml`, `src/Home.html`: Mica's look, matching the Windows build: its colors (light and dark), the sidebar with vertical tabs, the page in a rounded frame, and the new tab page.
- `src/Commands.cs` (Spotlight, palette, menus, keys), `src/PageTools.cs` (find, zoom, bookmarks, error page, site icons, resting), `src/Library.cs` + `src/Library.html` (downloads, History and Downloads pages), `src/Extras.cs` (reading mode, extensions, site card, private window, Personalize, task manager), `src/Island.cs` + `src/Ticker.cs` (the island and its rolling numbers), `src/Personalize.cs`, `src/Account.cs` (account card, profiles, language), `src/Dialogs.cs` (Mica's dialogs), `src/Localization.cs` (Vietnamese), `src/Corners.cs` (rounded page corners).
- `src/SelfTest.cs`, `src/SelfTestFeatures.cs`: `--self-test`. `src/Preview.cs`: `--preview <folder>` saves screenshots of a few scenes from the off-screen window. `test.sh`: build and test on macOS/Linux. `package.sh`: packages.

Profile folder: `%LOCALAPPDATA%\MicaCross` (Windows, kept apart from the real Mica profile), `~/Library/Application Support/Mica` (macOS), `~/.local/share/Mica` (Linux). `--profile <dir>` overrides it. `--install-extension <store id or URL>` installs a Chrome Web Store extension before start.

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

- **Not run on macOS or Linux yet.** The CefGlue branch's author reports macOS working on CEF 134, but nobody has retested since its last fixes. In Chrome style on macOS, Chromium is hosted in a native NSView. That's the part most likely to need work.
- **Old Chromium.** CEF 134 is Chromium 134 (spring 2025), still well behind current Chrome. It's fine for testing, but newer CEF is needed before real users.
- **Not tested end to end:** signing in to Claude and Claude actually doing a task, and Claude Code's connection to the extension. Claude Code talks to it over native messaging, which it registers for Chrome and Edge, not Mica.
- **Missing from Windows Mica:** home page, history/downloads pages, settings, vault, and most menus.
- **No real sandbox:** CefGlue runs Chromium with `NoSandbox`. This must be fixed before release.
