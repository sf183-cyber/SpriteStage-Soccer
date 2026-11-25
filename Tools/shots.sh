#!/bin/bash
# Headless check of the 3D view: imports whatever textures are new, plays a scripted match, writes PNGs.
# usage: Tools/shots.sh <output folder> [full|quick|tokyo]     东京塔夜场：SPRITESTAGE_ARGS="-stage tokyo" Tools/shots.sh 目录 tokyo
cd "$(dirname "$0")/.."
U=/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity
mkdir -p "$1"
"$U" -batchmode -quit -projectPath "$PWD" -executeMethod SoccerTools.Setup -logFile "$1/setup.log" || { grep -E "error CS|Exception" "$1/setup.log" | sort -u | head -20; exit 1; }
"$U" -batchmode -quit -projectPath "$PWD" -executeMethod SoccerTools.Shots -shotDir "$1" -shotSet "${2:-full}" -logFile "$1/shots.log" $SPRITESTAGE_ARGS
grep -E "error CS|\[Soccer\]|Exception|NullReference" "$1/setup.log" "$1/shots.log" | grep -v "shot /" | sort -u | head -30
