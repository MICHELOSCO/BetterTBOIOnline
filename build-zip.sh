#!/bin/sh
# Builds BetterTBOIOnline-per-amici.zip from the repo sources (mods + dist-src/LEGGIMI.txt).
set -e
cd "$(dirname "$0")"
P="${TMP:-/tmp}/pack/BetterTBOIOnline"
rm -rf "${TMP:-/tmp}/pack" && mkdir -p "$P/mods/bettertboionline-diagnostics"
cp -r thefuture-onlinefix "$P/mods/"
cp diagnostics-mod/main.lua diagnostics-mod/metadata.xml "$P/mods/bettertboionline-diagnostics/"
cp dist-src/LEGGIMI.txt "$P/LEGGIMI.txt"
rm -f BetterTBOIOnline-per-amici.zip
(cd "${TMP:-/tmp}/pack" && powershell -NoProfile -Command "Compress-Archive -Force -Path 'BetterTBOIOnline' -DestinationPath '$(cygpath -w "$OLDPWD")\BetterTBOIOnline-per-amici.zip'")
