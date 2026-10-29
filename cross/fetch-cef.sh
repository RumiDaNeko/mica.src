#!/usr/bin/env bash
# Downloads the CEF build named in cef.lock for one runtime identifier, checks its SHA-1 and unpacks it into
# cef/<rid>/ in the layout the build copies from (the same layout the old cef.redist.* packages had):
#   linux: libcef.so, the other Release files, resources and locales/ side by side (stripped when `strip` exists)
#   macOS: libcef.dylib (the framework binary), its Libraries, and Resources/ with the English locale.pak
# Mica.Cross.csproj runs it before a build when cef/<rid>/.version doesn't match cef.lock.
#   ./fetch-cef.sh osx-arm64        (osx-x64, osx-arm64, linux-x64, linux-arm64)
set -euo pipefail
cd "$(dirname "$0")"
rid="${1:?usage: fetch-cef.sh <osx-x64|osx-arm64|linux-x64|linux-arm64>}"
case "$rid" in
  osx-x64) platform=macosx64 ;; osx-arm64) platform=macosarm64 ;;
  linux-x64) platform=linux64 ;; linux-arm64) platform=linuxarm64 ;;
  *) echo "fetch-cef.sh: unsupported runtime identifier $rid" >&2; exit 2 ;;
esac
version="$(awk '$1=="version"{print $2}' cef.lock)"
sha1="$(awk -v p="$platform" '$1==p{print $2}' cef.lock)"
[ -n "$version" ] && [ -n "$sha1" ] || { echo "fetch-cef.sh: cef.lock has no entry for $platform" >&2; exit 2; }

dest="cef/$rid"
if [ -f "$dest/.version" ] && [ "$(cat "$dest/.version")" = "$version" ]; then exit 0; fi
mkdir -p cef; printf '*\n' > cef/.gitignore

name="cef_binary_${version}_${platform}_minimal.tar.bz2"
archive="cef/$name"; staging="cef/.unpack-$rid"
if [ ! -f "$archive" ]; then
  echo "Downloading CEF $version for $platform..."
  curl -fL --retry 3 -o "$archive.part" "https://cef-builds.spotifycdn.com/${name//+/%2B}"
  mv "$archive.part" "$archive"
fi
actual="$( (command -v sha1sum >/dev/null && sha1sum "$archive" || shasum -a 1 "$archive") | cut -d' ' -f1)"
if [ "$actual" != "$sha1" ]; then
  rm -f "$archive"; echo "fetch-cef.sh: $name has SHA-1 $actual, cef.lock expects $sha1" >&2; exit 1
fi

rm -rf "$staging" "$dest"; mkdir -p "$staging" "$dest"
tar -xjf "$archive" -C "$staging"
root="$(find "$staging" -mindepth 1 -maxdepth 1 -type d | head -n1)"
case "$rid" in
osx-*)
  fw="$root/Release/Chromium Embedded Framework.framework"
  mkdir -p "$dest/Resources"
  cp "$fw/Chromium Embedded Framework" "$dest/libcef.dylib"
  cp "$fw/Libraries/"* "$dest/"
  find "$fw/Resources" -mindepth 1 -maxdepth 1 -type f -exec cp {} "$dest/Resources/" \;
  cp "$fw/Resources/en.lproj/"* "$dest/Resources/"
  ;;
linux-*)
  cp -a "$root/Release/." "$dest/"
  cp -a "$root/Resources/." "$dest/"
  # The minimal distribution keeps symbols (about 1 GB); stripped it is what the old package shipped.
  if command -v strip >/dev/null && [ "$(uname -m)" = "$( [ "$rid" = linux-arm64 ] && echo aarch64 || echo x86_64)" ]; then
    for lib in libcef.so libEGL.so libGLESv2.so libvk_swiftshader.so libvulkan.so.1; do [ -f "$dest/$lib" ] && strip -s "$dest/$lib"; done
  fi
  ;;
esac
rm -rf "$staging" "$archive"
printf '%s' "$version" > "$dest/.version"
echo "CEF $version is in $dest"
