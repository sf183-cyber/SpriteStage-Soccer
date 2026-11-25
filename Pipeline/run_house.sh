#!/bin/bash
# The measured house: bake its lighting and write the meshes for Unity.   run_house.sh <log folder> [texture size] [samples] [far]
cd "$(dirname "$0")"
B=/Applications/Blender.app/Contents/MacOS/Blender
LOG="${1:-/tmp}"
if [ "$4" = "far" ]; then (time $B -b --factory-startup -P u_far.py -- 4096 32 far) > "$LOG/far_full.log" 2>&1; fi
(time $B -b --factory-startup -P u_house2.py -- bake "${2:-4096}" "${3:-96}") > "$LOG/house2_bake.log" 2>&1
grep -E "RENDER_OK far|BAKE_OK|MESH_OK|DETAIL_OK|rror:|Traceback|^real" "$LOG/far_full.log" "$LOG/house2_bake.log"
