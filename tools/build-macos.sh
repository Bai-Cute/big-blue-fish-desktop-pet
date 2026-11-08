#!/bin/bash
# Build a self-contained Apple Silicon .app from source. See docs/MACOS.md.
# Usage: tools/build-macos.sh [Standard|Safe] [output directory]
set -euo pipefail
root=$(cd "$(dirname "$0")/.." && pwd)
edition=${1:-Standard}; output=${2:-$root/release}
[[ $edition == Standard || $edition == Safe ]]
[[ $(uname -s) == Darwin && $(uname -m) == arm64 ]] || { echo "需要 Apple Silicon Mac。" >&2; exit 1; }
dotnet=${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}
version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Version.props")
cache="$root/.work/macos-download"; work="$root/.work/macos-$edition"
title=蓝色大肥鱼; [[ $edition == Safe ]] && title=蓝色大肥鱼无MCP版
app="$output/$title.app"; payload="$work/$title.app/Contents/MacOS"
mkdir -p "$cache" "$output"; rm -rf "$work"; mkdir -p "$payload/runtime" "$work/$title.app/Contents/Resources"

fetch() { # url sha256 -> cached path, verified
    local file="$cache/$(basename "$1")"
    if [[ ! -f $file ]]; then curl -fL --retry 3 -o "$file.partial" "$1"; mv "$file.partial" "$file"; fi
    echo "$2  $file" | shasum -a 256 -c --status || { echo "下载校验失败：$file" >&2; rm -f "$file"; exit 1; }
    echo "$file"
}

# Program (same publish flags as the Linux package)
"$dotnet" publish "$root/code/BigBlueFish.Mac/BigBlueFish.Mac.csproj" -c Release -r osx-arm64 --self-contained true "-p:CompanionEdition=$edition" -p:RestoreLockedMode=true -o "$payload" --nologo
if [[ $edition == Standard ]]; then
    "$dotnet" publish "$root/code/BigBlueFish.Mcp/BigBlueFish.Mcp.csproj" -c Release -r osx-arm64 --self-contained true -p:RestoreLockedMode=true -o "$payload" --nologo
fi

# llama.cpp: the build Windows pins (b10809), macOS arm64 with Metal
llama=$(fetch https://github.com/ggml-org/llama.cpp/releases/download/b10809/llama-b10809-bin-macos-arm64.tar.gz 7d692df9e1e386e62f1c12b843903218041e6cd74c9415aa39a7ed3176f9eaa2)
tar -xzf "$llama" -C "$work"
cp -a "$work/llama-b10809/llama-server" "$work/llama-b10809/"*.dylib "$payload/runtime/"
cp "$work/llama-b10809/LICENSE" "$payload/runtime/LLAMA-LICENSE"

# GStreamer: the pinned 1.29.2 subset Windows uses, from the matching macOS wheels
manifest="$root/tools/gstreamer-runtime-macos.json"; wheels=()
while read -r url sha; do wheels+=("$(fetch "$url" "$sha")"); done < <(python3 -c 'import json,sys;[print(a["url"],a["sha256"]) for a in json.load(open(sys.argv[1]))["archives"]]' "$manifest")
python3 - "$manifest" "$payload/media/gstreamer" "${wheels[@]}" <<'EOF'
import hashlib, json, os, sys, zipfile
manifest, target, *wheels = sys.argv[1:]
for archive, wheel in zip(json.load(open(manifest))["archives"], wheels):
    with zipfile.ZipFile(wheel) as package:
        for item in archive["files"]:
            data = package.read(item["source"])
            if hashlib.sha256(data).hexdigest() != item["sha256"]: sys.exit("动画组件文件校验失败：" + item["target"])
            path = os.path.join(target, item["target"]); os.makedirs(os.path.dirname(path), exist_ok=True)
            open(path, "wb").write(data); os.chmod(path, 0o755)
EOF
cp "$manifest" "$payload/media/gstreamer/runtime-manifest.json"

# Native helpers: process-exit guard (shared with Linux) and Apple Vision OCR
clang -O2 -Wall -Wextra -o "$payload/runtime/BigBlueFish.RunnerGuard" "$root/code/BigBlueFish.Linux/runner-guard.c"
swiftc -O -o "$payload/runtime/BigBlueFish.MacOcr" "$root/code/BigBlueFish.Mac/mac-ocr.swift"
cp -a "$root/code/licenses" "$payload/"; cp "$root/code/LICENSE" "$root/code/THIRD-PARTY.md" "$payload/"

# Bundle
sips -s format png "$root/code/BigBlueFish.Windows/BigBlueFish.ico" --out "$work/icon.png" >/dev/null
sips -z 512 512 "$work/icon.png" >/dev/null; sips -s format icns "$work/icon.png" --out "$work/$title.app/Contents/Resources/BigBlueFish.icns" >/dev/null
cat > "$work/$title.app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>$title</string>
<key>CFBundleDisplayName</key><string>$title</string>
<key>CFBundleIdentifier</key><string>io.github.bai-cute.bigbluefish</string>
<key>CFBundleExecutable</key><string>BigBlueFish.Mac</string>
<key>CFBundleIconFile</key><string>BigBlueFish</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>$version</string>
<key>CFBundleVersion</key><string>$version</string>
<key>LSMinimumSystemVersion</key><string>14.0</string>
<key>LSUIElement</key><true/>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
EOF
# shortcut: ad-hoc signature only; Developer ID signing and notarization need the maintainers' certificate.
codesign --force --deep --sign - "$work/$title.app"
rm -rf "$app"; mv "$work/$title.app" "$app"
echo "已生成：$app"
