#!/bin/bash
# Headless pictures at chosen moments of a recorded run (FCEUX .fm2 movie played from power-on).
# usage: Tools/movie_shots.sh <movie.fm2> <output folder> "frame:yaw:pitch:distance:fov;frame:..."
# extra Unity arguments may come in $SPRITESTAGE_ARGS, e.g. SPRITESTAGE_ARGS="-spectateFrom 1511 -allSpecials 1" (computer v computer, every shot a special)
cd "$(dirname "$0")/.."
U=/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity
mkdir -p "$2"
"$U" -batchmode -quit -projectPath "$PWD" -executeMethod SoccerTools.MovieShots -movie "$1" -shotDir "$2" -frames "$3" -logFile "$2/movie.log" $SPRITESTAGE_ARGS
grep -E "error CS|\[Soccer\]|Exception|NullReference" "$2/movie.log" | sort -u | head -40
