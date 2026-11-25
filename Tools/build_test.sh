#!/bin/bash
# Compiles the emulator core (+ bridge) with one of the console tools, using the compiler that ships inside Unity
# (no Unity licence or window needed).   usage: Tools/build_test.sh [out.exe] [Tools/NesTest.cs]
set -e
cd "$(dirname "$0")/.."
S="/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/Resources/Scripting"
L="$S/MonoBleedingEdge/lib/mono/4.7.1-api"
OUT="${1:-/tmp/NesTest.exe}"
SRC="${2:-Tools/NesTest.cs}"
"$S/NetCoreRuntime/dotnet" "$S/DotNetSdkRoslyn/csc.dll" -nologo -optimize+ -nowarn:0219 -out:"$OUT" \
  -nostdlib -r:"$L/mscorlib.dll" -r:"$L/System.dll" -r:"$L/System.Core.dll" \
  Assets/Scripts/NesCore/*.cs Assets/Scripts/Game/SoccerBridge.cs Assets/Scripts/Game/SpriteArt.cs Assets/Scripts/Game/Tweaks.cs Assets/Scripts/Game/Sfx.cs "$SRC"
echo "built $OUT   (run with: \"$S/MonoBleedingEdge/bin/mono\" $OUT ...)"
