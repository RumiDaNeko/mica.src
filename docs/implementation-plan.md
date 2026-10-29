# Mica Browser Implementation Plan

**Goal:** Deliver a runnable Windows browser with Arc-inspired chrome and keyboard-first Spotlight.
**Architecture:** WPF shell and native start page, WebView2 content views, pure C# browser state/search core.
**Tech stack:** .NET Framework 4.8-compatible C# 5, WPF, Microsoft.Web.WebView2 1.0.4258.31.
**Spec:** docs/design.md

## Constraints
- No 1.1 MB per live tab claim. Measure total process working set.
- HTTP(S) navigation only; no native bridge exposed to websites.
- Retain metadata for sleeping tabs and explain reload behavior.
- Keep all build dependencies in this project.

## Tasks
- [x] Core: write behavior tests in tests/CoreTests.cs; compile against contract stubs and observe failures, then implement src/Core.cs. Tests cover search ranking, URL policy, tabs and JSON round trips.
- [x] Shell: implement src/Shell.xaml and src/BrowserApp.cs. Bind navigation and sidebar to the core, persist sessions, implement native home, theme and window controls.
- [x] Spotlight: implement overlay with ranked results, arrow selection, Enter, Escape, focus restoration and tab switching across spaces. Hide the HWND web content while the overlay is open to avoid WPF airspace overlap.
- [x] Web engine: initialize shared WebView2 environment lazily, handle title/navigation/new windows/errors, dispose stale controls and keep playing audio alive.
- [x] Deliver: build.ps1 and run.cmd; compile and run behavior tests; run isolated app smoke mode; inspect PNGs and document verified behavior and limitations in README.md.
