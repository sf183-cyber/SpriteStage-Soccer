# -*- coding: utf-8 -*-
"""真实的地理位置。原点是足球场的中点。

足球场在芝公园（プリンス芝公園的椭圆草坪），东京塔在它西北方 305 m。场景坐标系转了个方向：
y 轴指向东京塔（游戏的默认机位正对着塔），x 轴向右。

坐标、高度、层数：维基数据 / 维基百科（2026-11-24 查，见 SOURCES.md）。标“估”的是没查到、看地图或照片估的。
平面尺寸、朝向、外形全是看照片和地图估的（没有图纸）。"""
import math

ARENA = (35.656269, 139.747266)        # 足球场中点（2026-11-24）：プリンス芝公園里那片椭圆草坪（约 65 x 70 m，
                                       # OpenStreetMap 网页地图上看着估的中心 35.65623, 139.74723），再往东北挪 5 m，让近景草坪
                                       # （51 x 26 m）避开草坪西边 PLATEAU 里一栋 1 层的小屋（用途 403）。离塔 305 m。
TOWER_LL = (35.65859, 139.74545)       # 东京塔：PLATEAU 里塔身（100 m 以上）的中心（维基数据是 35.65861, 139.74556，差 10 m）
TOWER_FACE_REAL = 56.6                 # 塔的面朝向（从正东逆时针，度）：PLATEAU 里大展望台、FootTown 的墙的方向
M_LAT = 110950.0                       # 这个纬度上，1° 纬度、经度各多少米
M_LON = 111320.0 * math.cos(math.radians(ARENA[0]))


def en(lat, lon):
    """真实世界里相对对战线的 (东, 北) 米数"""
    return (lon - ARENA[1]) * M_LON, (lat - ARENA[0]) * M_LAT


_te, _tn = en(*TOWER_LL)
BEARING = math.atan2(_te, _tn)          # 塔的方位（北偏东，弧度；这里是负的：西北）
TOWER_D = math.hypot(_te, _tn)


def scene(lat, lon):
    """真实位置 -> 场景坐标 (x 右, y 朝塔)"""
    e, n = en(lat, lon)
    c, s = math.cos(BEARING), math.sin(BEARING)
    return e * c - n * s, e * s + n * c


def latlon(x, y):
    """场景坐标 -> 经纬度（scene 的反变换）"""
    c, s = math.cos(BEARING), math.sin(BEARING)
    e, n = x * c + y * s, -x * s + y * c
    return ARENA[0] + n / M_LAT, ARENA[1] + e / M_LON


def mesh3(lat, lon):
    """标准地域 3 次メッシュ编号（约 1 km 见方，PLATEAU 按它分文件）"""
    p = int(lat * 1.5); u = int(lon - 100)
    la = lat - p / 1.5; lo = lon - 100 - u
    q = int(la / (5 / 60)); v = int(lo / (7.5 / 60))
    r = int((la - q * 5 / 60) / (30 / 3600)); w = int((lo - v * 7.5 / 60) / (45 / 3600))
    return '%02d%02d%d%d%d%d' % (p, u, q, v, r, w)


# 下载了 PLATEAU 建筑数据的方格（这些方格里的楼全用真实的；之外的仍用下面 LANDMARKS 的估算体量 + 随机填充）
PLATEAU_BLDG = {'53393589', '53393599', '53393598', '53393690'}
PLATEAU_VEG = set()                    # PLATEAU 的植被只有零星几十棵（单棵树），树还是随机撒、避开真实建筑


def covered(x, y, kind='bldg'):
    return mesh3(*latlon(x, y)) in (PLATEAU_BLDG if kind == 'bldg' else PLATEAU_VEG)


def scene_rot(real_deg):
    """真实世界里（从正东逆时针量）的朝向 -> 场景里的朝向（弧度）"""
    return math.radians(real_deg) + BEARING


# 周边的真实建筑。
# (名字, 纬度, 经度, 高 m, 层数, 平面, 宽, 深, 朝向°(真实世界、从正东逆时针), 顶部, 外墙)
# 平面：rect 方、round 圆角方、slab 板楼；顶部：flat 平、wave 波浪（麻布台）、chamfer 切角、step 退台、crown 顶上一圈灯
# 外墙：glass 玻璃幕墙办公、office 普通办公、hotel 酒店（暖光、房间小窗）、res 住宅、concrete 老式办公
LANDMARKS = [
    # 塔脚下和公园周围
    ('機械振興会館',            35.65969, 139.74524,  35,  8, 'rect',  62, 38,  -20, 'flat',   'concrete'),   # 高、尺寸估
    ('東京プリンスホテル',      35.65899, 139.74789,  36, 10, 'slab', 150, 24,   72, 'flat',   'hotel'),      # 高估
    ('ザ・プリンス パークタワー東京', 35.65519, 139.74701, 139, 33, 'slab', 82, 30,  25, 'step',  'hotel'),     # 高估（33 层）
    ('港区役所',                35.65800, 139.75140,  70, 16, 'rect',  60, 40,   10, 'flat',   'office'),     # 位置、高估
    ('東京慈恵会医科大学附属病院', 35.66361, 139.75152,  60, 14, 'rect',  80, 40,   15, 'flat',   'office'),     # 高估
    # 麻布台、六本木、赤坂
    ('麻布台ヒルズ森JPタワー',  35.66099, 139.74039, 325, 64, 'round', 62, 62,   20, 'wave',   'glass'),
    ('アークヒルズ仙石山森タワー', 35.66352, 139.74179, 207, 47, 'round', 55, 45,   20, 'chamfer', 'glass'),     # 高估
    ('泉ガーデンタワー',        35.66467, 139.73938, 216, 45, 'round', 60, 50,   15, 'chamfer', 'glass'),      # 高估
    ('アーク森ビル',            35.66711, 139.74017, 153, 37, 'rect',  70, 50,   30, 'step',   'office'),     # 高估
    ('六本木ヒルズ森タワー',    35.66056, 139.72917, 238, 54, 'round', 80, 70,   10, 'crown',  'glass'),
    ('東京ミッドタウン',        35.66583, 139.73111, 248, 54, 'rect',  60, 50,   35, 'chamfer', 'glass'),      # 高估
    # 神谷町、虎ノ門、爱宕
    ('東京ワールドゲート',      35.66553, 139.74595, 180, 36, 'rect',  55, 45,   20, 'flat',   'glass'),
    ('愛宕グリーンヒルズ MORIタワー', 35.66215, 139.74890, 187, 42, 'slab', 70, 32,   80, 'flat',   'glass'),     # 高估（42 层）
    ('虎ノ門ヒルズ森タワー',    35.66690, 139.74900, 255, 52, 'round', 70, 60,    5, 'chamfer', 'glass'),
    ('虎ノ門ヒルズステーションタワー', 35.66700, 139.74780, 266, 49, 'round', 65, 55,   10, 'crown', 'glass'),     # 位置、高估
    # 汐留、浜松町、竹芝
    ('汐留シティセンター',      35.66535, 139.76098, 216, 43, 'rect',  60, 45,   25, 'flat',   'glass'),
    ('電通本社ビル',            35.66437, 139.76237, 213, 48, 'slab',  70, 40,   60, 'flat',   'glass'),      # 高估
    ('日本テレビタワー',        35.66442, 139.75991, 193, 32, 'slab',  75, 35,   30, 'flat',   'glass'),
    ('汐留メディアタワー',      35.66310, 139.75900, 173, 34, 'rect',  50, 40,   25, 'flat',   'office'),     # 高估
    ('東京汐留ビルディング',    35.66300, 139.76122, 173, 37, 'rect',  55, 45,   25, 'flat',   'office'),     # 高估
    ('世界貿易センタービル南館', 35.65630, 139.75700, 200, 39, 'rect',  60, 45,   20, 'flat',   'glass'),      # 2020 年的南館，高估
    ('東京ポートシティ竹芝',    35.65511, 139.76061, 165, 40, 'rect',  60, 45,   25, 'flat',   'glass'),      # 高估
    ('浜松町ビルディング',      35.65150, 139.75767, 166, 40, 'rect',  55, 40,   25, 'flat',   'office'),     # 高估
    ('日本電気本社ビル',        35.64944, 139.74806, 180, 43, 'slab',  75, 30,  -10, 'crown',  'office'),
]

# 增上寺（大殿、三解脱门）：传统屋顶，夜里有灯打亮
ZOJOJI_DAIDEN = (35.65748, 139.74838)          # 维基数据（增上寺的坐标就在大殿上）
ZOJOJI_GATE = (35.65735, 139.75060)            # 三解脱门，地图上估的
FOOTTOWN = TOWER_LL

# 地图上看的真实道路（场景里只放路灯，路面在全景带的地面里）：日比谷通り/祝田通り 在公园西边南北向
ROADS = [((35.6600, 139.7462), (35.6575, 139.7466), (35.6560, 139.7462), (35.6540, 139.7460))]


if __name__ == '__main__':
    print('tower: %.1f m at bearing %.1f°' % (TOWER_D, math.degrees(BEARING)))
    for row in LANDMARKS:
        x, y = scene(row[1], row[2])
        d = math.hypot(x, y)
        print('%-24s x %7.0f y %7.0f  dist %5.0f  az %6.1f  el %4.1f' % (row[0], x, y, d, math.degrees(math.atan2(x, y)), math.degrees(math.atan2(row[3], d))))
    print('daiden', scene(*ZOJOJI_DAIDEN), 'gate', scene(*ZOJOJI_GATE))
