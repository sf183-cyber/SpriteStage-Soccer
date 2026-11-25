// 东京塔夜场（Resources/World/tokyo/）的贴图导入规则（2026-11-24）。
// 全景带要开 alphaIsTransparency（透明处的颜色按边缘扩出去，不然双线性采样会在楼的轮廓上带出一圈黑边）。
using UnityEditor;
using UnityEngine;

public class TokyoImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Contains("/Resources/World/tokyo/")) return;
        var t = (TextureImporter)assetImporter;
        string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        bool band = name.StartsWith("band_"), hedge = name.StartsWith("hedge_"), moon = name == "moon", sky = name == "sky";
        t.textureType = TextureImporterType.Default;
        t.npotScale = TextureImporterNPOTScale.None;
        t.maxTextureSize = 8192;
        t.mipmapEnabled = !sky;
        t.anisoLevel = 16; t.filterMode = FilterMode.Trilinear;
        t.textureCompression = sky ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
        t.alphaSource = band || hedge || moon ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        t.alphaIsTransparency = band || moon;
        t.mipMapsPreserveCoverage = hedge; t.alphaTestReferenceValue = 0.45f;
        t.wrapMode = hedge || sky ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        t.isReadable = false;
    }
}
