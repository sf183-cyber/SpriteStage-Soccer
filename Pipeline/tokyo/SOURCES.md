# 东京塔夜场：资料和素材来源

第二个比赛场景“东京塔夜场”：足球场放在东京芝公园（プリンス芝公園）的椭圆草坪上，东京塔在西北方约 305 m。
这个文件夹是生成和烘焙这个场景的脚本（Python + 无界面 Blender），结果在 `Assets/Resources/World/tokyo/`。

## 出典（必须保留）

周边建筑的形状、高度、层数和外墙颜色来自：

**出典：国土交通省 3D都市モデル（Project PLATEAU）港区（2025年度）（G空間情報センター https://www.geospatial.jp/ckan/dataset/plateau-13103-minato-ku-2025 ）を加工して作成**

（数据的著作权属于所在的地方政府；加工后的东西不能说成是国土交通省自己的。）

- 利用条件：PLATEAU サイトポリシー「3. 著作権について」 https://www.mlit.go.jp/plateau/site-policy/ （CC BY 4.0 互換，可商用，要出典表示）
- 数据目录：https://www.geospatial.jp/ckan/dataset/plateau-13103-minato-ku-2025
- 我们对数据做了加工：只取 LOD2 的墙面和屋顶形状、`measuredHeight`、地上层数、用途；离球场 185 m 以内的 63 栋楼烘成 Unity 里的三维模型
  （`near.bytes`、`near.jpg`），更远的楼渲进远景全景带（`band_0..3.png`）。夜里的窗户灯光是程序画的，没有用 PLATEAU 的贴图本身；
  外墙颜色取的是 PLATEAU 外墙照片的平均颜色。
- 原始数据（CityGML，约 480 MB）**不在源码里**。重新生成时自己下载，放进 `plateau_raw/`：
  - `53393589_bldg_6697_op.gml`（芝公园、增上寺西侧）
  - `53393599_bldg_6697_op.gml`（东京塔、麻布台、神谷町、爱宕）
  - `53393598_bldg_6697_op.gml`（六本木、麻布）
  - `53393690_bldg_6697_op.gml`（增上寺东侧、御成门、汐留）
  - `53393599_veg_6697_op.gml`（植被）
  - 外墙照片 `<方格>_bldg_6697_appearance/*.jpg`（和 gml 在同一个目录下）
  - 下载地址可以从 PLATEAU 的数据目录 API 查到：`https://api.plateauview.mlit.go.jp/datacatalog/citygml/m:<3次メッシュ番号>`

## 东京塔

- 塔是脚本按公开尺寸搭的钢桁架模型（`t_tower.py`），夜里打“地标灯”。没有用任何照片或别人的模型当素材。
- 尺寸和灯光是文字资料：日文维基百科「東京タワー」（高 333 m，塔脚中心间距 88.0 m，大展望台约 120 m 以上，特别展望台约 223.55 m，
  国际橙和白 7 等分的涂装，地标灯 180 盏、冬天是暖色的高压钠灯，塔下 5 层的 FootTown）。
- 塔身的收分、展望台的大小、横梁的位置是看照片估的（维基共享资源的照片只在浏览器里看过，没有下载、没有用作贴图）。
- 塔的位置和朝向按 PLATEAU 里塔身和 FootTown 的形状校正。

## 地点和其他

- 球场的位置：在 OpenStreetMap 的网页地图上**看**着估的椭圆草坪中心（35.65623, 139.74723），没有下载、没有使用它的数据；
  再按 PLATEAU 的建筑往东北挪了 5 m（`t_geo.py`）。
- PLATEAU 的 4 个方格以外的几栋有名的高楼（虎之门之丘、竹芝、滨松町一带）：坐标、高度、层数来自维基数据（Wikidata，CC0）的
  P625 / P2048 / P1101，外形看照片估的；再远处是随机生成的楼。城市里没有任何真实的楼名、商标和文字。
- 大路的走向（`roads.json`）是看地图描的大致路线。

## 素材

- 树：和白宫场景同一套扫描树 `jacaranda_tree`、`tree_small_02`（Poly Haven，CC0），做成面片（impostor）。
- 月面 `moon.png`：NASA 科学可视化工作室 CGI Moon Kit（LRO 数据），公有领域。
- 其余全部是程序生成的：东京塔（`t_tower.py`）、楼和窗户的灯、航空障碍灯、路灯（`t_city.py`、`t_facade3d.py`、`t_plateau_build.py`）、
  夜空和云（`t_sky.py`）、公园的草坪和路（`t_far.py`、`t_outer.py`）、近景草坪（`n_lawn_near.py`）、公园边的绿篱（`t_hedge.py`）。

## 重新生成

```bash
bash run_bakes.sh                                   # 草坪、远景、外圈地面、近处的三维楼（Blender 无界面，约 25 分钟）
Blender -b --factory-startup -P t_pano.py -- ...    # 远景全景带（约 9 分钟），参数见脚本开头
python3 t_compose.py band unity                     # 冲洗成 Assets/Resources/World/tokyo/ 里的图
```

`out/` 是中间结果（约 1 GB），不在源码里。
