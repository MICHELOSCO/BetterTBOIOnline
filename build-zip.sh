#!/bin/sh
# Builds BetterTBOIOnline-per-amici.zip from the repo sources (mods/bto-* + diagnostics + dist-src/LEGGIMI.txt)
# and BTO-Sync-<version>.zip, the asset of the "sync-v<version>" release that BTO Sync updates itself from.
set -e
cd "$(dirname "$0")"
# check: the handshake block must be identical in every BTO mod, or parties with different mods would agree
n=$(for f in mods/*/main.lua; do sed -n '/BTO handshake begin/,/BTO handshake end/p' "$f" | md5sum; done | sort -u | wc -l)
[ "$n" -eq 1 ] || { echo "handshake block differs between mods" >&2; exit 1; }
P="${TMP:-/tmp}/pack/BetterTBOIOnline"
rm -rf "${TMP:-/tmp}/pack" && mkdir -p "$P/mods/bettertboionline-diagnostics"
cp -r mods/bto-* "$P/mods/"
cp diagnostics-mod/main.lua diagnostics-mod/metadata.xml "$P/mods/bettertboionline-diagnostics/"
cp dist-src/LEGGIMI.txt "$P/LEGGIMI.txt"
(cd sync && dotnet build -c Release -nologo -v q >/dev/null)
B=sync/bin/Release/net48
mkdir -p "$P/BTO-Sync"
cp "$B/BTO-Sync.exe" "$B/BTO-Sync.exe.config" "$B/Facepunch.Steamworks.Win64.dll" "$B/steam_api64.dll" "$P/BTO-Sync/"
cp sync/LEGGIMI.txt "$P/BTO-Sync/LEGGIMI.txt"
rm -f BetterTBOIOnline-per-amici.zip
(cd "${TMP:-/tmp}/pack" && powershell -NoProfile -Command "Compress-Archive -Force -Path 'BetterTBOIOnline' -DestinationPath '$(cygpath -w "$OLDPWD")\BetterTBOIOnline-per-amici.zip'")
V=$(grep -o '<Version>[^<]*' sync/BtoSync.csproj | cut -d'>' -f2)
rm -f BTO-Sync-*.zip
(cd "$P" && powershell -NoProfile -Command "Compress-Archive -Force -Path 'BTO-Sync' -DestinationPath '$(cygpath -w "$OLDPWD")\BTO-Sync-$V.zip'")
echo "BetterTBOIOnline-per-amici.zip, BTO-Sync-$V.zip"
