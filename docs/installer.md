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
