#!/bin/bash
# A stretch of a recorded run (FCEUX .fm2) as a film with sound.
# usage: Tools/movie_video.sh <movie.fm2> <work folder> <from:to> <yaw0:yaw1:pitch:distance:fov> <out.mp4>
#   camera variants: ...:fov:shift           follows the play; shift moves the ground line (-0.5 = low, for close-ups of a ball in the air)
#                    ...:fov:east:north:height  looks at that fixed point instead (e.g. a goal: +-17.4:0.3:1.7)
# extra Unity arguments may come in $SPRITESTAGE_ARGS, e.g. SPRITESTAGE_ARGS="-spectateFrom 1511 -allSpecials 1" (computer v computer, every shot a special)
cd "$(dirname "$0")/.."
U=/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity
rm -rf "$2"; mkdir -p "$2"
"$U" -batchmode -quit -projectPath "$PWD" -executeMethod SoccerTools.MovieVideo -movie "$1" -shotDir "$2" -range "$3" -cam "$4" -logFile "$2/video.log" $SPRITESTAGE_ARGS
grep -E "error CS|\[Soccer\]|Exception|NullReference" "$2/video.log" | sort -u | head -20
ffmpeg -y -loglevel error -framerate 60.0988 -i "$2/f%04d.jpg" -f f32le -ar 48000 -ac 1 -i "$2/audio_f32.raw" -c:v libx264 -pix_fmt yuv420p -crf 22 -preset slow -c:a aac -b:a 128k -shortest -movflags +faststart "$5"
