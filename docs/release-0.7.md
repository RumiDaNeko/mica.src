# Mica 0.7 — verification and deployment boundary

2026-10-24. Windows x64, .NET Framework 4.8, WebView2 SDK 1.0.4258.31. Node.js 24 backend. No engine migration in this release.

## Implemented

Home uses counted browsing history grouped by origin, with recent-visit tie breaking and a four-site limit. Existing records migrate to one visit. Empty history shows an empty state. A Windows Jump List builds Top Sites, Recently closed, New window and New private window. Taskbar URL activation forwards to the running process through a named pipe restricted to the current Windows user. New windows share normal cookies, history, bookmarks and download records while keeping independent tabs.

Private windows use actual InPrivate controllers in isolated user-data folders, display PRIVATE in the address bar, and do not save session/history/download metadata or disk favicon caches. Vault, sync and extension management are unavailable. Closing schedules bounded engine-folder cleanup on a foreground thread; next startup retries crash/locked-folder leftovers. Files explicitly downloaded remain.

Native caption/thick-frame/minimize/maximize styles are preserved so Windows/DWM controls transitions. Resize minimums, work-area footer bounds and animation interruption regressions pass. Exact visual equivalence to Edge has not been verified.

The password vault supports manual credential creation/edit/delete/copy and explicit fill on the matching HTTPS origin, without automatic capture or submission. The master password is independent of Discord. AES256CBC ciphertext uses Encrypt-then-HMACSHA256 with authenticated metadata and PBKDF2-SHA256 600,000 iterations. The server receives only the encrypted envelope. Local account tokens use DPAPI; closing the vault removes its UI-held credential references. Managed strings are not a guarantee of immediate secure memory erasure.

Discord OAuth uses identify scope, state, a desktop-held possession proof, one-use token delivery, hashed sessions and logout revocation. SQLite vault writes use authenticated account identity and version comparison. The client requires Pull before Push, checks encrypted account binding, and confirms remote replacement. Browser snapshots contain tabs/bookmarks/history/theme/Space. Restore is explicit and confirmed.

## Verification

- Core suite passed, including visit-count persistence/migration, origin ranking and recently closed snapshots.
- 38 native regression checks passed, including frame styles, maximize, footer visibility, resize limits, sidebar/Spotlight interruption, context-menu lifecycle, navigation and favicons.
- 11 library and 11 real-download checks passed after integration.
- 7 privacy/Home/window checks passed on the final executable: true InPrivate mode; two-way cookie isolation; no app metadata persistence; new private session starts empty; normal multiwindow shared profile and aggregate tab persistence; ranked Home; native Jump List object contents; empty-state behavior.
- A separate private-only process navigated and set a fixture cookie, then closed its last window. After process exit, its private profile directory was confirmed removed.
- 14 standalone vault crypto/origin/schema checks passed. Five native vault integration checks passed: create/save/reopen; no plaintext credentials on disk; actual matching-origin form fill without submission; rejection on another origin; .NET HTTP → mocked Discord OAuth → Node SQLite → decrypt roundtrip; stale-write rejection and logout revocation.
- Five Node HTTP integration tests passed covering state replay, possession proof, expiry, polling limits, user isolation, concurrent compare-and-swap, payload validation/limits, CORS, logout and SQLite persistence.
- Full browser smoke passed: real page navigation/Back, bookmarks, Spotlight, sleeping/restoring tabs, themes, persistence and compact layout.

Read-only review identified final-window private cleanup lifetime and response disposal; both were fixed. Inspected personalized Home, private badge/empty state and masked vault screenshots. Tests used isolated profiles, fixture passwords and mock OAuth. No real Discord login or production-domain request was performed. Windows shell acceptance/pinned-shortcut behavior and physical taskbar clicks were not automated; the native Jump List contents and command targets were checked without modifying the user's taskbar.

## Delivery and remaining scope

Server source includes Node24 HTTP/SQLite, `.env.example`, Docker/Compose and nginx templates. See `server/README.md` for exact deployment steps and callback. Docker deployment, DNS/TLS provisioning and real Discord OAuth remain for the user-hosted environment; Docker was not run locally.

Passkeys remain Windows-managed. A Mica passkey provider and passkey cloud sync are not implemented. Sync is manual, not automatic conflict merging, and excludes website cookies/sessions, download files/metadata and extension packages. This is an initial password manager implementation, not a claim of an independently audited credential product.

Final tested executable SHA256:
`6258ADA72F936C77B1449642C75EBD2875C3BCB803E9E9C8CD120041F5E2458C`

Deliverables: `release-v0.7/Mica.exe`, `artifacts/Mica-Windows-x64-v0.7.zip`, `artifacts/Mica-Sync-Server-v0.7.zip`. `run.cmd` prefers 0.7. All previous release directories remain intact.
