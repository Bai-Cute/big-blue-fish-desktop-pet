#!/bin/bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
if [[ "$(uname -s)" != Darwin || "$(uname -m)" != arm64 ]]; then
  echo 'This build currently requires an Apple Silicon Mac.' >&2; exit 1
fi
if [[ $# -gt 1 ]]; then
  echo 'Usage: tools/build-macos.sh [version]' >&2
  exit 2
fi
version="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$root/Version.props" | head -n 1)"
if [[ $# -eq 1 ]]; then
  if [[ ! "$1" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo 'Version must be a numeric major.minor.patch value.' >&2
    exit 2
  fi
  version="$1"
fi
[[ -n "$version" ]]
output="$root/release/macos"
mkdir -p "$output"
output="$(cd "$output" && pwd)"
target="$output/BigBlueFish.app"
backup="$root/release/.macos-backup.$$"
if [[ -e "$backup" ]]; then echo "Refusing to use existing backup path: $backup" >&2; exit 1; fi
stage="$(mktemp -d "$root/release/.macos-stage.XXXXXX")"
app="$stage/BigBlueFish.app"
icon_tmp=""
cleanup() {
  local status=$?
  rm -rf "$stage"
  if [[ -n "$icon_tmp" ]]; then rm -rf "$icon_tmp"; fi
  if [[ -e "$backup" ]]; then
    if [[ -e "$target" ]]; then rm -rf "$backup"; else mv "$backup" "$target"; fi
  fi
  exit "$status"
}
trap cleanup EXIT
command -v dotnet >/dev/null
xcrun --find clang >/dev/null
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
dotnet publish "$root/code/BigBlueFish.Mac/BigBlueFish.Mac.csproj" -c Release -r osx-arm64 \
  --self-contained true -p:RestoreLockedMode=true "-p:Version=$version" --nologo -o "$app/Contents/MacOS"
cat > "$app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>BigBlueFish</string>
<key>CFBundleDisplayName</key><string>蓝色大肥鱼</string>
<key>CFBundleIdentifier</key><string>io.github.bai-cute.bigbluefish.mac</string>
<key>CFBundleVersion</key><string>$version</string>
<key>CFBundleShortVersionString</key><string>$version</string>
<key>CFBundleExecutable</key><string>BigBlueFish.Mac</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleIconFile</key><string>BigBlueFish.icns</string>
<key>LSMinimumSystemVersion</key><string>12.0</string>
<key>LSUIElement</key><true/>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
EOF
icon_tmp="$(mktemp -d "${TMPDIR:-/tmp}/bbf-icons.XXXXXX")"
icons="$icon_tmp/BigBlueFish.iconset"
mkdir -p "$icons"
for size in 16 32 128 256 512; do
  sips -s format png -z "$size" "$size" "$root/code/BigBlueFish.Windows/BigBlueFish.ico" --out "$icons/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -s format png -z "$double" "$double" "$root/code/BigBlueFish.Windows/BigBlueFish.ico" --out "$icons/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$icons" -o "$app/Contents/Resources/BigBlueFish.icns"
cp "$root/LICENSE" "$app/Contents/Resources/LICENSE.txt"
cp -R "$root/code/licenses" "$app/Contents/Resources/licenses"
cp "$root/code/THIRD-PARTY.md" "$app/Contents/Resources/THIRD-PARTY.md"
plutil -lint "$app/Contents/Info.plist"
if [[ -e "$target" ]]; then mv "$target" "$backup"; fi
mv "$app" "$target"
rm -rf "$backup"
rm -rf "$stage"
rm -rf "$icon_tmp"
icon_tmp=""
trap - EXIT
echo "Built: $target"
echo 'Local development bundle: requires Homebrew FFmpeg (brew install ffmpeg) or BIGBLUEFISH_FFMPEG.'
echo 'Not Developer ID signed or notarized; do not distribute as a standalone release.'
