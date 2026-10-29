# CefGlue (vendored)

Source of OutSystems/CefGlue, branch `RDEV-8412-bump-cef-to-134.3.9` at commit `2e308b3` (2026-08-03), which is
draft PR OutSystems/CefGlue#208: CefGlue moved to CEF 134.3.9 (Chromium 134). No NuGet release of it exists, so Mica
builds it from source. MIT licensed, see `LICENSE`.

Only the library projects are copied (CefGlue, CefGlue.Common, CefGlue.Common.Shared, CefGlue.BrowserProcess,
CefGlue.Avalonia). Mica's changes, all marked "Mica:":
- `Directory.Build.props`: Platform defaults to the machine's architecture (x64 or ARM64).
- `Directory.Build.targets`: no NuGet packages on build.
- `Nuget.config` removed (it put a private package cache inside the folder).

Read `KNOWN-ISSUES.md` and `CEF134-PR208-REVIEW.md` before relying on it: the PR is still a draft.
Replace this folder with an official CefGlue release on CEF 128+ when one ships.
