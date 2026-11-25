// Renders one complete picture off-screen: the 3D view, then the 2D layer (status bar, or the game's own
// picture outside a match) - the same two cameras that draw the window.  Used by the headless test renders
// (editor) and by the built app's self-test when it runs without a window.
using UnityEngine;

namespace Soccer
{
    public static class Snapshot
    {
        /// <summary>比赛中不画状态栏（比分、时间、头像），只出 3D 画面。录像工具的 -nohud 1。</summary>
        public static bool NoHud;

        public static byte[] Png(Director d, int w, int h, bool snap = false, int jpgQuality = 0)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.antiAliasing = 4;
            bool match = d.Host.Nes != null && d.Host.Bridge.InMatch && !d.Host.Flat;
            if (d.Front != null && d.Front.HidesPicture) match = true;     // (the start screen: the court behind it)
            if (d.Host.Nes != null) d.Host.UploadFrame();
            d.Cam.targetTexture = rt; d.Cam.ResetAspect();
            d.Frame(0f, snap);
            if (match) d.Cam.Render();
            d.Hud.Layout(w, h);
            d.Hud.HudCam.targetTexture = rt; d.Hud.HudCam.ResetAspect();
            if (!(match && NoHud)) d.Hud.HudCam.Render();             // 比赛以外（菜单、过场）的画面整幅就是原版，照画
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false, false);        // no alpha: that channel only carries the "cartoon" mark
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            d.Cam.targetTexture = null; d.Cam.ResetAspect(); d.Hud.HudCam.targetTexture = null; d.Hud.HudCam.ResetAspect();
            byte[] png = jpgQuality > 0 ? tex.EncodeToJPG(jpgQuality) : tex.EncodeToPNG();
            if (Application.isPlaying) { Object.Destroy(tex); rt.Release(); Object.Destroy(rt); }
            else { Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); }
            return png;
        }
    }
}
