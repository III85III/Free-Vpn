#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
test "$(uname -s)" = Darwin || { echo 'Build this package on macOS.' >&2; exit 1; }
root="$PWD"
output="$root/.build/macos"
app="$output/III85III VPN.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources/core" "$output/slices"
cp macOS/Info.plist "$app/Contents/Info.plist"
for arch in arm64 amd64; do
    swift_arch="$arch"
    if [ "$arch" = amd64 ]; then swift_arch=x86_64; fi
    (cd core && GOOS=darwin GOARCH="$arch" CGO_ENABLED=0 go build -trimpath \
        -ldflags='-s -w -X github.com/III85III/III85III-VPN/core/internal/version.Current=1.0.0' \
        -o "$output/slices/core-$arch" ./cmd/firefox-vpn-core)
    (cd core && GOOS=darwin GOARCH="$arch" CGO_ENABLED=0 go build -trimpath \
        -ldflags='-s -w -X github.com/III85III/III85III-VPN/core/internal/version.Current=1.0.0' \
        -o "$output/slices/tun-$arch" ./cmd/firefox-vpn-tun)
    xcrun swiftc -O -target "$swift_arch-apple-macosx13.0" -framework Cocoa \
        macOS/main.swift -o "$output/slices/app-$arch"
done
lipo -create "$output/slices/core-arm64" "$output/slices/core-amd64" -output "$app/Contents/Resources/core/iii85iii-core"
lipo -create "$output/slices/tun-arm64" "$output/slices/tun-amd64" -output "$app/Contents/Resources/core/firefox-vpn-tun"
lipo -create "$output/slices/app-arm64" "$output/slices/app-amd64" -output "$app/Contents/MacOS/III85III VPN"
(cd core && go run ../tools/macos-secret/main.go) > "$app/Contents/Resources/dashboard-auth.json"
cp -R licenses "$app/Contents/Resources/licenses"
cp macOS/README.md "$app/Contents/Resources/Read me.md"
# Reuse the product's existing icon; only standard macOS icon sizes change.
mkdir -p "$output/AppIcon.iconset"
for size in 16 32 128 256 512; do
    sips -z "$size" "$size" native/assets/brand/iii85iii-luxury.png --out "$output/AppIcon.iconset/icon_${size}x${size}.png" >/dev/null
    double=$((size * 2))
    sips -z "$double" "$double" native/assets/brand/iii85iii-luxury.png --out "$output/AppIcon.iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$output/AppIcon.iconset" -o "$app/Contents/Resources/AppIcon.icns"
/usr/libexec/PlistBuddy -c 'Add :CFBundleIconFile string AppIcon' "$app/Contents/Info.plist"
chmod 755 "$app/Contents/MacOS/III85III VPN" "$app/Contents/Resources/core/iii85iii-core" "$app/Contents/Resources/core/firefox-vpn-tun"
codesign --force --sign - "$app/Contents/Resources/core/iii85iii-core"
codesign --force --sign - "$app/Contents/Resources/core/firefox-vpn-tun"
codesign --force --sign - "$app"
codesign --verify --deep --strict "$app"
plutil -lint "$app/Contents/Info.plist"
echo 'Built universal macOS app. Ad-hoc signed; not Apple notarized.'
