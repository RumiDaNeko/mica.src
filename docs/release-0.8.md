# Mica 0.8 — Extensions

The extension manager now hides WebView2's built-in PDF and clipboard components without changing their enabled state. A redesigned light/dark interface shows user-installed extensions, actual package icons, source/version, search, enabled-state filters, toggles and removal.

Paste a Chrome Web Store detail URL, select Review extension, inspect requested access, then Add extension. Browse store opens the catalog; opening the manager while viewing a detail page prepopulates its URL. The website's Add to Chrome button is not integrated.

Packages download from Google's official HTTPS update endpoint with bounded Google-host redirects and a 30-second deadline. CRX3 verification binds a developer RSA signature and signed ID to the requested Store ID. It is not a pinned Google-publisher attestation. ECDSA-only developer proofs are unsupported. Package size, expanded size, entry count and paths are bounded; symlinks, traversal, Windows reserved names and case collisions are rejected. Google reserved _metadata files are omitted after verification. The verified public key preserves the original extension identity when loaded unpacked.

Preparing a package does not install it. Cancel or closing before confirmation disposes staging files. Confirm installs through WebView2 and retains the managed folder. Successful removal/replacement prunes old managed folders after metadata persistence; external unpacked sources are never deleted.

## Verified on the final executable

- 36 standalone package validation checks.
- Nine native extension regressions, including real content-script execution, disable/re-enable/removal and unchanged runtime components.
- Four Store checks: real Dark Reader 4.9.133 download/signature verification; review/cancel without installation; confirmed original-ID installation and actual stylesheet injection; removal and package cleanup.
- Full browser smoke: navigation/Back, bookmarks, Spotlight, sleeping/restoring, themes/session and compact layout.
- Inspected native captures for light/dark installed list and permission review. Tests use isolated profiles.

## Limits

The engine remains WebView2. Installation does not imply complete Chrome API compatibility; toolbar extension popups and automatic package updates remain unsupported. Re-paste and review a link to replace the installed version. Extensions are shared by normal Spaces and unavailable in private windows. No real user profile was modified by testing.
