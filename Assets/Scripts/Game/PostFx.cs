// Photographic finish of the 3D view (see G3Post.shader).  Sits on the main camera; the status bar is drawn
// by its own camera afterwards and is not touched.  Press P in the game to switch it off / on.
using UnityEngine;

namespace Soccer
{
    [ExecuteAlways]
    public sealed class PostFx : MonoBehaviour
    {
        public float Threshold = 0.80f, Intensity = 0.32f, Vignette = 0.16f, Contrast = 1.06f;
        public float Flash, Rain;           // set by the effects layer: lightning (0..1), rain (0..1)
        public Vector4 Wave, Heat, Zoom;    // ... a shock wave (centre x, y in the picture, radius, strength), hot air (same), a rush towards a point (x, y, -, strength)
        public float Gloom, Dim;            // storm light (0..1); the hush while a special shot is in the air (0..1)
        public Vector4 Punch, Lines;        // a mighty kick's flash (rgb, amount); impact lines (centre x, y in the picture, strength, which set)
        public Vector4 Push;                // the standstill of a special shot: the picture drawn in towards a point - x, y in the picture, -, how much (0.1 = 10 % closer)
        public float Clock;
        Material mat;

        void Update()
        {
            if (Application.isPlaying && Keys.Down(KeyCode.P)) enabled = !enabled;
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (mat == null)
            {
                var sh = Resources.Load<Shader>("Shaders/G3Post");
                if (sh == null) { Graphics.Blit(src, dst); return; }
                mat = new Material(sh); mat.hideFlags = HideFlags.DontSave;
            }
            mat.SetFloat("_Threshold", Threshold); mat.SetFloat("_Intensity", Intensity); mat.SetFloat("_Vignette", Vignette); mat.SetFloat("_Contrast", Contrast);
            mat.SetFloat("_Flash", Flash); mat.SetFloat("_Rain", Rain);
            mat.SetVector("_Wave", Wave); mat.SetVector("_Heat", Heat); mat.SetVector("_Zoom", Zoom);
            mat.SetFloat("_Gloom", Gloom); mat.SetFloat("_Dim", Dim); mat.SetVector("_Punch", Punch); mat.SetVector("_Lines", Lines);
            mat.SetFloat("_Aspect", src.width / (float)src.height); mat.SetFloat("_Clock", Clock);
            mat.SetVector("_Push", Push);
            int w = Mathf.Max(8, src.width / 4), h = Mathf.Max(8, src.height / 4);
            var a = RenderTexture.GetTemporary(w, h, 0, src.format); var b = RenderTexture.GetTemporary(w, h, 0, src.format);
            a.filterMode = b.filterMode = FilterMode.Bilinear;
            Graphics.Blit(src, a, mat, 0);
            for (int i = 0; i < 2; i++) { Graphics.Blit(a, b, mat, 1); Graphics.Blit(b, a, mat, 2); }
            var c = RenderTexture.GetTemporary(w / 2, h / 2, 0, src.format); var d = RenderTexture.GetTemporary(w / 2, h / 2, 0, src.format);
            Graphics.Blit(a, c);                                    // a second, wider octave
            for (int i = 0; i < 2; i++) { Graphics.Blit(c, d, mat, 1); Graphics.Blit(d, c, mat, 2); }
            Graphics.Blit(c, b, mat, 1); Graphics.Blit(b, a, mat, 2);       // blend the wide glow back into the quarter-size one
            mat.SetTexture("_Bloom", a);
            Graphics.Blit(src, dst, mat, 3);
            RenderTexture.ReleaseTemporary(a); RenderTexture.ReleaseTemporary(b); RenderTexture.ReleaseTemporary(c); RenderTexture.ReleaseTemporary(d);
        }
    }
}
