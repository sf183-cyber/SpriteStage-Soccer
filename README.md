> **这是 `sf183-cyber` 的备份 fork，不是上游项目。**
> This is a personal backup fork. The original project is
> [maiziman/SpriteStage-Soccer](https://github.com/maiziman/SpriteStage-Soccer).

| 项目 | 说明 |
|---|---|
| 上游仓库 | [maiziman/SpriteStage-Soccer](https://github.com/maiziman/SpriteStage-Soccer) |
| Fork 时间 | 2026-11-25 |
| Fork 时对应的上游提交 | `95fb97f7f166ea0b0cd9a9240f14f3074833a4fd`（`main`，即上游 `v1.0`） |
| 本 fork 的用途 | 只做离线留档，不开发、不发 PR、不追踪上游更新 |
| 代码许可 | MIT（见 `LICENSE`，版权归原作者） |
| 包含游戏数据 | **否**（源码中不含任何 `.nes` 文件） |

本仓库的全部代码、美术资产与文档均由上游作者 **maiziman** 创作。此处仅作副本保存，
如与上游出现差异，**以上游为准**。

---

## 上游 Release 说明（v1.0）

本 fork **不包含 Release 二进制**。GitHub 的 fork 只复制 git 内容，不会复制 Release 资产，
下面这份记录用于备查。

| 项 | 值 |
|---|---|
| 上游 Release | [v1.0 — SpriteStage-Soccer 1.0 (Windows x64)](https://github.com/maiziman/SpriteStage-Soccer/releases/tag/v1.0) |
| 发布时间 | 2026-11-25 06:54 UTC |
| 资产文件名 | `SpriteStage-Soccer-Windows-x64.zip` |
| 大小 | 237,481,073 字节（约 226 MiB） |
| SHA-256 | `f2fa4a23d1cea491253045cec7021e4f7ace2bd296c4bb6502c4351cc73323da` |
| 平台 | Windows x64，免安装，解压即用，无需管理员权限 |
| 下载地址 | https://github.com/maiziman/SpriteStage-Soccer/releases/download/v1.0/SpriteStage-Soccer-Windows-x64.zip |

### 运行步骤（摘自上游 Release 说明）

1. 下载上面的 zip 并解压到短路径（例如 `C:\Tools\SpriteStage-Soccer`）。
2. **包内不含游戏。** 自备 `Kunio-kun no Nekketsu Soccer League (Japan)` 的 `.nes`
   （262,160 字节，文件名任意），放进 exe 同级的 `ROM` 文件夹，或直接拖到 exe 图标上。
3. 运行 `SpriteStage-Soccer.exe`。程序未做代码签名，Windows 可能提示"已保护你的电脑"，
   选择 *更多信息 → 仍要运行*。
4. 启动画面按回车直接进入 3D 比赛；`H` 查看全部按键，`F1` 改键，
   `F2` 在 3D 场景与原始画面之间切换，`0` 切换白宫草坪 / 东京塔夜景两个舞台。
   完整按键表见包内 `使用说明.txt`（中文）。

### 游戏数据声明

- 本仓库与上游 Release **均不包含、也不分发**任何游戏 ROM。
- 运行需要玩家自备的 `.nes`，测试版本校验值为
  SHA-1 `c29da974d39a76b7d1134a81cadaf00b51208151`（去除 16 字节 header 后）。
- 上游主动做了技术隔离：由游戏图像派生的数据（`sprite_classes.bytes`）以 `SPS2` 格式
  逐 tile 用玩家自己 ROM 的 CHR 字节做 SHA-256 加密，没有对应 ROM 无法解开。
- 原游戏的名称、角色与内容版权归其权利人所有。本项目为第三方非官方作品，
  与权利人无关联、未获授权。

### 上游自己标注的已知限制

> Characters are flat, camera-facing drawings (no side or back views); screens outside
> matches stay 2D; only MMC3 cartridges with character ROM are supported.
> Not verified so far: the Windows build has never been run, the look and feel in a real
> window, controller feel, and sound.

即：Windows 构建在发布时尚未被实际运行验证过，窗口内观感、手感与声音均未经确认。
上游性能数据取自 Apple M2 Max（2560×1440、4× MSAA、含游戏步进 4.7–6.3 ms/帧）。

### 第三方素材归属（沿用上游）

- 白宫：Historic American Buildings Survey，HABS DC-37（美国政府作品，公有领域）
- 东京场景建筑：出典 国土交通省 3D都市モデル（Project PLATEAU）港区（2025 年度），经本项目加工
- 东京塔：依公开尺寸程序化建模；部分远景塔位高来自 Wikidata（CC0）
- 天空 HDRI 与扫描树木：Poly Haven（CC0）
- 月球：NASA Scientific Visualization Studio, CGI Moon Kit（公有领域）

---

## 与上游同步

本 fork 不会自动更新。需要拉取上游变更时，在 GitHub 仓库页点击 **Sync fork**，
或本地执行：

```bash
git remote add upstream https://github.com/maiziman/SpriteStage-Soccer.git
git fetch upstream && git merge upstream/main
```

---

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
