#!/usr/bin/env bash
# Packages Mica's cross-platform shell. Run it on the target OS (or in CI): publishing from Windows works,
# but loses the executable bits that macOS and Linux need.
#   ./package.sh linux   -> out/mica-<version>-linux-<arch>.tar.gz (+ AppImage if appimagetool is on PATH)
#   ./package.sh macos   -> out/Mica.app and out/Mica-<version>-<arch>.dmg (unsigned unless SIGN_ID is set)
# <arch> is the machine's own (x64 or arm64); set ARCH=x64 or ARCH=arm64 to choose.
set -euo pipefail
cd "$(dirname "$0")"
target="${1:?usage: package.sh linux|macos}"
# The same version as the Windows app (its AppVersion), which a release carries.
version="$(sed -n 's:.*AppVersion="\([0-9.]*\)".*:\1:p' ../src/BrowserUpdates.cs | head -n 1)"
mkdir -p out
case "${ARCH:-$(uname -m)}" in arm64|aarch64) platform=ARM64; arch=arm64 ;; *) platform=x64; arch=x64 ;; esac

# "dotnet publish -o" would also send CefGlue's helper process (a project reference published for each OS) into that
# folder, so Mica publishes to its own publish folder, with the helper under CefGlueBrowserProcess/, and is copied.
# Packages carry the .NET runtime (self-contained): a machine without .NET would otherwise refuse to start Mica
# ("You must install .NET"), and from Finder the app just closes without a word. Dev builds (test.sh) stay small.
publish() {
  dotnet publish -c Release -r "$1" -p:Platform=$platform --self-contained true
  rm -rf "$2"; mkdir -p "$2"; cp -a "bin/$platform/Release/net8.0/$1/publish/." "$2/"
}

case "$target" in
linux)
  rid=linux-$arch; app="out/mica-$version-$rid"
  publish "$rid" "$app"
  chmod +x "$app/Mica" "$app/CefGlueBrowserProcess/Xilium.CefGlue.BrowserProcess"
  # Chromium's setuid sandbox helper, when the CEF package ships one, must be owned by root with 4755.
  if [ -f "$app/CefGlueBrowserProcess/chrome-sandbox" ]; then
    echo "note: run 'sudo chown root:root chrome-sandbox && sudo chmod 4755 chrome-sandbox' after installing"
  fi
  cp ../src/Assets/Brand/Mica.ico "$app/mica.ico"
  cat > "$app/mica.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Mica
Comment=A light, calm web browser
Exec=Mica %U
Icon=mica
Categories=Network;WebBrowser;
MimeType=x-scheme-handler/http;x-scheme-handler/https;text/html;
StartupWMClass=Mica
EOF
  tar -C out -czf "out/mica-$version-$rid.tar.gz" "$(basename "$app")"
  if command -v appimagetool >/dev/null; then
    dir="out/Mica.AppDir"; rm -rf "$dir"; mkdir -p "$dir/usr/lib/mica"
    cp -a "$app/." "$dir/usr/lib/mica/"; cp "$app/mica.desktop" "$dir/"
    # AppImage wants a PNG icon; convert the .ico when ImageMagick is there.
    if command -v convert >/dev/null; then convert "$app/mica.ico[0]" "$dir/mica.png"; else touch "$dir/mica.png"; fi
    printf '#!/bin/sh\nexec "$(dirname "$(readlink -f "$0")")/usr/lib/mica/Mica" "$@"\n' > "$dir/AppRun"; chmod +x "$dir/AppRun"
    appimage_arch=$([ $arch = arm64 ] && echo aarch64 || echo x86_64)
    # Everyone who runs the AppImage must be able to read it (appimagetool checks).
    chmod -R a+rX "$dir"; chmod a+rx "$dir/AppRun"
    ARCH=$appimage_arch appimagetool "$dir" "out/Mica-$version-$appimage_arch.AppImage"
  fi
  ;;
macos)
  # Chromium 134 comes for both Intel (osx-x64) and Apple Silicon (osx-arm64).
  rid=osx-$arch; pub="out/publish-$rid"; bundle="out/Mica.app"
  rm -rf "$bundle"; publish "$rid" "$pub"
  mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
  cp -a "$pub/." "$bundle/Contents/MacOS/"
  chmod +x "$bundle/Contents/MacOS/Mica" "$bundle/Contents/MacOS/CefGlueBrowserProcess/Xilium.CefGlue.BrowserProcess"
  if command -v sips >/dev/null; then sips -s format icns ../src/Assets/Brand/Mica.ico --out "$bundle/Contents/Resources/Mica.icns" >/dev/null || true; fi
  cat > "$bundle/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>Mica</string>
  <key>CFBundleDisplayName</key><string>Mica</string>
  <key>CFBundleIdentifier</key><string>browser.mica.desktop</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleExecutable</key><string>Mica</string>
  <key>CFBundleIconFile</key><string>Mica</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSCameraUsageDescription</key><string>A website you visit wants to use your camera. Mica asks you first.</string>
  <key>NSMicrophoneUsageDescription</key><string>A website you visit wants to use your microphone. Mica asks you first.</string>
  <key>NSLocationUsageDescription</key><string>A website you visit wants to know your location. Mica asks you first.</string>
  <key>NSLocationWhenInUseUsageDescription</key><string>A website you visit wants to know your location. Mica asks you first.</string>
  <key>CFBundleURLTypes</key><array><dict>
    <key>CFBundleURLName</key><string>Web site URL</string>
    <key>CFBundleURLSchemes</key><array><string>http</string><string>https</string></array>
  </dict></array>
</dict></plist>
EOF
  if [ -n "${SIGN_ID:-}" ]; then
    # Hardened runtime + JIT entitlements are what .NET and V8 need; notarize afterwards with notarytool.
    cat > out/entitlements.plist <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>com.apple.security.cs.allow-jit</key><true/>
  <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
  <key>com.apple.security.cs.disable-library-validation</key><true/>
  <key>com.apple.security.device.camera</key><true/>
  <key>com.apple.security.device.audio-input</key><true/>
  <key>com.apple.security.personal-information.location</key><true/>
</dict></plist>
EOF
    sign=(--force --options runtime --timestamp --sign "$SIGN_ID"); entitled=(--entitlements out/entitlements.plist)
  else
    # Unsigned builds are still signed ad-hoc, all of it. Apple Silicon runs no unsigned code, and an app whose
    # pieces aren't sealed together is "damaged" to Gatekeeper once downloaded, with no way to open it; sealed
    # ad-hoc it is only "unverified", which Privacy & Security lets the user open.
    sign=(--force --timestamp=none --sign -); entitled=()
  fi
  # Inside out, deepest first, then each framework, then the app. Nothing in the app changes after this.
  # .NET keeps its managed files (.dll, .json) beside the executable in Contents/MacOS, where macOS takes every file
  # for code: each one needs a signature of its own (kept in the file's extended attributes), or the app's seal is
  # invalid and Gatekeeper calls the app damaged. Inside a framework, its own seal covers what isn't code.
  find "$bundle/Contents" -type f | awk '{print length($0) " " $0}' | sort -rn | cut -d' ' -f2- | while IFS= read -r f; do
    kind="$(file -b "$f")"
    # The app's own executable is signed with the app, last.
    [ "$f" = "$bundle/Contents/MacOS/Mica" ] && continue
    case "$f" in *.framework/*) echo "$kind" | grep -q 'Mach-O' || continue ;; "$bundle/Contents/MacOS/"*) ;; *) echo "$kind" | grep -q 'Mach-O' || continue ;; esac
    if echo "$kind" | grep -q 'Mach-O.*executable'; then codesign "${sign[@]}" ${entitled[@]+"${entitled[@]}"} "$f"; else codesign "${sign[@]}" "$f"; fi
  done
  find "$bundle/Contents" -type d \( -name '*.framework' -o -name '*.app' \) | awk '{print length($0) " " $0}' | sort -rn | cut -d' ' -f2- | while IFS= read -r d; do
    codesign "${sign[@]}" "$d"
  done
  codesign "${sign[@]}" ${entitled[@]+"${entitled[@]}"} "$bundle"
  codesign --verify --deep --strict --verbose=2 "$bundle"
  spctl -a -vv "$bundle" || true  # only reports: an unsigned (ad-hoc) app is expected to be rejected here
  # The usual drag-to-install window: Mica beside a shortcut to Applications, big icons, no toolbar. Finder lays
  # the window out on a writable copy of the image; where Finder can't be scripted (some build machines), the
  # image still opens to the same two icons, just in Finder's default arrangement.
  dmg="out/Mica-$version-$arch.dmg"; stage="out/dmg-stage"; scratch="out/Mica-scratch.dmg"
  rm -rf "$stage" "$dmg" "$scratch"; mkdir -p "$stage"
  cp -a "$bundle" "$stage/"; ln -s /Applications "$stage/Applications"
  hdiutil create -volname Mica -srcfolder "$stage" -ov -fs HFS+ -format UDRW "$scratch"
  attached="$(hdiutil attach -readwrite -noverify -noautoopen "$scratch")"
  device="$(echo "$attached" | awk 'NR==1 {print $1}')"; volume="$(echo "$attached" | grep -o '/Volumes/.*' | head -n 1)"
  [ -n "$volume" ] && mdutil -i off "$volume" >/dev/null 2>&1 || true
  if [ -f "$bundle/Contents/Resources/Mica.icns" ] && [ -n "$volume" ]; then
    cp "$bundle/Contents/Resources/Mica.icns" "$volume/.VolumeIcon.icns"
    command -v SetFile >/dev/null && SetFile -a C "$volume" || true
  fi
  disk=Mica
  osascript <<APPLESCRIPT &
tell application "Finder"
  tell disk "$disk"
    open
    set current view of container window to icon view
    set toolbar visible of container window to false
    set statusbar visible of container window to false
    set the bounds of container window to {200, 120, 740, 480}
    set viewOptions to the icon view options of container window
    set arrangement of viewOptions to not arranged
    set icon size of viewOptions to 112
    set text size of viewOptions to 13
    set position of item "Mica.app" of container window to {140, 170}
    set position of item "Applications" of container window to {400, 170}
    update without registering applications
    delay 1
    close
  end tell
end tell
APPLESCRIPT
  finder=$!; ( sleep 90; kill $finder 2>/dev/null ) & guard=$!
  if wait $finder; then echo "dmg: window laid out"; else echo "dmg: Finder couldn't be scripted here; default layout"; fi
  kill $guard 2>/dev/null || true
  if [ -n "$volume" ] && [ -f "$volume/.DS_Store" ]; then echo "dmg: layout saved (.DS_Store)"; fi
  # Finder or Spotlight can still hold the volume for a moment after the layout ("Resource busy").
  sync; detached=
  for try in 1 2 3 4 5; do
    if hdiutil detach "$device" -quiet; then detached=1; break; fi
    sleep $((try * 2))
  done
  [ -n "$detached" ] || hdiutil detach "$device" -force
  hdiutil convert "$scratch" -format UDZO -imagekey zlib-level=9 -o "$dmg"
  rm -rf "$stage" "$scratch"
  ;;
*) echo "unknown target: $target" >&2; exit 2 ;;
esac
