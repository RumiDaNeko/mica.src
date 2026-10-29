# Mica 0.6 verification

2026-10-24. Windows x64, .NET Framework 4.8, WebView2 SDK 1.0.4258.31.

History and Downloads are native internal pages (`mica://history`, `mica://downloads`) sharing one library tab per Space when opened from the menu. Existing web tabs remain available. The six-command menu removes duplicate new-tab, search, theme and Space actions.

History supports title/URL search, local date groups, date filters, individual removal and confirmed clearing. Downloads uses real WebView2 operations, a Windows destination picker, received-byte progress, pause/resume/cancel, file/folder actions and persisted metadata. Removing records never deletes files. Source controllers are retained for live downloads after tab closure and released when finished. Detached pages cannot start new downloads or open tabs, including after a queued callback.

Finished download metadata is limited to 200 recent records and a conservative 1 MiB character budget; live operations are never pruned. Loading supports files beyond the previous default JSON serializer limit. The refresh timer stops when no rendering or persistence work remains.

Verification on the packaged binary:

- 31 core logic checks passed, including internal URL normalization and session restoration.
- 11 library checks passed, including preserving the source website, grouping/search, persistent single removal, clear/empty state, native tab reuse, Spotlight restoration and returning to the web view. Additional assertions cover date/download filters and cancelling history clearing.
- 11 download checks passed using a loopback HTTP server: exact file bytes; actual native Pause/Resume actions; paused byte count stability; sleep protection; completion of an 8 MiB payload after source-tab closure; detached-page guards; cancellation; restart metadata; JSON files over 2 MiB; bounded pruning that preserves live transfers; file preservation; idle timer shutdown.
- 37 existing native regression checks passed: sidebar, Spaces, menus, interrupted animations, minimum size/maximize/footer, refresh lifecycle, aborted navigation, native context commands and favicons.
- Full smoke check passed: real example.com navigation, address/Back recovery, bookmarks, Spotlight across Spaces, sleep/restore, themes, persistence and 960×680 layout.

Visually inspected History, live/paused Downloads, light/dark library, compact library and compact menu captures. Tests use isolated profiles. No test opened downloaded files or used the user's browser data. The standard destination dialog and OS file/folder launch were not automated; tests chose fixture paths.

Only downloads begun in 0.6 are tracked. Closing the app stops unfinished transfers; restart shows Interrupted and offers retry through the website, not cross-session resume. In-session resume depends on WebView2/server support. The test runtime resumed its existing response without a new range request and produced the complete byte-exact payload.

`release-v0.6/Mica.exe` matches the tested executable SHA256:
`6408BD2706D18F89C9663D25E22018AE494F28A06F44B5E12D2AB1657BBEC70A`

Portable archive: `artifacts/Mica-Windows-x64-v0.6.zip`. `run.cmd` prefers 0.6.
