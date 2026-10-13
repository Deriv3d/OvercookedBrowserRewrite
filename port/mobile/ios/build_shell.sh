#!/bin/bash
# Builds the iPhone app shell, unsigned, with no game files in it
# (port/mobile/ios/README.md). Runs on macOS with Xcode (the GitHub workflow
# .github/workflows/ios-shell.yml). SideStore signs the app when it's installed.
#
#   port/mobile/ios/build_shell.sh <out folder> [build number]
#
# Writes <out folder>/Overcooked-shell.ipa.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="${1:?out folder}"
number="${2:-1}"
app="$out/Payload/Overcooked.app"

rm -rf "$out"
mkdir -p "$app"
sdk="$(xcrun --sdk iphoneos --show-sdk-path)"
xcrun --sdk iphoneos swiftc -target arm64-apple-ios15.0 -sdk "$sdk" -O -module-name Overcooked \
  -o "$app/Overcooked" "$here"/Sources/*.swift

sed "s/__BUILD__/$number/" "$here/Info.plist" > "$app/Info.plist"
plutil -replace DTPlatformName -string iphoneos "$app/Info.plist"
plutil -replace DTSDKName -string "iphoneos$(xcrun --sdk iphoneos --show-sdk-version)" "$app/Info.plist"
plutil -replace DTPlatformVersion -string "$(xcrun --sdk iphoneos --show-sdk-version)" "$app/Info.plist"
plutil -lint "$app/Info.plist"
printf 'APPL????' > "$app/PkgInfo"

# Checks: an arm64 iOS binary, and the plist the app reads at launch.
lipo -info "$app/Overcooked"
otool -l "$app/Overcooked" | grep -A4 LC_BUILD_VERSION | grep -E "platform|minos"
otool -L "$app/Overcooked"
plutil -p "$app/Info.plist"

(cd "$out" && zip -qry Overcooked-shell.ipa Payload)
ls -l "$out/Overcooked-shell.ipa"
