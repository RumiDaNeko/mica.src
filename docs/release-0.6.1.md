# Mica 0.6.1 verification

2026-10-24. The four Home shortcuts now display website-owned ICO assets embedded in the executable. No requests or hidden web engines are created to render these icons. Their destinations are unchanged. Sources recorded in `src/Assets/Favicons/SOURCES.md`.

Build succeeded. Full smoke suite passed with an isolated profile: Home rendering, web navigation and Back, bookmarks, Spotlight across Spaces, sleeping/restoring tabs, theme/session persistence and 960×680 layout. Inspected the light and dark Home captures: all four website icons render correctly. The broader 0.6 regression suites were not rerun for this asset-only behavior change.

Created `memory.md` with user preferences, implemented behavior, limitations, build/test workflow and engine research. All three requested engine goals remain open. No engine migration is claimed: local D: free space is approximately 6.3 GiB, below Chromium's documented 100 GB Windows build requirement.

Tested and packaged `release-v0.6.1/Mica.exe` SHA256:
`EC952999F558E585DC3F37CA727B949123794C019B5AFCDF39EB54283B14B7A8`

Portable archive: `artifacts/Mica-Windows-x64-v0.6.1.zip`.
