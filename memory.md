# Mica project memory

Updated: 2026-10-24. Workspace: `D:\project\browser`.

## User intent and preferences

- Never use stock Windows MessageBox UI for Mica-owned help, notices or confirmations. User explicitly rejected the old information-icon + plain text + OK shortcut dialog. Use themed Mica surfaces, readable grouping, keycaps, custom close controls and themed scrolling. Preserve explicit confirmation for destructive actions, with Cancel as the default.

- Build a real Windows browser inspired by Arc: polished, restrained purple/cream UI, Spotlight, smooth animation and low memory use.
- User explicitly wants all three engine goals: broad Chrome-extension compatibility, lower RAM/smoother operation, and independence from Edge WebView2. None may be silently dropped.
- The original 1.1 MB per live tab request is not achieved. Do not fabricate RAM numbers or promise an engine switch will achieve it.
- Communicate in concise, informal Vietnamese. Carry out authorized fixes without repeatedly asking for confirmation.
- Keep only the sidebar collapse toggle, never duplicate it on the toolbar. Collapsed sidebar remains a usable mini rail with search and favicon tab switching.
- Remove the old GH/video/mail sidebar cluster and ambiguous Home/Work buttons. Spaces is an animated dropdown.
- Keep Menu/theme visible when maximized or when many tabs are pinned. Animate reload only while the active page is loading; respect Windows reduced-motion settings.
- Use website favicons, including the four Home shortcuts. Avoid generic letters/video/mail symbols for known website shortcuts.
- History and Downloads have dedicated native pages; keep the browser menu compact.

## Current implementation

### 0.7 additions (2026-10-24)

- User requested history-based Home, password/passkey manager, Discord login + Node.js sync at `the-api.lessnoise.online` (user deploys), incognito, Windows taskbar Jump List matching screenshot, Edge-like maximize animation.
- Home now ranks actual history by visit count aggregated by origin, then recency. Empty history has no fixed promotional cards. Jump List uses the same Top Sites, eight Recently closed entries and new/private window tasks. Windows privacy settings can suppress custom categories; user-removed items remain removed.
- `BrowserLaunch.cs`: same-user restricted named-pipe launch forwarding, multiple normal windows sharing history/bookmarks/cookies/download records, independent tabs. Normal snapshots aggregate open windows. Ctrl+N opens a window, Ctrl+Shift+N opens a separate InPrivate window.
- Private windows use real WebView2 InPrivate controllers and separate temporary user-data directories. No app history/session/download-record persistence or favicon disk writes; vault/sync/extensions disabled. PRIVATE badge stays visible in the address bar. Files explicitly downloaded remain. Foreground cleanup thread retries engine-folder deletion even after the final window closes; failed/crashed leftovers retry on next launch.
- `BrowserHome.cs`: dynamic Home and native Jump List; `BrowserWindow.cs` restores WS_CAPTION/THICKFRAME/MINIMIZEBOX/MAXIMIZEBOX styles so Windows/DWM owns transitions. Native geometry/style regressions pass; exact visual parity with Edge is not verified.
- `BrowserVault.cs` / `VaultModel.cs`: manual encrypted password CRUD/copy/fill, explicit top-level HTTPS-origin matching, no automatic capture or submission. Separate master password; close dialog to lock. PBKDF2-SHA256 600k, AES256CBC + HMACSHA256 (metadata authenticated before decryption). Account token uses DPAPI. Only one vault dialog at a time across windows.
- `server/`: Node24 builtin HTTP/SQLite, no runtime npm dependencies; Discord OAuth identify, one-use state and client possession proof, hashed 30-day sessions, revocation, account-isolated opaque vault records, compare-and-swap versions, rate/body limits. Includes Docker, nginx and env example. User deploys; do not claim domain/live Discord configured.
- Sync is manual Pull/Push, with explicit snapshot capture/restore for tabs/bookmarks/history/theme/Space. It does NOT sync cookies, website sessions, download files/metadata or extension packages. Pull replacement is confirmed; no automatic merge.
- Passkeys remain Windows-managed. Opening Windows passkey settings is implemented; a Mica passkey provider and passkey cloud sync are NOT implemented. Never present ordinary encrypted JSON password storage as a passkey authenticator.
- Discord redirect: `https://the-api.lessnoise.online/v1/auth/discord/callback`. Client ID/secret belong only in server `.env`; do not ask user to post secrets in chat. Master password is not recoverable via Discord.
- Tests added: `--privacy-test`, `--private --private-exit-test`, `--vault-test`, `tests/vault-test.ps1`, `node --test server/test/api.test.mjs`. Native vault test uses fixture passwords, intercepted local HTTPS forms and a loopback Node server with mocked Discord; no real credentials/domain calls.

- C# 5 / .NET Framework 4.8, WPF native shell, Microsoft Edge WebView2 SDK 1.0.4258.31. No engine migration has been completed.
- Current feature release: 0.9.2. Previous releases 0.6 and 0.6.1 remain intact. See README and release notes for verified deliverables.
- Build: `build.ps1 -OutputDirectory <folder>`, using the Windows Framework compiler; dependencies under `.deps/webview2`.
- No Git repository or .NET SDK was available during the previous implementation. Check current tooling before assuming it exists.
- `run.cmd` selects the newest packaged version. Keep old releases intact; package a tested binary plus adjacent WebView DLLs/config.
- `src/BrowserApp.cs`: lifecycle/navigation/keyboard; `Shell.xaml`: native chrome/Home.
- `BrowserIcons.cs`: real tab favicons, bounded 32px cache, embedded official Home favicons. Assets and source URLs: `src/Assets/Favicons/`.
- `BrowserMotion.cs`, `BrowserWindow.cs`: motion and native window geometry. WM_GETMINMAXINFO must not suppress WPF minimum-size handling.
- `BrowserContextMenus.cs`: styled native tab/web context menus. Complete native menu deferrals exactly once before disposing a view.
- `BrowserLibrary.cs`/`Library.xaml`: `mica://history` (Ctrl+H), `mica://downloads` (Ctrl+J); no extra WebView just to show a library page.
- `BrowserDownloads.cs`/`DownloadModel.cs`: true transfers, pause/resume/cancel, file actions, record persistence, retain source controller while downloading. Detached pages cannot start new downloads/popups. Clear/remove records never deletes files.
- Downloads survive source-tab closure, not app shutdown. On restart, unfinished entries become Interrupted; retry reopens the source URL. Up to 200 finished records with roughly 1 MiB metadata budget; live transfers never pruned.
- History stores the latest 500 unique URLs. Spaces share cookies/profile/extensions; they are not privacy-isolated profiles.
- Extensions now install from pasted Chrome Web Store URLs via verified CRX3 download, permission review and WebView2 unpacked loading. Native website Add to Chrome, full Chrome API/toolbar popup compatibility and auto-updates are NOT implemented. Built-in clipboard/PDF components are hidden, not removed. User-installed list supports search, enabled-state filters, icons and toggles. Store packages live in profile/StoreExtensions/package-<GUID>; only validated managed paths may be removed. Never delete local unpacked sources.
- Existing navigation fixes distinguish HTTP 2xx pages that call window.stop from real network failures. Never suppress certificate errors.

## Engine direction: researched, not yet selected or migrated

All three user priorities above are requirements. A full Chromium-based browser is the leading architectural candidate for preserving Chromium's browser/extension infrastructure and controlling the shipped engine. This is an engineering recommendation, not a measured performance result or a user-approved specific technology choice.

CEF is an alternative for embedding an independently shipped Chromium runtime, but must be tested for extension APIs, browser UI integration and memory before calling it sufficient. Electron explicitly supports only a subset of Chrome extension APIs and does not aim for complete compatibility; do not present it as the solution to full extensions.

Local feasibility check on 2026-10-24: D: had about 6.3 GiB free. Chromium's official Windows build instructions require at least 100 GB free; Visual Studio installer discovery path was absent, and git/cmake/ninja were not found on PATH. A full Chromium build cannot proceed in this workspace as currently provisioned. Do not delete user data or install a large toolchain implicitly to overcome this.

Next engine work: provision sufficient build storage/toolchain, choose a maintained Chromium baseline, prototype custom browser chrome and extension actions, then compare the same sites and tab counts against WebView2. Verify startup, total process memory, idle CPU, sleep/wake, Gmail login, downloads, popup lifecycle, extension service workers and permissions. Preserve current session/history/bookmarks via an explicit migration. Owning Chromium also requires a security-update distribution process.

Primary references checked 2026-10-24:
- Chromium Windows build: https://chromium.googlesource.com/chromium/src/+/HEAD/docs/windows_build_instructions.md
- Chromium source workflow: https://www.chromium.org/developers/how-tos/get-the-code/
- CEF overview: https://github.com/chromiumembedded/cef
- Electron limitations: https://www.electronjs.org/docs/latest/api/extensions/

## Validation and data safety

- Real profile: `%LOCALAPPDATA%\MicaBrowser`. Do not modify it during tests or close the user's running browser.
- Test modes use isolated `artifacts/smoke-*` profiles and a shared `MicaBrowserSmoke` mutex. Run native suites sequentially, never concurrently.
- Native WebView tests need execution outside the restricted sandbox in this environment. Use hidden Start-Process, wait for exit, inspect current output and exit code. Sandbox-only networking can fail independently of the app.
- `test.ps1`: core logic. Native flags: `--smoke-test`, `--regression-test`, `--library-test`, `--downloads-test`, `--extensions-test`, `--sites-test`.
- Baseline 0.6: 31 core, 37 native regression, 11 library and 11 real download checks passed, plus full smoke. Read `docs/release-0.6.md` for scope and limitations.
- Inspect PNG captures for visual changes. WPF RenderTargetBitmap may omit native web HWND content; native Home/library screenshots are reliable.
- Match release executable SHA256 with the tested binary before packaging. Record new validation evidence rather than inheriting old results as current tests.

Update this file when requirements, architecture, release status or known limitations change. Keep proposals distinct from implemented and verified behavior.


## 0.8 verification (2026-10-24)

- 36 package checks passed: Store URL parsing, developer RSA signature/ID, tamper rejection, ZIP boundaries/traversal, key preservation and cleanup.
- Nine native extension checks passed: runtime hiding without disabling, manager lifecycle, search/filter, real MV3 content script install/disable/re-enable/remove.
- Four real Store checks passed: official Dark Reader download, review/cancel cleanup, confirmed installation with original ID, actual stylesheet injection, removal and managed-folder cleanup with runtime components preserved.
- Browser smoke passed on final binary; inspected light/dark manager and permission review screenshots.
- New files: src/Extensions.xaml, src/ExtensionPackage.cs, tests/ExtensionPackageTests.cs, tests/StoreRegression.cs. Native flag --store-test downloads actual Dark Reader in an isolated profile.
- CRX verification binds the developer RSA signature to requested Store ID. It does not assert a pinned Google publisher signature. Downloads are restricted to the official HTTPS endpoint and permitted Google content redirects. ECDSA-only developer packages are rejected.

## 0.8.1 dialog update

- src/BrowserDialogs.cs replaces shortcut MessageBox with a singleton owned, draggable, frameless themed window, grouped keycaps, scrolling, Escape/Done/close, and arrival motion. Two vault replacement confirmations use the same surface with Cancel default. No MessageBox calls remain under src.
- Native --dialogs-test passed: singleton/close cleanup, light/dark rendering, accept/cancel decisions and safe default. Inspected screenshots after fixing close padding and native scrollbar styling.

## 0.8.2 pinned shortcut fix

- Ctrl+W must consume the shortcut without closing a pinned tab. Explicit close controls remain available. The shared HandleKey path covers shell and WebView accelerator handling. Context menu omits the Ctrl W hint on pinned tabs; shortcut reference says Close unpinned tab.
- --pinned-test reproduced the sole-pinned-tab failure before the fix. After the fix all three checks passed: pinned tab preserved, normal tab closes, unpin restores closure and last-tab Home fallback.

## 0.8.3 vault design

- User rejected plain square vault fields, native titlebar and icon-poor vertical action list. Apply rounded themed window/fields/cards and clear action icons to Mica-owned tools.
- Vault uses the shared frameless dialog with Passwords and Sync & account tabs. Passwords has master unlock, saved login list and credential editor; sync groups account, encrypted transfers, browser snapshot and Windows passkeys. Icons accompany labeled commands. Credential list shows origin/username only; passwords remain masked.
- src/VaultLayout.cs arranges existing controls without changing encryption, network or confirmation logic. tests/VaultUiRegression.cs locates nested controls via logical tree and stable command tags. Five vault checks passed on final executable: encrypted save/reopen, matching-origin fill/no submission, local mocked OAuth and encrypted Node roundtrip, stale-write rejection/revocation. Inspected locked, saved light/dark and sync screenshots.

## 0.8.4 branding

- User selected Google Fonts Caveat for Mica text and requested exact M. logo. Sidebar wordmark is Mica. in Caveat Regular; executable/window icon renders M. on rounded purple tile. Keep general UI type unchanged.
- src/Assets/Brand contains original Caveat.ttf, OFL.txt, icon PNG/ICO and source notes. build.ps1 embeds font into Mica.g.resources and ICO as Win32/managed resources; includes Caveat-OFL.txt in release. No system font installation required.
- --brand-test confirmed GlyphTypeface resolves embedded caveat.ttf without fallback and window icon loads. Inspected wordmark and logo captures.

## Cloudflare Workers backend (2026-10-24)

- server/worker is the Workers + D1 alternative; desktop API/domain and vault envelope unchanged. Node/Docker backend retained. No cloud deployment or real credentials used.
- Wrangler config uses the-api.lessnoise.online Custom Domain, DB binding, 2026-10-19 compatibility, nodejs_compat, disabled observability/preview/workers.dev, 15-minute cleanup cron. User must create D1, enter database_id, deploy and set DISCORD_CLIENT_ID/SECRET.
- Shared D1 rate limits; atomic OAuth state claims, polling throttle and one-use token batch; atomic compare-and-swap vault SQL. No memory-only auth state or filesystem persistence.
- Six tests passed with local D1 including full module under workerd and large envelope roundtrip. Wrangler dry-run bundled successfully. See server/worker/README.md.

## 0.9 portable engine

- User requested bundled engine so Edge/WebView2 need not be separately installed. release-v0.9 includes all 257 files of official Microsoft Fixed Version 156.0.4314.42 x64 under Runtime (about 673.5 MiB). This is still WebView2, not an engine migration. Windows x64 + .NET Framework 4.8 remain prerequisites.
- BundledRuntime.Folder selects local runtime and prepares required AppContainer read/execute ACLs on runtime binaries only. bundled-engine.json marks portable releases; missing/incomplete runtime errors instead of falling back. Unbundled developer builds can use Evergreen. Extension Store requests derive version from actual selected environment.
- package-portable.ps1 verifies pinned CAB SHA256 and Microsoft executable Authenticode before packaging. No system engine copied or uninstalled. Preserve full runtime and notices. Fixed Version security updates must be distributed with future app versions.
- Verified actual browser process executable under release-v0.9/Runtime, navigation/JS, full browser smoke, nine extension tests, seven privacy/Home/multiwindow tests. Initial cold-start test exceeded a 15-second timeout; subsequent runtime verification passed, with a 45-second allowance in the final suite. No engine-free clean Windows VM available; do not claim that separate test.

## 0.9.1 compact alignment

- Collapsed sidebar uses symmetric 12px margins and centers ToggleSidebar; expanded mode restores original margins/right alignment. Menu and search centers now match sidebar midpoint within 0.5px.
- All 39 native regression checks passed on release-v0.9.1 with bundled runtime.

## 0.9.2 private theme

- Private windows inherit only Dark from an open normal window (or saved normal session when launched alone), never its history/bookmarks. Theme toggles synchronize open windows; private Save remains a no-op. Applying main theme also updates private windows.
- Eight privacy/Home/window tests passed, including new dark inheritance/live toggles/no-private-session assertions; bundled runtime process test passed. release-v0.9.2/Runtime uses hardlinks to unchanged release-v0.9 runtime files to save local disk space; do not mutate engine files in place.


## 0.10 vault unlock popup and Windows Hello

- User wants unlock as a popup upon entering Passwords, not an inline card. Themed modal-owned popup opens automatically; cancel closes the locked vault page. Master field stays in memory only for the open vault session and is cleared on close.
- Optional Hello enrollment requires correct master password plus native UserConsentVerifier verification. Later unlock re-verifies Hello before reading profile-bound CurrentUser DPAPI enrollment. Never sync hello-unlock.bin or master password. Forget enrollment and master fallback remain available.
- This is app-enforced Hello reauthentication over DPAPI, NOT hardware/TPM-bound encryption; another process running as the Windows user may decrypt DPAPI. Native interop requires supported Windows (Windows 11 desktop API); unsupported/unconfigured systems fall back to master password.
- Ten vault checks passed: automatic popup/cancel, protected enrollment/wrong password/denied Hello, successful simulated Hello, close-during-verification, unavailable Hello/forget/fallback, plus five existing save/fill/sync checks. Real native availability returned false on test machine; biometric/PIN interaction not exercised.
- release-v0.10 includes unchanged bundled Runtime via hardlinks to prior release to avoid duplicate disk use. Do not edit linked engine binaries in place. run.cmd selects 0.10.


## 0.10.1 automatic pull after Discord sign-in

- User requests automatic download after Discord login. Successful OAuth polling now calls a shared completion handler that saves the session then verifies account and downloads the encrypted vault automatically. Manual Pull retries the same path. No automatic upload.
- Matching account ID and successful authenticated decryption are required before replacing the local vault. Preserve the previous encrypted file as vault-before-pull-GUID.enc.json; never upload backups. Empty cloud and failed fetch leave local data unchanged.
- Different cloud master password opens a themed password prompt. Changing the local vault master invalidates local Hello enrollment. Snapshot is downloaded inside the vault but Restore remains explicit so active tabs are not unexpectedly replaced.
- run.cmd selects release-v0.10.1; bundled runtime unchanged.
- Verification: release-v0.10.1 passed all 12 vault checks, including empty-cloud sign-in, automatic download/display with encrypted backup, failure preserving local bytes, and existing OAuth transport/encryption/Hello regressions. Discord OAuth was mocked by the local fixture; no real account/network deployment used.


## 0.11 browser tools / Claude handoff

- User requests custom DevTools, Ctrl+P, casting, integrated ambient YouTube, Ctrl+F, browser task manager, and completed-step Markdown for continuation in Claude. CONTINUE-HERE.md is the authoritative handoff with feature status, verification and limitations.
- Implemented themed tools in BrowserTools.cs, BrowserDeveloperTools.cs, BrowserMediaTools.cs, Ambient.js. Shared ToolWindow has rounded fields/checks, icons and theme. Menus now scroll to fit screen; existing tests adapted to the ScrollViewer wrapper.
- Find uses CoreWebView2.Find (counts/options/debounce/cleanup). Print uses engine PrintAsync/PrintToPdfAsync with custom settings; physical printing requires explicit user click. DevTools captures console/network and exposes DOM source/evaluation, with full engine tools fallback. Task manager reports actual working set and sampled normalized CPU per process, no fabricated per-tab RAM; rest/close protects active/audio/pinned rules.
- Ambient is opt-in per profile; shared normal windows update together, private windows remain separate. A 64x36 blurred video canvas samples at up to 8fps, skips hidden/reduced-motion/fullscreen, removes itself on disable/pagehide; no pixel reading or network upload.
- Current bundled engine rejects CDP Cast.enable. Cast window explicitly reports this and offers Windows Cast via user-initiated Win+K; this mirrors a compatible display, not Chromecast/tab-only casting. Actual receiver and physical printer not tested.
- Final tools suite passed 6 grouped checks including actual synthetic video drawing, persistent setting and CPU samples. Existing native regression passed 39 checks. Core tests passed with 0 failures outside sandbox (sandbox initially denied atomic file replacement). No real profile modifications. Latest launcher is release-v0.11; Runtime hardlinked from existing bundled engine, never edit linked engine files.


## 0.11.1 Find/sidebar/official Ambient/Store installation

- User requested compact Ctrl+F at bottom right, centered compact rail, official Ambient Light for YouTube paponcgjfojgemddooebbgniglhkajkj replacing home-grown effect, automatic Store CRX handling without manual file interactions, and redesigned extension popup.
- BrowserTools.CompactFind reparents Find controls into 430x90 rounded window anchored 12 DIP from content bottom/right. Position follows owner move/resize; handlers removed on close. Sidebar collapsed margin is 0 with explicit centered buttons/favicons.
- BrowserExtensions.ShowExtensions is now frameless/transparent with rounded theme surface and draggable header. Existing permissions review, filter and management remain.
- BrowserStoreBridge.cs/StoreBridge.js intercept trusted Add to Chrome/Edge clicks on HTTPS Chrome Web Store detail pages; fallback Add to Mica button is visible on detail pages. Per-page closure token + matching native source origin/detail ID guards installation; synthetic clicks and raw script navigation are rejected. WebView IsUserInitiated alone proved insufficient in tests, so do not replace token validation with it. Store CRX download events are intercepted and re-fetched via validated Google endpoint without save dialog. Signed CRX ID validation/extraction limits remain unchanged.
- Menu ambient toggle installs/enables/disables actual official extension, not the old custom canvas script. Old Ambient.js stays as inactive historical source/resource. Existing enabled preference migrates on first normal web navigation; private extensions remain disabled. Manager toggle/removal keeps ambient preference synchronized.
- Official Google CRX 2.38.17 installed under original ID in isolated test profile. Store bridge suite verified trusted click, ignored synthetic click/raw script request, enable/disable/persistence, frameless popup, bottom-right Find. Tools suite passed. Full effect on real YouTube video/DRM and native Chrome toolbar popup remain unverified/unsupported respectively.
- Latest executable release-v0.11.1/Mica.exe; runtime hardlinks unchanged. CONTINUE-HERE.md contains current continuation instructions.
- Final release-v0.11.1 regression rerun passed all 39 native checks after token cleanup; tools and official Store bridge suites also passed.


# Mica 0.11.2 — shell rendering and tab highlight

- Selected expanded tab now spans title and close-button region, with inset close hover. Tab background no longer scales/clips on hover/press.
- WPF shell uses ProcessRenderMode.SoftwareOnly before any windows are created. WebView2 page/video GPU acceleration remains enabled in separate engine processes. No system registry/driver settings changed.
- This mitigates the reported disappearing shell restored by Win+Ctrl+Shift+B. The long-running GPU failure has NOT been reproduced here; root cause/driver attribution and long-session resolution remain unconfirmed. Software shell rendering may increase CPU usage during animation/resize.
- Build and --regression-test passed (41 named checks). Spotlight test now waits for observable close completion (bounded 3 seconds), rather than a brittle 240ms delay; initial run failed that timing check and the dependent caption hit test.
- Portable runtime linked from 0.11.1. Close all existing Mica windows and restart with run.cmd to use this release.

## Next investigation if shell disappears again
Record runtime version, time to failure, display/GPU details, and whether web content remains interactive. Distinguish WPF shell/device loss from WebView2 renderer failure. Do not claim the GPU issue conclusively fixed from short regression tests.


## 0.11.3 — popup trigger toggles (completed)

- Cause: StaysOpen=false dismisses a popup on mouse-down outside it, before the trigger Button.Click on mouse-up. The old IsOpen guard then reopened it and replayed its entrance animation.
- BrowserMenus.cs intercepts PreviewMouseDown in InputManager before auto-dismiss, only when the left press is within the currently open popup's visible trigger. Cancels that press and closes the popup. Normal outside dismissal and keyboard Click behavior remain; handler is removed on window close.
- Applies to expanded SpaceButton/MenuButton and compact MiniSpace/MiniMenu. No timing-based suppression flags.
- Build release-v0.11.3 succeeded. Native --regression-test passed 45 named checks, including OS mouse down/up for open/close/reopen on all four triggers, counting Popup.Opened to ensure no replay. Isolated profile; cursor restored after checks.
- run.cmd now selects release-v0.11.3; engine hardlinked from previous portable release.
- Prior 0.11.2 long-session disappearing-shell mitigation remains unconfirmed in real extended use; see earlier notes.


# Mica 0.12.0 — Custom Windows installer

## Completed
- Custom rounded purple WPF installer with Caveat M. branding, install progress, retry/error states, optional Desktop shortcut and Start menu shortcut.
- One offline setup EXE bundles the fixed engine. Per-user install at %LOCALAPPDATA%\Programs\Mica, no elevation. Does not change the default browser or auto-pin to taskbar.
- Registers HTTP/HTTPS capabilities, App Paths, Installed Apps uninstall entry. Does not advertise local HTML handling (Mica currently accepts HTTP(S) URLs only).
- Creates Start/Desktop shell links with Mica.Browser.Desktop AppUserModelID. Browser HWND now receives matching identity plus relaunch command/name/icon; the old portable build only assigned the process ID.
- Existing Jump List generators retain Top Sites, Recently closed, New window and New private window. Windows settings/policy can suppress history. Rejected-item reasons are logged to jump-list-status.txt without page titles/URLs.
- Staged extraction, payload SHA-256 integrity check, traversal/duplicate-path rejection, free-space check, managed-folder marker, no reparse-point cleanup. Existing unmanaged folders are refused. Upgrade rolls back application files if activation/registration fails.
- Running Mica blocks install/uninstall via its existing mutex. Uninstaller launches a small temporary copy so it can delete the installed EXE. Profile at %LOCALAPPDATA%\MicaBrowser is preserved.

## Validation
- Installer self-test: extract full engine, verify both shortcut AppUserModelIDs, isolated registry registration, upgrade, uninstall and outside-data preservation, reject unmanaged directory: PASS.
- Preview rendered and visually inspected in artifacts/mica-installer.png.
- Browser regression includes shell window identity and both task commands. See artifacts/regression-result.txt for final run.
- No production install, default-browser change, taskbar pinning or profile mutation performed. Actual Explorer taskbar popup after installing remains a manual visual check.

## Build / handoff
- build.ps1 -OutputDirectory release-v0.12.0; bundle Runtime (reuse hardlinks to previous release).
- build-installer.ps1 creates installer-output/Mica-0.12.0-Setup.exe. Compression temp file goes to system TEMP to conserve D: space.
- Source: installer/Setup.xaml, Setup.cs, InstallEngine.cs; shared src/WindowsIntegration.cs.
- Self-test: setup --self-test <absolute result file>. Uses disposable system-TEMP directory and HKCU\Software\MicaInstallerTests only.
- Preview: installer-output/MicaSetup.exe --preview <absolute PNG path>. Does not install.
- Binary is not Authenticode-signed; Windows may show Unknown publisher/SmartScreen until a signing certificate is supplied. SHA-256 checks detect corruption, not publisher authenticity.
- If an old portable taskbar pin remains, unpin it and pin the installed Mica from Start. Windows controls Jump List appearance.
- Earlier disappearing-shell mitigation remains unconfirmed under long-session real usage; see 0.11.2 notes.

## Final verification — 0.12.0
- Final setup: installer-output/Mica-0.12.0-Setup.exe, 321980856 bytes (~307 MiB).
- Final packaged EXE --self-test: all 7 checks PASS, including installed bundled-engine launch/navigation/JavaScript. artifacts/installer-result.txt and installed-runtime-result.txt.
- Final browser --regression-test: 47 named checks PASS.
- Final installer preview refreshed and visually inspected; artifacts/mica-installer.png.
- No production installation was performed. Actual Explorer Jump List display must be checked after installing/launching via Start menu.
