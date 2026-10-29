# Mica 0.8.3 — Vault interface

Passwords & sync now uses Mica's frameless rounded dialog, themed rounded fields/cards and labeled action icons. Passwords separates the saved-login list from its editor. Sync & account groups Discord sign-in, encrypted pull/push, browser snapshots and Windows passkey settings. Close and Escape retain locking behavior.

Existing encryption, transport, consent and credential-fill behavior is unchanged. Five native vault integration checks passed on this executable: encrypted disk persistence; close/reopen locking; HTTPS-origin-restricted fill without submission; mocked Discord and encrypted Node SQLite roundtrip; stale-write rejection and token revocation. Tests used isolated credentials/profiles and a local mock API. No production server or real account was used.

Inspected light/dark saved vault, locked state and sync screenshots. Passwords remain masked; list rows display origin and username only.
