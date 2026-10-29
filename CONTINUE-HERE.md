# Mica handoff for Claude — 0.12.0

## Latest completed: 0.12.0 — Custom Windows installer

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

## 0.11.3 — popup trigger toggles (completed)

- Cause: StaysOpen=false dismisses a popup on mouse-down outside it, before the trigger Button.Click on mouse-up. The old IsOpen guard then reopened it and replayed its entrance animation.
- BrowserMenus.cs intercepts PreviewMouseDown in InputManager before auto-dismiss, only when the left press is within the currently open popup's visible trigger. Cancels that press and closes the popup. Normal outside dismissal and keyboard Click behavior remain; handler is removed on window close.
- Applies to expanded SpaceButton/MenuButton and compact MiniSpace/MiniMenu. No timing-based suppression flags.
- Build release-v0.11.3 succeeded. Native --regression-test passed 45 named checks, including OS mouse down/up for open/close/reopen on all four triggers, counting Popup.Opened to ensure no replay. Isolated profile; cursor restored after checks.
- run.cmd now selects release-v0.11.3; engine hardlinked from previous portable release.
- Prior 0.11.2 long-session disappearing-shell mitigation remains unconfirmed in real extended use; see earlier notes.

## Latest completed work (0.11.2)

- Selected expanded tab now spans title and close-button region, with inset close hover. Tab background no longer scales/clips on hover/press.
- WPF shell uses ProcessRenderMode.SoftwareOnly before any windows are created. WebView2 page/video GPU acceleration remains enabled in separate engine processes. No system registry/driver settings changed.
- This mitigates the reported disappearing shell restored by Win+Ctrl+Shift+B. The long-running GPU failure has NOT been reproduced here; root cause/driver attribution and long-session resolution remain unconfirmed. Software shell rendering may increase CPU usage during animation/resize.
- Build and --regression-test passed (41 named checks). Spotlight test now waits for observable close completion (bounded 3 seconds), rather than a brittle 240ms delay; initial run failed that timing check and the dependent caption hit test.
- Portable runtime linked from 0.11.1. Close all existing Mica windows and restart with run.cmd to use this release.

## Next investigation if shell disappears again
Record runtime version, time to failure, display/GPU details, and whether web content remains interactive. Distinguish WPF shell/device loss from WebView2 renderer failure. Do not claim the GPU issue conclusively fixed from short regression tests.

## Previous feature release (0.11.1)
Start `run.cmd` or `D:\project\browser\release-v0.11.3\Mica.exe`. Keep DLLs and Runtime next to EXE. Latest request implemented: small bottom-right Ctrl+F, center sidebar icons, use specified official Ambient extension, automatic Chrome Web Store CRX installation, redesign extension manager popup.

## Finished steps
- [x] Ctrl+F is a 430×90 rounded bar, 12 DIP inside content bottom-right. Moves/resizes with main window; next/previous, case/whole word, counts and Escape remain.
- [x] Collapsed rail has no asymmetric margins; all mini controls and tab icons explicitly center.
- [x] Extensions manager uses frameless rounded themed chrome, close button and draggable header.
- [x] Add to Chrome/Edge trusted click is captured on Store pages; Add to Mica fallback appears at bottom-right when native Store button is disabled. Downloads directly from validated Google update endpoint; verifies CRX signature and expected ID, extracts within limits, installs with original ID, persists managed folder. No save/open dialog or second install confirmation in this flow. Paste-link permissions review remains an optional separate flow.
- [x] Native CRX download event from a Store detail page is intercepted without file picker and re-fetched through the signed Store pipeline. This fallback branch is implemented; primary trusted-button path was tested end-to-end.
- [x] Ambient menu now controls official **Ambient Light for YouTube** ID `paponcgjfojgemddooebbgniglhkajkj`. First enable downloads/installs; next toggles enable/disable. Stored preference migrates old enabled installations on first web navigation. Private extensions remain off. Manager toggle/removal syncs preference. Old homemade effect no longer injected.
- [x] Build and isolated native tests; progress stored in this file and memory.md.

## Verification / scope
- Build release-v0.11.1 passed.
- `--store-bridge-test`: official Google CRX 2.38.17 installed with correct ID; actual trusted browser click downloads/installs without picker; synthetic click and raw script navigation do not install; enable/disable/persistence; popup chrome; Find geometry all passed. `artifacts/store-bridge-result.txt`.
- `--tools-test`: Find counts/options, PDF export, Console/DOM/Network, RAM/CPU tasks, no legacy ambient injection, tab-switch cleanup passed. `artifacts/tools-result.txt`.
- `--regression-test`: final build passed 39 checks including rail centering, window bounds, menus, navigation and favicon. See `artifacts/regression-result.txt`.
- Screenshots: mica-find-bottom-right.png, mica-extensions-v0111-light.png, mica-extensions-v0111-dark.png, mica-centered-sidebar.png in artifacts.
- No real user profile or account changed. Installed packages removed from isolated test profiles at test completion. Actual effect across real YouTube videos/DRM not claimed verified; real Store DOM may change, so fallback button exists.

## Important implementation details
- src/BrowserStoreBridge.cs: direct install helper, trusted Store source/ID validation, native navigation/CRX interception, official ambient toggle. Uses per-page secret token held in injected closure, validated natively. Do not rely only on WebView2 IsUserInitiated: raw scripts tested true for this API. The closure only dispatches after isTrusted clicks.
- src/StoreBridge.js: embedded resource Mica.StoreBridge.js. Captures Add buttons in English/Vietnamese and adds fallback Add to Mica. Handles Store SPA paths. No WebMessage/host objects enabled.
- src/BrowserTools.cs: CompactFind layout/anchor; resources retained after reparenting.
- src/BrowserExtensions.cs + Extensions.xaml: popup chrome/UI, official ambient toggle/removal state consistency.
- src/BrowserMediaTools.cs: old canvas injection replaced with cleanup/migration; ToggleAmbient calls SetAmbientExtension. Ambient.js remains inactive historical code/resource, not the live implementation.
- BrowserApp.cs: Store bridge hooks before ordinary navigation/download dispatch and on completion; token cleanup on view disposal.
- tests/StoreBridgeRegression.cs: local Store UI fixture, actual official CRX download/install. tests/ToolsRegression.cs ambient section now checks old effect is absent; official ambient is covered separately.

## Environment / continuation commands
Workspace D:\project\browser, no Git repo. WPF, C#5, .NET Framework 4.8, WebView2. No dotnet SDK. Build: `powershell -File build.ps1 -OutputDirectory release-v0.11.1`.
Native suites: run one at a time (shared smoke mutex). Start-Process -WindowStyle Hidden -PassThru, wait, read result and exit code. --*-test creates artifacts/smoke-GUID profile. Elevated execution may be necessary because sandbox blocks native networking and atomic replacement. Never touch %LOCALAPPDATA%\MicaBrowser during tests.
Python: C:\Users\rumi\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe; specify UTF-8 for reads/writes. C#5 cannot await inside catch/finally. Node fixture tests need C:\Program Files\nodejs on PATH.
Low disk: Runtime 257 files are hardlinks to unchanged official fixed engine 156.0.4314.42 x64. Never modify linked engine files in place. build.ps1 does not bundle Runtime; include Runtime and bundled-engine.json for a new release. No new ZIP made.

## Remaining broader limits from 0.11
Direct Chromecast/tab casting unsupported by current engine; Windows Cast button provides display-level Miracast chooser only (no receiver tested). DevTools custom panels are compact Console/DOM-source/Network, with full engine tools fallback. Print has settings/PDF export but no paginated preview; no physical print job tested. Native Chrome toolbar extension popups and automatic extension updates remain unsupported.

## References
User-selected extension: https://chromewebstore.google.com/detail/ambient-light-for-youtube/paponcgjfojgemddooebbgniglhkajkj
Official project: https://github.com/WesselKroos/youtube-ambilight
All previous requirements/backend/vault/history: README.md and memory.md.

## Final verification — 0.12.0
- Final setup: installer-output/Mica-0.12.0-Setup.exe, 321980856 bytes (~307 MiB).
- Final packaged EXE --self-test: all 7 checks PASS, including installed bundled-engine launch/navigation/JavaScript. artifacts/installer-result.txt and installed-runtime-result.txt.
- Final browser --regression-test: 47 named checks PASS.
- Final installer preview refreshed and visually inspected; artifacts/mica-installer.png.
- No production installation was performed. Actual Explorer Jump List display must be checked after installing/launching via Start menu.
