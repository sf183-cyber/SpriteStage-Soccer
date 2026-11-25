#!/bin/bash
# Compiles all the game's scripts against Unity's libraries without starting Unity (a few seconds): catches compiler
# errors before a batch run that would take minutes to report them.   usage: Tools/check_scripts.sh
cd "$(dirname "$0")/.."
U=/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents
S="$U/Resources/Scripting"
REFS=()
for f in "$U"/PlaybackEngines/MacStandaloneSupport/Variations/mono/Managed/UnityEngine*.dll; do REFS+=("-r:$f"); done
"$S/NetCoreRuntime/dotnet" "$S/DotNetSdkRoslyn/csc.dll" -nologo -target:library -nowarn:0219,0649,0414,0169 -out:${TMPDIR:-/tmp}/soccer_check.dll \
  -nostdlib -r:"/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/Resources/Scripting/NetStandard/ref/2.1.0/netstandard.dll" "${REFS[@]}" Assets/Scripts/NesCore/*.cs Assets/Scripts/Game/*.cs 2>&1 | grep -v ": warning" ; echo "check done"
