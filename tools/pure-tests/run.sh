#!/usr/bin/env bash
# Runs the parser / pure-logic tests WITHOUT nuget.org (blocked in the Claude sandbox: 403).
# See CLAUDE.md section 4 for the why. What it does, all in user space:
#   1. downloads the .NET 8 SDK debs from the Ubuntu archive (apt-get download) and unpacks
#      them with dpkg -x - no root needed;
#   2. compiles HtmlAgilityPack from its GitHub source (codeload.github.com is reachable);
#   3. compiles the files listed in files.txt together with Shim.cs (a minimal stand-in for
#      xUnit's [Fact]/[Theory]/[InlineData] and the Shouldly extension methods) and Runner.cs
#      (reflection test runner), then runs every test in MainCore.Test.* namespaces from the
#      MainCore.Test directory (fixture paths are relative to it).
# NOT covered: anything that needs Immediate.Handlers-generated code, EF Core, Selenium,
# StronglyTypedId, ReactiveUI - i.e. handlers, tasks, view models. Use the extra csc pass in
# CLAUDE.md section 4 for a syntax-level check of those.
# Shim caveat: avoid IEnumerable<T>.ShouldBe(array) in tests - ambiguous with ShouldBe<T>(T,T);
# compare string.Join(...) instead (works identically under real Shouldly).
set -euo pipefail

REPO="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="${WORK:-/tmp/dn}"
HERE="$REPO/tools/pure-tests"
export DOTNET_ROOT="$WORK/root/usr/lib/dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export PATH="$PATH:$DOTNET_ROOT"
mkdir -p "$WORK"

if [ ! -x "$DOTNET_ROOT/dotnet" ]; then
  echo ">> installing .NET 8 SDK into $WORK (user space)"
  mkdir -p "$WORK/lists/partial" "$WORK/cache/archives/partial" "$WORK/debs" "$WORK/root"
  APT="-o Dir::State::Lists=$WORK/lists -o Dir::Cache=$WORK/cache"
  # nodesource et al. may 403 - irrelevant, the Ubuntu archive lists are what matter.
  apt-get $APT update >/dev/null 2>&1 || true
  (cd "$WORK/debs" && apt-get $APT download dotnet-sdk-8.0 dotnet-runtime-8.0 dotnet-host-8.0 \
     dotnet-hostfxr-8.0 dotnet-targeting-pack-8.0 dotnet-apphost-pack-8.0 \
     netstandard-targeting-pack-2.1-8.0 >/dev/null)
  for f in "$WORK"/debs/*.deb; do dpkg -x "$f" "$WORK/root"; done
fi

CSC="$(find "$DOTNET_ROOT/sdk" -name csc.dll | head -1)"
REF="$(find "$DOTNET_ROOT/packs/Microsoft.NETCore.App.Ref" -type d -path '*/ref/net8.0' | head -1)"
REFS="$(ls "$REF"/*.dll | sed 's/^/-r:/')"

if [ ! -f "$WORK/build/HtmlAgilityPack.dll" ]; then
  echo ">> building HtmlAgilityPack from source"
  mkdir -p "$WORK/build" "$WORK/hap"
  curl -sSL -m 120 -o "$WORK/hap.zip" https://codeload.github.com/zzzprojects/html-agility-pack/zip/refs/heads/master
  unzip -q -o "$WORK/hap.zip" -d "$WORK/hap"
  dotnet "$CSC" -nologo -target:library -out:"$WORK/build/HtmlAgilityPack.dll" -langversion:latest -nullable:disable \
    -nowarn:CS0618,CS8632,CS1591,CS0168,CS0219,CS0649,CS0169,CS0414 \
    -define:NETSTANDARD2_0 -define:NETSTANDARD -define:NET8_0 -noconfig -nostdlib $REFS \
    "$WORK"/hap/html-agility-pack-master/src/HtmlAgilityPack.Shared/*.cs 2>&1 | grep -v warning || true
fi

OUT="$WORK/harness"
mkdir -p "$OUT"
FILES=()
while IFS= read -r line; do
  case "$line" in ''|'#'*) continue ;; esac
  FILES+=("$REPO/$line")
done < "$HERE/files.txt"
for extra in "$@"; do FILES+=("$REPO/$extra"); done

echo ">> compiling ${#FILES[@]} source files + shim"
dotnet "$CSC" -nologo -target:exe -out:"$OUT/harness.dll" -langversion:12 -nullable:enable \
  -nowarn:CS8632,CS0168,CS0219,CS8618,CS8602,CS8604,CS8600,CS8601,CS8603,CS8625,CS8619,CS8767,CS8714 \
  -noconfig -nostdlib $REFS -r:"$WORK/build/HtmlAgilityPack.dll" \
  "$HERE/Shim.cs" "$HERE/Runner.cs" "${FILES[@]}" 2>&1 | grep -v warning || true

cp "$WORK/build/HtmlAgilityPack.dll" "$OUT/"
echo '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}' > "$OUT/harness.runtimeconfig.json"

echo ">> running"
cd "$REPO/MainCore.Test"
dotnet "$OUT/harness.dll"
