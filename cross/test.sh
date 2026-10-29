#!/usr/bin/env bash
# Builds Mica's cross-platform shell and runs its self-tests on macOS or Linux: the engine chooser and updater (no
# window, nothing downloaded; cross/out/engine-self-test.txt), then the browser itself. The window opens off-screen;
# results land in cross/out/self-test.txt. Pass --claude to also install Claude for Chrome (from the Chrome Web Store)
# into the throwaway test profile and check it in the AI panel.
#   ./cross/test.sh            ./cross/test.sh --claude
set -euo pipefail
cd "$(dirname "$0")"
command -v dotnet >/dev/null || { echo "Install the .NET 8 SDK first: https://dotnet.microsoft.com/download/dotnet/8.0" >&2; exit 2; }

case "$(uname -m)" in arm64|aarch64) platform=ARM64; arch=arm64 ;; *) platform=x64; arch=x64 ;; esac
case "$(uname -s)" in Darwin) rid="osx-$arch" ;; *) rid="linux-$arch" ;; esac

dotnet build -c Debug -p:Platform="$platform"
app="bin/$platform/Debug/net8.0/$rid"
chmod +x "$app/Mica" "$app/CefGlueBrowserProcess/Xilium.CefGlue.BrowserProcess" 2>/dev/null || true

mkdir -p out
"$app/Mica" --engine-self-test "$PWD/out/engine-self-test.txt" || { cat out/engine-self-test.txt; exit 1; }
grep -v '^PASS' out/engine-self-test.txt
result="$PWD/out/self-test.txt"; profile="$PWD/out/test-profile"; rm -rf "$result" "$profile"
args=(--self-test "$result" --profile "$profile")
[ "${1:-}" = "--claude" ] && args+=(--install-extension fcoeoabgfenejglbffodgkkbkcdhcgfn)
run=("$app/Mica" "${args[@]}")
# Linux without a display (CI, SSH): give it a virtual one.
if [ "$(uname -s)" = Linux ] && [ -z "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ] && command -v xvfb-run >/dev/null; then run=(xvfb-run -a "${run[@]}"); fi

set +e; "${run[@]}"; code=$?; set -e
cat "$result" 2>/dev/null || echo "No result file: the app exited early (code $code). Look at the terminal output above."
echo "Engine log: $profile/engine.log"
exit $code
