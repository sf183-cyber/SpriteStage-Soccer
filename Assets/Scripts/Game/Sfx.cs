// The sounds of the special shots that the original does not have: the "super flash" when the world stands still, the
// boom when it lets go, and one sound for each kind of special (fire, thunder, wind ...).
//
// Nothing is recorded: every sound is worked out here from sines, square waves and noise when the game starts (a few
// hundredths of a second), so there are no sound files and nobody else's rights.  Plain C# (no Unity), so that the
// command-line tools can write the very same sounds to a file (Tools/SfxDump.cs).
//
// Two of these mixers exist at a time at most: one for the loudspeakers (AudioOut pulls from it on Unity's audio thread)
// and one for the sound track of a headless recording (GameHost.AudioDump).
using System;

namespace Soccer
{
    public sealed class Sfx
    {
        public const int Flash = 0, Boom = 1, Theme = 2;        // Theme + 0..7: fire, thunder, wind, water, blade, dark, light, earth
        public const int Count = 10;
        public static readonly string[] Names = { "flash", "boom", "fire", "thunder", "wind", "water", "blade", "dark", "light", "earth" };

        static float[][] clips; static int clipRate;
        static readonly object building = new object();

        struct Voice { public float[] Data; public int Pos; public float Vol; }
        readonly Voice[] voices = new Voice[12];
        readonly object gate = new object();

        public Sfx(int rate) { Clips(rate); }

        public static float[][] Clips(int rate)
        {
            lock (building)
            {
                if (clips == null || clipRate != rate) { clips = Build(rate); clipRate = rate; }
                return clips;
            }
        }

        /// <summary>Starts a sound (after 'delay' seconds).</summary>
        public void Play(int id, float vol = 1f, float delay = 0f)
        {
            if (id < 0 || id >= Count) return;
            lock (gate)
            {
                int free = 0, oldest = -1;
                for (int i = 0; i < voices.Length; i++)
                {
                    if (voices[i].Data == null) { free = i; oldest = -2; break; }
                    if (oldest < 0 || voices[i].Pos > voices[oldest].Pos) oldest = i;
                }
                if (oldest >= 0) free = oldest;
                voices[free] = new Voice { Data = clips[id], Pos = -(int)(delay * clipRate), Vol = vol };
            }
        }

        /// <summary>Keeps a sum of sounds inside -1..1 without the crack of a hard limit: untouched up to 0.8, squeezed above.</summary>
        public static float Soft(float v)
        {
            float m = v < 0f ? -v : v;
            if (m <= 0.8f) return v;
            m = 0.8f + 0.2f * (float)Math.Tanh((m - 0.8f) / 0.2f);
            return v < 0f ? -m : m;
        }

        public void StopAll() { lock (gate) for (int i = 0; i < voices.Length; i++) voices[i].Data = null; }

        /// <summary>Adds what is playing to the first n samples of buf (mono).</summary>
        public void Mix(float[] buf, int n, float gain = 1f)
        {
            lock (gate)
            {
                for (int v = 0; v < voices.Length; v++)
                {
                    var d = voices[v].Data; if (d == null) continue;
                    int pos = voices[v].Pos; float vol = voices[v].Vol * gain;
                    for (int i = 0; i < n; i++, pos++)
                    {
                        if (pos < 0) continue;
                        if (pos >= d.Length) break;
                        buf[i] += d[pos] * vol;
                    }
                    voices[v].Pos = pos;
                    if (pos >= d.Length) voices[v].Data = null;
                }
            }
        }

        // ------------------------------------------------------------------ the sounds

        sealed class Rnd
        {
            uint s;
            public Rnd(uint seed) { s = seed; }
            public float Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / (float)0x800000 - 1f; }     // -1..1
            public float Unit() { return Next() * 0.5f + 0.5f; }
        }

        // a filter that gives low, band and high at once (Chamberlin); f in Hz, q = 1 / resonance
        struct Svf
        {
            float low, band;
            public float Low, Band, High;
            public void Step(float x, float f, float q, int rate)
            {
                float k = 2f * (float)Math.Sin(Math.PI * Math.Min(f, rate * 0.22f) / rate);
                low += k * band; High = x - low - q * band; band += k * High; Low = low; Band = band;
            }
        }

        static float Sin(double phase) { return (float)Math.Sin(phase * 2 * Math.PI); }
        static float Square(double phase, double duty) { return phase - Math.Floor(phase) < duty ? 1f : -1f; }
        static float Saw(double phase) { return (float)(2 * (phase - Math.Floor(phase)) - 1); }
        static float Exp(float t, float rate) { return (float)Math.Exp(-t * rate); }
        static float Attack(float t, float time) { return t >= time ? 1f : t / time; }
        static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        static float Glide(float a, float b, float t) { return a * (float)Math.Pow(b / a, Math.Max(0f, Math.Min(1f, t))); }   // from a to b, by ratio

        // loudest sample to 'peak'; a few milliseconds of fade at both ends, so that nothing clicks
        static float[] Finish(float[] a, int rate, float peak)
        {
            float max = 1e-6f;
            for (int i = 0; i < a.Length; i++) { a[i] = (float)Math.Tanh(a[i]); max = Math.Max(max, Math.Abs(a[i])); }
            int fade = rate / 200;
            for (int i = 0; i < a.Length; i++)
            {
                float f = Math.Min(1f, Math.Min(i / (float)Math.Max(1, fade / 4), (a.Length - 1 - i) / (float)fade));
                a[i] *= peak / max * f;
            }
            return a;
        }

        static float[][] Build(int rate)
        {
            var c = new float[Count][];
            c[Flash] = MakeFlash(rate); c[Boom] = MakeBoom(rate);
            c[Theme + 0] = MakeFire(rate); c[Theme + 1] = MakeThunder(rate); c[Theme + 2] = MakeWind(rate); c[Theme + 3] = MakeWater(rate);
            c[Theme + 4] = MakeBlade(rate); c[Theme + 5] = MakeDark(rate); c[Theme + 6] = MakeLight(rate); c[Theme + 7] = MakeEarth(rate);
            return c;
        }

        // The world stands still: a blade drawn (a ring of high partials and a hiss), a thud underneath, and a tone that
        // climbs for as long as the standstill lasts.
        static float[] MakeFlash(int rate)
        {
            int n = (int)(rate * 0.66f); var a = new float[n]; var r = new Rnd(11); var hp = new Svf();
            double[] ph = new double[8]; float[] fr = { 2093f, 2794f, 3520f, 4186f, 5274f, 6272f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, u = t / 0.62f, s = 0f;
                for (int k = 0; k < fr.Length; k++)
                {
                    ph[k] += fr[k] * (1f + 0.004f * k) / rate;
                    s += Sin(ph[k]) * (0.5f / (1f + 0.5f * k)) * Exp(t, 5f + k) * (0.75f + 0.25f * Sin(t * (23 + 3 * k)));
                }
                hp.Step(r.Next(), 5200f, 0.9f, rate);
                s += hp.High * 0.45f * Attack(t, 0.004f) * Exp(t, 11f);
                float sub = Glide(190f, 70f, t / 0.25f); ph[6] += sub / rate;
                s += Sin(ph[6]) * 0.55f * Exp(t, 10f);
                float up = Glide(196f, 1568f, u) * (1f + 0.012f * Sin(t * 31)); ph[7] += up / rate;
                s += Square(ph[7], 0.25) * 0.20f * u * u * (t < 0.60f ? 1f : Math.Max(0f, 1f - (t - 0.60f) / 0.05f));
                a[i] = s;
            }
            return Finish(a, rate, 0.80f);
        }

        // It lets go: a deep drop and a burst of air.
        static float[] MakeBoom(int rate)
        {
            int n = (int)(rate * 0.75f); var a = new float[n]; var r = new Rnd(23); var lp = new Svf(); double ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                ph += Glide(220f, 48f, t / 0.35f) / rate;
                lp.Step(r.Next(), Glide(2600f, 260f, t / 0.4f), 1.0f, rate);
                a[i] = (Sin(ph) * 0.8f + Sin(ph * 2) * 0.35f) * Exp(t, 5f) + (lp.Low * 0.9f + lp.Band * 1.1f) * Attack(t, 0.003f) * Exp(t, 7f);
            }
            return Finish(a, rate, 0.90f);
        }

        // Fire: a roar that swells and dies, crackling.
        static float[] MakeFire(int rate)
        {
            int n = (int)(rate * 0.95f); var a = new float[n]; var r = new Rnd(31); var bp = new Svf(); var lp = new Svf(); double ph = 0; float crack = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, u = t / 0.95f;
                float centre = u < 0.25f ? Glide(260f, 1500f, u / 0.25f) : Glide(1500f, 320f, (u - 0.25f) / 0.75f);
                bp.Step(r.Next(), centre, 0.9f, rate);
                if (r.Unit() < 60f / rate) crack = 1f;                    // a crackle now and then
                crack *= Exp(1f / rate, 900f);
                lp.Step(r.Next() * crack, 3500f, 1.4f, rate);
                ph += 62.0 / rate;
                float env = Attack(t, 0.03f) * Exp(t, 3.2f);
                a[i] = (bp.Band * 2.2f + bp.Low * 0.4f) * env + lp.Low * 1.6f * Exp(t, 2f) + Sin(ph * 2) * 0.12f * env;
            }
            return Finish(a, rate, 0.80f);
        }

        // Thunder: a zap falling through the scale, buzzing, and the crackle after it.
        static float[] MakeThunder(int rate)
        {
            int n = (int)(rate * 0.75f); var a = new float[n]; var r = new Rnd(41); var hp = new Svf(); double ph = 0, buzz = 0; float gate = 0f; int hold = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                ph += Glide(2600f, 110f, t / 0.28f) / rate; buzz += 53.0 / rate;
                float zap = Saw(ph) * (0.55f + 0.45f * Square(buzz, 0.5)) * Exp(t, 6f);
                if (hold-- <= 0) { hold = rate / 140; gate = r.Unit() < 0.55f * Exp(t, 3.5f) + 0.05f ? 1f : 0f; }
                hp.Step(r.Next() * gate, 3200f, 0.6f, rate);
                a[i] = zap * 0.9f + hp.Band * 0.9f * Exp(t, 2.5f);
            }
            return Finish(a, rate, 0.78f);
        }

        // Wind: a gust rushing past.
        static float[] MakeWind(int rate)
        {
            int n = (int)(rate * 0.95f); var a = new float[n]; var r = new Rnd(53); var bp = new Svf(); var bp2 = new Svf();
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, u = t / 0.95f;
                float swell = (float)Math.Sin(Math.PI * Math.Pow(u, 0.6));
                float centre = 420f + 2300f * swell * swell;
                float x = r.Next();
                bp.Step(x, centre, 0.28f, rate); bp2.Step(x, centre * 2.1f, 0.5f, rate);
                a[i] = (bp.Band * 0.9f + bp2.Band * 0.35f) * swell * (0.85f + 0.15f * Sin(t * 13));
            }
            return Finish(a, rate, 0.72f);
        }

        // Water: a splash, and bubbles coming up after it.
        static float[] MakeWater(int rate)
        {
            int n = (int)(rate * 0.85f); var a = new float[n]; var r = new Rnd(67); var hp = new Svf(); var lp = new Svf();
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float x = r.Next();
                hp.Step(x, 2600f, 0.7f, rate); lp.Step(x, 700f, 1.2f, rate);
                a[i] = hp.Band * 0.75f * Attack(t, 0.004f) * Exp(t, 7f) + lp.Low * 0.9f * Attack(t, 0.01f) * Exp(t, 12f);
            }
            for (int b = 0; b < 12; b++)
            {
                float at = 0.04f + 0.6f * r.Unit(), f0 = 420f + 1300f * r.Unit(), len = 0.035f + 0.04f * r.Unit(), amp = 0.5f * (1f - at);
                int i0 = (int)(at * rate), m = (int)(len * rate); double ph = 0;
                for (int i = 0; i < m && i0 + i < n; i++)
                {
                    float u = i / (float)m;
                    ph += f0 * (1f + 0.9f * u) / rate;
                    a[i0 + i] += Sin(ph) * amp * (float)Math.Sin(Math.PI * u);
                }
            }
            return Finish(a, rate, 0.76f);
        }

        // Blade: steel through the air, and its ring.
        static float[] MakeBlade(int rate)
        {
            int n = (int)(rate * 0.60f); var a = new float[n]; var r = new Rnd(71); var bp = new Svf(); double[] ph = new double[3]; float[] fr = { 2637f, 3951f, 5588f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate, s = 0f;
                bp.Step(r.Next(), Glide(6500f, 2200f, t / 0.14f), 0.35f, rate);
                s += bp.Band * 1.1f * Attack(t, 0.003f) * Exp(t, 16f);
                for (int k = 0; k < 3; k++) { ph[k] += fr[k] / rate; s += Sin(ph[k]) * (0.38f / (1 + k)) * Attack(t, 0.02f) * Exp(t, 7f + 2 * k); }
                a[i] = s;
            }
            return Finish(a, rate, 0.72f);
        }

        // Dark: something drawing breath, then a growl sinking away.
        static float[] MakeDark(int rate)
        {
            int n = (int)(rate * 0.95f); var a = new float[n]; var r = new Rnd(83); var lp = new Svf(); var lp2 = new Svf(); double p1 = 0, p2 = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float f = Glide(110f, 55f, t / 0.9f);
                p1 += f / rate; p2 += f * 1.06f / rate;
                lp.Step(Saw(p1) + Saw(p2) + Square(p1 * 2, 0.5) * 0.5f, 900f, 0.8f, rate);
                float growl = lp.Low * 0.7f * Attack(t, 0.02f) * Exp(t, 2.6f) * (0.7f + 0.3f * Sin(t * 9));
                lp2.Step(r.Next(), Glide(300f, 2400f, t / 0.22f), 0.7f, rate);
                float breath = t < 0.22f ? lp2.Band * 0.9f * (t / 0.22f) * (t / 0.22f) : 0f;
                a[i] = growl + breath;
            }
            return Finish(a, rate, 0.80f);
        }

        // Light: a run of bells going up.
        static float[] MakeLight(int rate)
        {
            int n = (int)(rate * 0.90f); var a = new float[n]; var r = new Rnd(97); var hp = new Svf();
            float[] notes = { 1046.5f, 1318.5f, 1568f, 2093f, 2637f };
            for (int k = 0; k < notes.Length; k++)
            {
                int i0 = (int)(k * 0.048f * rate); double ph = 0, ph2 = 0;
                for (int i = i0; i < n; i++)
                {
                    float t = (i - i0) / (float)rate;
                    ph += notes[k] / rate; ph2 += notes[k] * 2.76f / rate;
                    a[i] += (Sin(ph) * 0.42f + Sin(ph2) * 0.12f * Exp(t, 12f)) * Attack(t, 0.002f) * Exp(t, 5.5f);
                }
            }
            for (int i = 0; i < n; i++) { float t = i / (float)rate; hp.Step(r.Next(), 7000f, 1f, rate); a[i] += hp.High * 0.10f * Exp(t, 4f); }
            return Finish(a, rate, 0.66f);
        }

        // Earth: a heavy blow, the ground rumbling, clods coming down.
        static float[] MakeEarth(int rate)
        {
            int n = (int)(rate * 0.95f); var a = new float[n]; var r = new Rnd(101); var lp = new Svf(); var lp2 = new Svf(); double ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                ph += Glide(170f, 52f, t / 0.3f) / rate;
                lp.Step(r.Next(), Glide(900f, 240f, t / 0.5f), 1.0f, rate);
                a[i] = (Sin(ph) * 0.8f + Sin(ph * 3) * 0.2f) * Exp(t, 4.2f) + (lp.Low * 1.2f + lp.Band * 0.8f) * Attack(t, 0.004f) * Exp(t, 3.2f);
            }
            for (int b = 0; b < 16; b++)
            {
                float at = 0.06f + 0.6f * r.Unit(), amp = 0.6f * (1f - at);
                int i0 = (int)(at * rate), m = (int)(0.012f * rate);
                for (int i = 0; i < m && i0 + i < n; i++) { lp2.Step(r.Next(), 1300f, 1.1f, rate); a[i0 + i] += lp2.Low * amp * (1f - i / (float)m); }
            }
            return Finish(a, rate, 0.86f);
        }
    }
}
