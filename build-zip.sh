#!/bin/sh
# Builds BTO-Sync-<version>.zip, the asset of the "sync-v<version>" release that BTO Sync updates itself from.
# Players get only this: BTO Sync installs the BTO mods from the per-mod release assets "<folder>-<version>.zip"
# (git archive --prefix=<folder>/ master:mods/<folder>), each one tied to the Workshop mod it fixes (Core.Ties).
set -e
cd "$(dirname "$0")"
# check: the handshake block must be identical in every BTO mod, or parties with different mods would agree
n=$(for f in mods/*/main.lua; do sed -n '/BTO handshake begin/,/BTO handshake end/p' "$f" | md5sum; done | sort -u | wc -l)
[ "$n" -eq 1 ] || { echo "handshake block differs between mods" >&2; exit 1; }
P="${TMP:-/tmp}/pack"
rm -rf "$P" && mkdir -p "$P/BTO-Sync"
(cd sync && dotnet build -c Release -nologo -v q >/dev/null)
B=sync/bin/Release/net48
cp "$B/BTO-Sync.exe" "$B/BTO-Sync.exe.config" "$B/Facepunch.Steamworks.Win64.dll" "$B/steam_api64.dll" "$P/BTO-Sync/"
cp sync/LEGGIMI.txt "$P/BTO-Sync/LEGGIMI.txt"
V=$(grep -o '<Version>[^<]*' sync/BtoSync.csproj | cut -d'>' -f2)
rm -f BTO-Sync-*.zip
(cd "$P" && powershell -NoProfile -Command "Compress-Archive -Force -Path 'BTO-Sync' -DestinationPath '$(cygpath -w "$OLDPWD")\BTO-Sync-$V.zip'")
echo "BTO-Sync-$V.zip"
