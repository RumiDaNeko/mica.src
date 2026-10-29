# Mica 0.4 verification

2026-10-23, Windows x64, .NET Framework 4.8, WebView2 SDK 1.0.4258.31.

Packaged `release-v0.4/Mica.exe` is a byte-for-byte copy of the tested `release-redesign/Mica.exe`. Portable archive: `artifacts/Mica-Windows-x64-v0.4.zip`. Launcher prefers 0.4. Existing profiles and running user browser were not modified by tests.

Validation passed:

- 29 core behavior tests and standalone favicon cache tests: origin isolation, bounded thumbnails/cache, persistence, corruption fallback, stable updates.
- 23 native UI/navigation regression checks: removed duplicate/obsolete controls; Spaces selection; labeled menu; compact Spotlight; 30-tab rail overflow; interrupted sidebar and Spotlight animations; native drag, resize feedback and maximize; actual HTTP 200 ConnectionAborted retention; rapid navigation; duplicate Spotlight activation; real favicon rendering, retention during sleep and compact tab activation.
- 7 extension checks with an isolated profile and harmless MV3 content script: initialization from home, manager window, install/enumeration, actual injection, disable, enable, removal. Built-in runtime extensions remain unchanged.
- Full smoke test: real web navigation, Back after error, bookmarks, cross-space Spotlight, tab sleeping/restoring, theme/session persistence and 960×680 layout.
- Public example.com, GitHub and Gmail sign-in loaded successfully. Authenticated Gmail was not tested.
- Visually inspected expanded/compact sidebar, light/dark home, Spaces/menu popup and extension manager captures. WPF shell captures do not include WebView2's separate native web surface; website rendering is verified via engine capture/navigation checks.

Review found compact scrollbar clipping and duplicate Spotlight selection during its close animation. Both were reproduced and fixed with regression coverage.

Limits: compatible unpacked extensions only, no direct Chrome Web Store/CRX installation, no Chrome-style extension toolbar popup, and no claim of complete Chrome API support. Native Windows owns resize/snap/maximize/minimize; Mica animates its own chrome and responsive home content, not third-party websites. No claim of 1.1 MB per tab.
