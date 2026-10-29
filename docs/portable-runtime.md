# Mica portable — bundled engine

Extract the entire ZIP into a local folder you own, then open Mica.exe. Keep Runtime/ and the DLL files beside the executable. Do not run from inside the ZIP or a network share. No Edge browser or separately installed WebView2 runtime is needed. Windows x64 and .NET Framework 4.8 are still required.

This package uses Microsoft WebView2 Fixed Version 156.0.4314.42 x64. It is still WebView2, not an independent Chromium fork. Mica selects the bundled Runtime folder explicitly; a missing/incomplete folder in a marked portable release produces an error rather than silently switching engines. Unbundled development builds may still use Evergreen.

On first use Mica ensures Microsoft's required AppContainer read/execute permissions on the engine folder. It does not grant access to profile/vault data or disable the renderer sandbox. Extract under your own user folder if permissions prevent setup. Browser data remains under the normal Mica user profile, not beside the executable.

Fixed Version does not self-update. Engine security updates must be shipped in future Mica packages. Replace the whole application/Runtime folder when upgrading; do not mix engine versions. Existing extension compatibility limitations remain.

## Rebuild

Run package-portable.ps1. It downloads (if absent), checks the pinned CAB SHA256, extracts all files and checks Microsoft's executable signature before building/copying the runtime. Update the version, URL and independently verified hash together when updating the engine. Keep all original runtime resources/notices.

Official distribution instructions:
https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution

Official catalog:
https://developer.microsoft.com/microsoft-edge/api/webview2

Local tests verify the actual browser process path points into this Runtime folder, not the installed system runtime. A clean Windows machine without any runtime installed was not available for a separate end-to-end test.
