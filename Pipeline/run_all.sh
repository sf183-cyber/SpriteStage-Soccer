#!/bin/bash
# The long renders, one after the other (they all want the GPU).
#   run_all.sh <log folder> [house] [far] [trees]        (no names = all three)
cd "$(dirname "$0")"
B=/Applications/Blender.app/Contents/MacOS/Blender
LOG="${1:-/tmp}"; shift
JOBS="${*:-house far trees}"
for J in $JOBS; do
  case $J in
    house) (time $B -b --factory-startup -P u_house2.py -- bake 4096 128) > "$LOG/house_full.log" 2>&1 ;;
    far)   (time $B -b --factory-startup -P u_far.py -- 4096 32 far) > "$LOG/far_full.log" 2>&1 ;;
    trees) (time $B -b --factory-startup -P u_trees.py -- 512 96) > "$LOG/trees_full.log" 2>&1 ;;
  esac
done
grep -E "BAKE_OK|MESH_OK|RENDER_OK far|TREE_OK|rror:|Traceback|^real" "$LOG"/house_full.log "$LOG"/far_full.log "$LOG"/trees_full.log 2>/dev/null | grep -v "RENDER_OK tree"
