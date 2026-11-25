#!/bin/bash
# 东京塔夜场：全景带以外的几样烘焙，按顺序跑（GPU 一次只跑一个）。M2 Max 上约 25 分钟。全景带另跑（t_pano.py，约 9 分钟）。
cd "$(dirname "$0")"
B=/Applications/Blender.app/Contents/MacOS/Blender
set -x
python3 t_layout.py
time $B -b --factory-startup -P n_lawn_near.py -- 160 192 near > out/near.log 2>&1; grep -E "RENDER_OK|Error" out/near.log
python3 t_compose.py near unity
time $B -b --factory-startup -P t_far.py -- 4096 64 far > out/far.log 2>&1; grep -E "RENDER_OK|Error" out/far.log
python3 t_compose.py far unity
time $B -b --factory-startup -P t_outer.py -- 4096 64 outer > out/outer.log 2>&1; grep -E "RENDER_OK|Error" out/outer.log
python3 t_compose.py outer unity
time $B -b --factory-startup -P t_nearbake.py -- bake 4096 192 > out/nearbake.log 2>&1; grep -E "BAKE_OK|MESH_OK|Error" out/nearbake.log
python3 t_compose.py nearbake unity
echo ALL_BAKES_DONE
