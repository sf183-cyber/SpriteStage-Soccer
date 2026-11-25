# SpriteStage-Soccer

**Sprite-preserving 3D re-staging of an unmodified 8-bit soccer game**

One of the two SpriteStage case studies; the other is [SpriteStage-Basketball](https://github.com/maiziman/SpriteStage-Basketball).

**Windows download: [Releases](https://github.com/maiziman/SpriteStage-Soccer/releases/latest)**

## Play (Windows x64)

1. Download `SpriteStage-Soccer-Windows-x64.zip` from [Releases](https://github.com/maiziman/SpriteStage-Soccer/releases/latest) and unzip it.
2. **No game is included.** Put your own copy of the game (see [Game files](#game-files); a `.nes` file or a `.zip` containing it) into the `ROM` folder next to the program, or drop it onto the `.exe`.
3. Run `SpriteStage-Soccer.exe`. The program is not code-signed, so Windows may say it protected your PC: choose *More info → Run anyway*.
4. Press Enter on the start screen to go straight into a match in 3D. Press **H** in the game for all keys, **F1** to change keys, **F2** to switch between the 3D scene and the original picture, **0** to switch between the White House lawn and the Tokyo Tower night stage. The full key list (in Chinese) is in `使用说明.txt` inside the zip.

## Abstract

SpriteStage shows an unmodified 8-bit console game inside a photoreal, freely orbitable 3D scene while every character keeps its original pixel art. The game program runs in a cycle-stepped emulator and keeps all of its own logic. A per-frame **bridge** reads the machine state, re-runs the game's own sprite composer on a sandboxed copy of memory for every object, and publishes objects with ground position, height and pixel pieces. A renderer places those pieces as camera-facing quads in pre-baked scenes built from public survey drawings and open city-model data, adds projected shadows, effects and a free camera, and keeps the game's status bar as a 2D overlay. Data derived from the game's graphics is shipped encrypted with those very graphics, tile by tile, so it opens only with the player's own copy of the game.

This repository is the case study of a 1993 Famicom **soccer** game, on two stages: the **White House south lawn** (from Historic American Buildings Survey drawings) and a **Tokyo Tower night** scene (from the PLATEAU 3D city model), switchable during a match.

## Method at a glance

1. **Emulator core** (C#, `Assets/Scripts/NesCore/`): 6502 CPU, PPU, APU and the MMC3 mapper, enough to run the game faithfully.
2. **Bridge** (`Assets/Scripts/Game/SoccerBridge.cs`): detects the moment the game composes its sprites (a *tick*), snapshots work RAM, and side-calls the game's own sprite composer once per object on a sandboxed copy. Pieces are published one frame late to match bank-switched graphics. Positions are mapped from the game's screen geometry to a metric ground plane.
3. **Re-staging** (Unity): original pixels on camera-facing quads, an optional sharpening table, projected shadows, a free camera with camera-relative controls, and effects driven by game events.
4. **Stages** (`Pipeline/`): baked offline with Blender Cycles (headless) from public-domain, CC0 or attributed open data.

The rendering path only reads the machine state. Optional gameplay extensions (e.g. computer-vs-computer play) do write to it; they are switchable and are turned off in the "original picture" mode.

## Results

| | Soccer |
|---|---|
| Bridge vs. the game's own sprite table | 170,893 pieces identical; 115 differ, all when the original's 64-entry table was full |
| Emulator vs. a reference emulator (work RAM, same input) | 1,382 / 1,488 frames byte-identical, the rest reconverge the next frame |
| Frame at 2560×1440, 4× MSAA, incl. game step (Apple M2 Max) | 4.7–5.0 ms (White House), 5.0–6.3 ms (Tokyo) |
| Emulator + bridge per frame | 1.3 ms |
| Locked data opened with the player's copy | 1,792 / 1,792 tiles |

## Game files

**This repository contains no game.** Running or building needs your own copy (`.nes`, 262,160 bytes, any file name). The program ignores the header and checks the rest. Tested version:

| Version | SHA-1 (without the 16-byte header) |
|---|---|
| Kunio-kun no Nekketsu Soccer League (Japan) | c29da974d39a76b7d1134a81cadaf00b51208151 |

The title is given only to identify the required game data. Other versions whose program banks used by the bridge are identical (a *program fingerprint*, see `RomFile.cs`) also work; programs that differ are reported and can still be tried (`-forceRom`).

## Building and testing

Unity 6000.3.23f1 with Windows Build Support (Mono). Put your game file into `ROM/` in the repository folder (ignored by git).

```bash
Unity -batchmode -quit -projectPath . -executeMethod SoccerTools.BuildRelease -buildPath out
```

This writes a Windows x64 build and a macOS build (the latter for headless tests). Self-test without a window: `<program> -batchmode -rom <absolute path to the game file> -selftest <folder>`; add `-launchTest` (through the start screen), `-viewSwitch` (original picture in pictures 2–3), `-stage tokyo` (start on the Tokyo stage) or `-stageSwitch` (change stage during the match). The command-line checks in `Tools/` (e.g. `BridgeCheck.cs`) build with Unity's bundled compiler (`Tools/build_test.sh`).

## Repository layout

| Path | Contents |
|---|---|
| `Assets/Scripts/NesCore/` | emulator core |
| `Assets/Scripts/Game/` | bridge, scene, sprites and shadows, camera, controls (`Controls.cs`), game-file handling (`RomFile.cs`) |
| `Assets/Editor/` | headless renders, video and packaging commands (`SoccerTools.cs`) |
| `Assets/Resources/World/` | baked stage textures and data (`tokyo/`: the Tokyo Tower night stage) |
| `Pipeline/` | stage generation and baking (Python + headless Blender); sources in `Pipeline/WH_SOURCES.md` and `Pipeline/tokyo/SOURCES.md` |
| `Tools/` | command-line checks and experiments, recorded input (`.fm2`) |

## Data derived from the game

The sprite sharpening table (`Assets/Resources/Sprites/sprite_classes.bytes`) was computed from the game's own graphics. It is stored encrypted with those graphics: format `SPS2` keys every 8×8 tile on its own 16 CHR bytes (SHA-256 with the salt `spritestage/soccer/`), so a version with some redrawn tiles still opens every shared tile. `Tools/rom_lock.py` documents the format:

```bash
python3 Tools/rom_lock.py tiles ROM/game.nes plain.bytes Assets/Resources/Sprites/sprite_classes.bytes spritestage/soccer/
python3 Tools/rom_lock.py open  ROM/game.nes locked.bytes opened.bytes spritestage/soccer/
```

## Assets and attribution

- White House: Historic American Buildings Survey, HABS DC-37 (US government work, public domain).
- Tokyo stage buildings: 出典：国土交通省 3D都市モデル（Project PLATEAU）港区（2025年度）（[G空間情報センター](https://www.geospatial.jp/ckan/dataset/plateau-13103-minato-ku-2025)）を加工して作成 — MLIT Japan, 3D city model of Minato City (FY2025), processed by this project. Terms: [PLATEAU site policy](https://www.mlit.go.jp/plateau/site-policy/) (compatible with CC BY 4.0). The original CityGML is not in this repository; see `Pipeline/tokyo/SOURCES.md`.
- Tokyo Tower: a procedural model built from published dimensions; positions and heights of a few distant towers from Wikidata (CC0).
- Sky HDRI and scanned trees (`kloofendal_38d_partly_cloudy_puresky`, `jacaranda_tree`, `tree_small_02`): Poly Haven (CC0).
- Moon: NASA Scientific Visualization Studio, CGI Moon Kit (public domain).
- Everything else (lawns, crowd figures, effects, city lights) was made for this project.

## Limitations

Characters are flat, camera-facing drawings (no side or back views); screens outside matches stay 2D; only MMC3 cartridges with character ROM are supported. Not verified so far: the Windows build has never been run, the look and feel in a real window, controller feel, and sound.

## License and disclaimer

Code: MIT (see `LICENSE`). Data and assets: as listed above. The names, characters and content of the original game belong to their rights holders. This project is unofficial, not affiliated with them, and does not include or distribute any game data.
