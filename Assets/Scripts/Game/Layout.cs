// The measurements of the 3D world (written by Pipeline/u_layout.py to Resources/World/layout.json)
// and the mapping from the game's field coordinates onto it.
using System;
using UnityEngine;

namespace Soccer
{
    [Serializable] public class TreeDef { public int species; public float x, y, h; public int variant; }
    // 东京塔夜场（2026-11-24）：泛光灯杆、全景带、公园路灯
    [Serializable] public class FloodDef { public float x, y, h, tx, ty; }      // 灯杆：脚下 (x, y)、灯的高度、瞄准的地面点
    // 公园以外的一切是一圈全景带（Pipeline/tokyo/t_pano.py 从 (0, 0, eyeZ) 渲的，纬度 lo..hi 度，tiles 块），贴在一个“按方向变半径”的
    // 圆筒上：radius[i] 是方位 i*step 度（从塔的方向起、向右为正）上的半径；没有就用 r
    [Serializable] public class BandDef { public float r, eyeZ, lo, hi, step; public int tiles; public float[] radius; }
    [Serializable] public class ParkLampDef { public float x, y, h; }

    [Serializable]
    public class LayoutData
    {
        public float kx, ky, kz, kPersp, fieldCx, fieldCy, halfLength, halfWidth;
        public float goalY0, goalY1, goalH, goalD;
        public float nearX0, nearX1, nearY0, nearY1, farCx, farCy, farSize;
        public float sunX, sunY, houseY, monumentX, monumentY, monumentH, monumentBase;
        public int views;
        public TreeDef[] trees;
        // ---- 东京塔夜场才有的（白宫场景的 layout.json 里没有这些：night = false、band = null）
        public bool night; public float moonAz, moonEl, moonSize, mastH;
        public FloodDef[] floods;
        public BandDef band;
        public ParkLampDef[] parkLamps;
        public float[] parkBox;             // 公园（远景草坪、绿篱）：x0, x1, y0, y1
        public float outerR, towerD;        // 公园外 Unity 画的那一圈地面的半径；东京塔离球场中点多远
        public string stage;
    }

    [Serializable] public class TreeSpeciesInfo { public int frameW, frameH; public float widthPerHeight, heightPerHeight, @base; }

    [Serializable]
    public class TreeAtlasInfo
    {
        public int atlasW, atlasH, cellW, cellH, views, cols, rowsPerVariant;
        public float sunElevation;
        public TreeSpeciesInfo[] species;
    }

    public static class Field
    {
        public static LayoutData L;

        /// <summary>场景：""（白宫南草坪，白天）或 "tokyo"（芝公园的椭圆草坪，夜里，远处是东京塔）。资源在 Resources/World/&lt;场景&gt;/，
        /// 找不到的用 World/ 下白宫场景的（树的图集、world.json）。命令行 -stage tokyo，游戏里按 0 换。</summary>
        public static string Stage = "";
        public static bool IsTokyo { get { return Stage == "tokyo"; } }

        public static T Load<T>(string name) where T : UnityEngine.Object
        {
            T r = null;
            if (!string.IsNullOrEmpty(Stage)) r = Resources.Load<T>("World/" + Stage + "/" + name);
            return r != null ? r : Resources.Load<T>("World/" + name);
        }

        public static void Load()
        {
            var ta = Load<TextAsset>("layout");
            L = JsonUtility.FromJson<LayoutData>(ta.text);
        }

        /// <summary>命令行里的 -stage tokyo（或 whitehouse）</summary>
        public static void StageFromArgs()
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, "-stage");
            if (i >= 0 && i + 1 < a.Length) Stage = a[i + 1] == "whitehouse" ? "" : a[i + 1];
        }

        /// <summary>Game field coordinates (NES pixels) -> point on the ground.  The original's trapezoid pitch becomes a rectangle.</summary>
        public static Vector3 ToWorld(float nx, float ny)
        {
            float xt = (nx - L.fieldCx) / (1f + L.kPersp * (ny - 50f));
            return new Vector3(xt * L.kx, 0f, (L.fieldCy - ny) * L.ky);
        }

        /// <summary>Layout coordinates (x east, y north, z up) -> Unity.</summary>
        public static Vector3 P(float x, float y, float z = 0f) { return new Vector3(x, z, y); }
    }
}
