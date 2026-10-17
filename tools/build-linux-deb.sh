#!/bin/bash
set -euo pipefail
root=$(realpath "$1"); work=$(realpath "$2"); runtime=$(realpath "$3"); output=$(realpath "$4"); version=$5; edition=$6
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]
[[ $edition == Standard || $edition == Safe ]]
payload="$work/payload"
mkdir -p "$payload/runtime"
cp -a "$runtime/llama-server" "$payload/runtime/"
cp -a "$runtime/"*.so* "$payload/runtime/"
cp "$runtime/LICENSE" "$payload/runtime/LLAMA-LICENSE"
gcc -O2 -Wall -Wextra -o "$payload/runtime/BigBlueFish.RunnerGuard" "$root/code/BigBlueFish.Linux/runner-guard.c"
gcc -O2 -Wall -Wextra $(pkg-config --cflags dbus-1) -o "$payload/runtime/BigBlueFish.PortalCapture" "$root/code/BigBlueFish.Linux/portal-capture.c" $(pkg-config --libs dbus-1)
cp -a "$root/code/licenses" "$payload/"
cp "$root/code/LICENSE" "$root/code/THIRD-PARTY.md" "$payload/"
# Stage in the Linux filesystem so Debian ownership/modes and symbolic links are preserved.
stage=$(mktemp -d /tmp/bigbluefish-package.XXXXXXXX)
[[ $stage == /tmp/bigbluefish-package.* && -d $stage ]]
trap 'rm -r "$stage"' EXIT
mkdir -p "$stage/opt/bigbluefish" "$stage/DEBIAN" "$stage/usr/share/applications" "$stage/usr/bin" "$stage/usr/share/icons/hicolor/256x256/apps"
cp -a "$payload/." "$stage/opt/bigbluefish/"
find "$stage/opt/bigbluefish" -type d -exec chmod 755 {} +
find "$stage/opt/bigbluefish" -type f -exec chmod 644 {} +
chmod 755 "$stage/opt/bigbluefish/BigBlueFish.Linux" "$stage/opt/bigbluefish/runtime/llama-server" "$stage/opt/bigbluefish/runtime/BigBlueFish.RunnerGuard" "$stage/opt/bigbluefish/runtime/BigBlueFish.PortalCapture"
if [[ $edition == Standard ]]; then chmod 755 "$stage/opt/bigbluefish/BigBlueFish.Mcp"; fi
package=bigbluefish; conflict=bigbluefish-safe; suffix=''; title=蓝色大肥鱼
if [[ $edition == Safe ]]; then package=bigbluefish-safe; conflict=bigbluefish; suffix=-Safe; title=蓝色大肥鱼无MCP版; fi
cat > "$stage/DEBIAN/control" <<EOF
Package: $package
Version: $version
Section: games
Priority: optional
Architecture: amd64
Maintainer: Bai-Cute <Bai-Cute@users.noreply.github.com>
Depends: libc6 (>= 2.35), libstdc++6, libgcc-s1, libgomp1, libssl3, libicu74 | libicu72 | libicu76 | libicu78, libfontconfig1, libfreetype6, libx11-6, libxcomposite1, libxext6, libxss1, libxi6, libxcursor1, libxrandr2, libxrender1, libice6, libsm6, libdbus-1-3, libvulkan1, ffmpeg, tesseract-ocr, tesseract-ocr-chi-sim, tesseract-ocr-eng, fonts-noto-cjk, xdg-utils, pkexec | policykit-1
Recommends: xdg-desktop-portal, xdg-desktop-portal-gtk
Conflicts: $conflict
Replaces: $conflict
Homepage: https://github.com/Bai-Cute/big-blue-fish-desktop-pet
Description: Big Blue Fish desktop companion
 Animated local-model desktop companion with OCR and seasonal actions.
EOF
cat > "$stage/usr/bin/bigbluefish" <<'EOF'
#!/bin/sh
exec /opt/bigbluefish/BigBlueFish.Linux "$@"
EOF
chmod 755 "$stage/usr/bin/bigbluefish"
cat > "$stage/usr/share/applications/bigbluefish.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=$title
Exec=bigbluefish
Icon=bigbluefish
Terminal=false
Categories=Game;Utility;
EOF
ffmpeg -v error -i "$root/code/BigBlueFish.Windows/BigBlueFish.ico" -frames:v 1 -vf scale=256:256 "$stage/usr/share/icons/hicolor/256x256/apps/bigbluefish.png"
name="BigBlueFish-v${version}${suffix}-Linux-x64.deb"
test ! -e "$output/$name"
dpkg-deb --root-owner-group -Zxz --build "$stage" "$output/$name"
(cd "$output"; sha256sum "$name" > "$name.sha256")
dpkg-deb --info "$output/$name"
