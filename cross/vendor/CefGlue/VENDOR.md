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

## CEF 156 (Mica, 2026-10-29)

Mica regenerated the interop for CEF 156.0.14 (Chromium 156.0.8078.48, API version 15600) itself:
- `CefGlue.Interop.Gen/`: the PR's generator, vendored. `include/` holds the 156.0.14 headers: from the Windows minimal
  distribution, plus `cef_application_mac.h`, `cef_sandbox_mac.h`, `internal/cef_{linux,mac,types_linux,types_mac}.h`
  from CEF's source at the same commit (`4fa4ab5`). The parser skips members marked `added=experimental` or removed at
  or before the target API version (CEF keeps them in the headers behind `#if CEF_API_...`).
- New classes (Level 1, not bridged into CefGlue.Common): `CefComponent`, `CefComponentUpdater`,
  `CefComponentUpdateCallback`, `CefV8BackingStore`, and the three `CefComponent*` enums.
- Struct mirrors and enums were compared field by field against the 156 headers evaluated at API 15600 (only
  `cef_settings_t` changed: `use_views_default_popup`, `keychain_service_name`, `keychain_account_name`; `log_items`
  was missing before but sat in padding). Enums gained their 135-156 values; existing values didn't move.
- `CefDisplayHandler`: `OnContentsBoundsChange` and `GetRootWindowScreenRect`, defaulting to CEF's own handling.
- CEF 154 removed V8 extensions (`CefRegisterExtension`). The renderer now installs the `cefglue` helper object in each
  new V8 context (`JavascriptHelper.InstallInContext`), so `EvaluateJavaScript` and bound objects work as before.
- No NuGet CEF packages (`CefGlue.Packages.props` is empty): Mica's `cross/fetch-cef.*` download CEF. `CefRuntime`
  can load libcef from the folder in `CEFGLUE_LIBCEF_DIR` (browser and helper processes), and `CefRuntimeLoader` then
  points resources/locales (macOS: framework and Resources) there. Mica's engine updater uses this.

To move to another CEF major: put its headers in `CefGlue.Interop.Gen/include` (keep the mac/linux ones), add new
classes to `schema_cef3.py` and new enums to `schema.py` until `python -B cefglue_interop_gen.py --cpp-header-dir include
--cefglue-dir ../CefGlue/ --no-backup` runs clean (delete `CefGlue/CefGlue.g.props` after), implement new handler
callbacks, diff the struct mirrors and enums against the headers at the new API version, then update `cross/cef.lock`.
Replace this folder with an official CefGlue release on CEF 128+ when one ships.
