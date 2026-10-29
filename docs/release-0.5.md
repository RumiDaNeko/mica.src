# Mica 0.5 verification

2026-10-23. Windows x64, .NET Framework 4.8, WebView2 SDK 1.0.4258.31.

The missing footer was reproduced on a 1920×1080 monitor with a work area ending at y=1032. Before the fix, the maximized root was 1094 pixels high and Menu/theme ended at y=1072, behind the taskbar. With monitor work-area bounds, the root is 1032 pixels high and the controls end at y=1017. Compact mode and 30 pinned tabs also keep the footer visible. Native minimum-size processing remains enabled.

The reload glyph rotates only while the active tab is loading and settles after completion/error or a switch to a resting tab. Native tab and web context menus share rounded light/dark styles, icons, separators, shortcuts and submenus. Web commands retain their engine command IDs; menu deferrals complete once, synchronously before tab disposal.

37 native regression checks passed, including the maximized footer, 30 pinned tabs, minimum dimensions, refresh lifecycle, real engine context-menu request and reload execution, dismissal and closing a tab with its menu open. Full smoke navigation, Back recovery, bookmarks, Spotlight, sleeping/restoring tabs, themes, persistence and 960×680 layout passed. Inspected maximized layout plus light/dark tab menu, space submenu and native web menu captures.

Read-only review raised minimum-size hook ordering and deferral timing. Actual application regressions verified both fixes. Tests used isolated profiles; the user's running browser and profile were not changed. Mixed-monitor DPI configurations were not physically exercised.

`release-v0.5/Mica.exe` matches the tested executable SHA256:
`D155E410C03BDD2560D53A00C271DD20F25FCC5C0ECC744C053F5130A8453E2B`

Portable archive: `artifacts/Mica-Windows-x64-v0.5.zip`. `run.cmd` prefers 0.5.
