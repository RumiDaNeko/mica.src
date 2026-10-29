# Mica 0.9 — Bundled engine

Portable package includes the complete official Microsoft WebView2 Fixed Version 156.0.4314.42 x64 runtime. Extract the full package and run Mica.exe; no separate Edge browser or Evergreen WebView2 installation is required. Windows x64 and .NET Framework 4.8 remain required.

Runtime selection explicitly supplies the adjacent Runtime folder. A marked portable package with missing/incomplete runtime fails with extraction guidance. Runtime initialization prepares Microsoft's required AppContainer read/execute permissions without changing browser profile permissions or sandbox settings. Store downloads use the selected environment's version.

Verified the official CAB hash and executable's valid Microsoft Authenticode signature. All 257 runtime files are retained. Final native tests passed: actual browser process path under the bundled folder and JS/navigation; full browser smoke; nine extension checks; seven privacy/Home/multiwindow checks. A separate clean Windows machine without installed WebView2 was unavailable; the process-path assertion proves the local tests used the packaged runtime.

Fixed Version does not self-update. Future Mica releases must ship engine security updates. This is a bundled WebView2 distribution, not a Chromium-engine migration. See READ-ME.md for deployment and rebuild instructions.
