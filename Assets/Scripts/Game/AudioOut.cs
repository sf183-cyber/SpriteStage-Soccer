// Plays what the emulated sound chip produces.  The game thread pushes samples into a ring buffer,
// Unity's audio thread pulls them out.
using UnityEngine;

namespace Soccer
{
    public sealed class AudioOut : MonoBehaviour
    {
        const int Size = 1 << 15, Mask = Size - 1;
        readonly float[] ring = new float[Size];
        volatile int w, r;
        public float Volume = 0.85f;
        public bool Mute;
        public int SampleRate = 48000;
        /// <summary>The sounds of the special shots (set by GameHost): mixed in here, whether or not the game itself is running.</summary>
        public Sfx Fx;
        public float FxVolume = 0.8f;
        float hold;
        bool primed;
        float[] fxBuf = new float[2048];

        /// <summary>Called from the game thread after every emulated frame.</summary>
        public void Push(float[] src, int n)
        {
            int wi = w;
            for (int i = 0; i < n; i++)
            {
                int next = (wi + 1) & Mask;
                if (next == r) break;               // full (fast-forward): drop the rest
                ring[wi] = src[i]; wi = next;
            }
            w = wi;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            int frames = data.Length / channels;
            int fill = (w - r) & Mask;
            // start (and restart after running dry) with a small cushion; never let the delay grow beyond ~0.2 s
            float vol = Mute ? 0f : Volume;
            if (!primed && fill >= SampleRate / 20) primed = true;
            if (!primed)
            {
                // nothing from the game yet (or it stands still): what was last heard dies away
                for (int f = 0; f < frames; f++) { hold *= 0.99f; for (int c = 0; c < channels; c++) data[f * channels + c] = hold * vol; }
            }
            else
            {
                int max = SampleRate / 5;
                if (fill > max) r = (w - SampleRate / 12) & Mask;
                int ri = r;
                for (int f = 0; f < frames; f++)
                {
                    float s;
                    if (ri != w) { s = ring[ri]; ri = (ri + 1) & Mask; hold = s; }
                    else { hold *= 0.99f; s = hold; primed = false; }
                    s *= vol;
                    for (int c = 0; c < channels; c++) data[f * channels + c] = s;
                }
                r = ri;
            }
            var fx = Fx;
            if (fx != null)
            {
                if (fxBuf.Length < frames) fxBuf = new float[frames];
                System.Array.Clear(fxBuf, 0, frames);
                fx.Mix(fxBuf, frames, Mute ? 0f : FxVolume);
                for (int f = 0; f < frames; f++)
                {
                    float s = fxBuf[f]; if (s == 0f) continue;
                    for (int c = 0; c < channels; c++) data[f * channels + c] = Sfx.Soft(data[f * channels + c] + s);
                }
            }
        }
    }
}
