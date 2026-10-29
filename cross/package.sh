#!/usr/bin/env bash
# Packages Mica's cross-platform shell. Run it on the target OS (or in CI): publishing from Windows works,
# but loses the executable bits that macOS and Linux need.
#   ./package.sh linux   -> out/mica-<version>-linux-<arch>.tar.gz (+ AppImage if appimagetool is on PATH)
#   ./package.sh macos   -> out/Mica.app and out/Mica-<version>-<arch>.dmg (unsigned unless SIGN_ID is set)
# <arch> is the machine's own (x64 or arm64); set ARCH=x64 or ARCH=arm64 to choose.
set -euo pipefail
cd "$(dirname "$0")"
target="${1:?usage: package.sh linux|macos}"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Mica.Cross.csproj)"
mkdir -p out
case "${ARCH:-$(uname -m)}" in arm64|aarch64) platform=ARM64; arch=arm64 ;; *) platform=x64; arch=x64 ;; esac

# "dotnet publish -o" would also send CefGlue's helper process (a project reference published for each OS) into that
# folder, so Mica publishes to its own publish folder, with the helper under CefGlueBrowserProcess/, and is copied.
publish() {
  dotnet publish -c Release -r "$1" -p:Platform=$platform
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
</dict></plist>
EOF
    codesign --force --deep --options runtime --timestamp --entitlements out/entitlements.plist --sign "$SIGN_ID" "$bundle"
  fi
  rm -f "out/Mica-$version-$arch.dmg"
  hdiutil create -volname Mica -srcfolder "$bundle" -ov -format UDZO "out/Mica-$version-$arch.dmg"
  ;;
*) echo "unknown target: $target" >&2; exit 2 ;;
esac
