// NES sound chip (2A03 APU): two pulse channels, triangle, noise, DMC.  Clocked per CPU cycle, mixed with the
// usual non-linear formulas and box-filtered down to the output sample rate.  Frame/DMC interrupts are not raised
// (the game does not use them - its RAM trace matches Mesen without them).
using System;

namespace NesCore
{
    public sealed class Apu
    {
        public const double CpuHz = 1789772.7272;
        public int SampleRate = 48000;
        /// <summary>Mono samples produced since the consumer last took them (-1..1).</summary>
        public readonly float[] Out = new float[8192];
        public int OutCount;

        readonly Nes nes;
        long cycle;                 // CPU cycle the APU has been run up to

        static readonly byte[] LengthTable = { 10, 254, 20, 2, 40, 4, 80, 6, 160, 8, 60, 10, 14, 12, 26, 14, 12, 16, 24, 18, 48, 20, 96, 22, 192, 24, 72, 26, 16, 28, 32, 30 };
        static readonly byte[][] Duty = {
            new byte[] { 0, 1, 0, 0, 0, 0, 0, 0 }, new byte[] { 0, 1, 1, 0, 0, 0, 0, 0 },
            new byte[] { 0, 1, 1, 1, 1, 0, 0, 0 }, new byte[] { 1, 0, 0, 1, 1, 1, 1, 1 } };
        static readonly int[] NoisePeriod = { 4, 8, 16, 32, 64, 96, 128, 160, 202, 254, 380, 508, 762, 1016, 2034, 4068 };
        static readonly int[] DmcPeriod = { 428, 380, 340, 320, 286, 254, 226, 214, 190, 160, 142, 128, 106, 84, 72, 54 };

        sealed class Envelope
        {
            public bool Start, Loop, Constant; public int Period, Divider, Decay;
            public void Clock()
            {
                if (Start) { Start = false; Decay = 15; Divider = Period; return; }
                if (Divider > 0) { Divider--; return; }
                Divider = Period;
                if (Decay > 0) Decay--; else if (Loop) Decay = 15;
            }
            public int Volume { get { return Constant ? Period : Decay; } }
        }

        sealed class Pulse
        {
            public bool Enabled, OnesComplement;
            public int DutyMode, Seq, Timer, Period, Length;
            public readonly Envelope Env = new Envelope();
            public bool SweepEnabled, SweepNegate, SweepReload; public int SweepPeriod, SweepShift, SweepDivider;
            int Target() { int d = Period >> SweepShift; return SweepNegate ? Period - d - (OnesComplement ? 1 : 0) : Period + d; }
            bool Muted() { return Period < 8 || Target() > 0x7FF; }
            public void ClockTimer() { if (Timer == 0) { Timer = Period; Seq = (Seq + 1) & 7; } else Timer--; }
            public void ClockLength() { if (!Env.Loop && Length > 0) Length--; }
            public void ClockSweep()
            {
                if (SweepDivider == 0 && SweepEnabled && SweepShift > 0 && !Muted()) Period = Math.Max(0, Target());
                if (SweepDivider == 0 || SweepReload) { SweepDivider = SweepPeriod; SweepReload = false; } else SweepDivider--;
            }
            public int Output() { return (Length > 0 && Duty[DutyMode][Seq] != 0 && !Muted()) ? Env.Volume : 0; }
        }

        readonly Pulse p1 = new Pulse { OnesComplement = true }, p2 = new Pulse();

        // triangle
        bool triEnabled, triControl, triReload; int triTimer, triPeriod, triLength, triLinear, triLinearLoad, triSeq;
        // noise
        bool noiEnabled, noiMode; int noiTimer, noiPeriod = 4, noiLength, lfsr = 1; readonly Envelope noiEnv = new Envelope();
        // DMC
        bool dmcLoop, dmcSilence = true, dmcBufferFull; int dmcTimer, dmcPeriod = 428, dmcLevel, dmcAddr = 0xC000, dmcLen = 1, dmcCur, dmcRemaining, dmcShift, dmcBits = 8, dmcBuffer;
        // frame sequencer
        bool fiveStep; int frameCycle;
        // output filter
        double acc, sum; int sumN; float hp, last;
        bool odd;

        public Apu(Nes n) { nes = n; }

        public void Reset()
        {
            cycle = nes.Cpu != null ? nes.Cpu.Cycles : 0; frameCycle = 0; fiveStep = false; OutCount = 0;
            p1.Enabled = p2.Enabled = triEnabled = noiEnabled = false; p1.Length = p2.Length = triLength = noiLength = 0; dmcRemaining = 0; dmcLevel = 0; lfsr = 1;
        }

        void Quarter()
        {
            p1.Env.Clock(); p2.Env.Clock(); noiEnv.Clock();
            if (triReload) triLinear = triLinearLoad; else if (triLinear > 0) triLinear--;
            if (!triControl) triReload = false;
        }

        void Half()
        {
            p1.ClockLength(); p2.ClockLength(); p1.ClockSweep(); p2.ClockSweep();
            if (!triControl && triLength > 0) triLength--;
            if (!noiEnv.Loop && noiLength > 0) noiLength--;
        }

        /// <summary>Runs the chip up to the given CPU cycle.</summary>
        public void RunTo(long cpuCycle)
        {
            double step = SampleRate / CpuHz;
            while (cycle < cpuCycle)
            {
                cycle++;
                // frame sequencer
                frameCycle++;
                if (frameCycle == 7457 || frameCycle == 22371) Quarter();
                else if (frameCycle == 14913) { Quarter(); Half(); }
                else if (!fiveStep && frameCycle == 29829) { Quarter(); Half(); }
                else if (fiveStep && frameCycle == 37281) { Quarter(); Half(); }
                if (frameCycle >= (fiveStep ? 37282 : 29830)) frameCycle = 0;
                // timers
                odd = !odd;
                if (odd) { p1.ClockTimer(); p2.ClockTimer(); }
                if (triTimer == 0) { triTimer = triPeriod; if (triLength > 0 && triLinear > 0 && triPeriod >= 2) triSeq = (triSeq + 1) & 31; } else triTimer--;
                if (noiTimer == 0)
                {
                    noiTimer = noiPeriod - 1;
                    int fb = (lfsr ^ (lfsr >> (noiMode ? 6 : 1))) & 1;
                    lfsr = (lfsr >> 1) | (fb << 14);
                }
                else noiTimer--;
                if (dmcTimer == 0) { dmcTimer = dmcPeriod - 1; ClockDmc(); } else dmcTimer--;
                // mix
                int a = p1.Output(), b = p2.Output();
                int t = triSeq < 16 ? 15 - triSeq : triSeq - 16;
                int n = (noiLength > 0 && (lfsr & 1) == 0) ? noiEnv.Volume : 0;
                double pulse = (a + b) == 0 ? 0 : 95.88 / (8128.0 / (a + b) + 100.0);
                double tnd = (t + n + dmcLevel) == 0 ? 0 : 159.79 / (1.0 / (t / 8227.0 + n / 12241.0 + dmcLevel / 22638.0) + 100.0);
                sum += pulse + tnd; sumN++;
                acc += step;
                if (acc >= 1.0)
                {
                    acc -= 1.0;
                    float s = (float)(sum / sumN); sum = 0; sumN = 0;
                    // remove the DC offset (one-pole high-pass, ~30 Hz)
                    hp = 0.996f * (hp + s - last); last = s;
                    if (OutCount < Out.Length) Out[OutCount++] = Math.Max(-1f, Math.Min(1f, hp * 1.6f));
                }
            }
        }

        void ClockDmc()
        {
            if (!dmcBufferFull && dmcRemaining > 0)
            {
                dmcBuffer = nes.CpuPeek((ushort)dmcCur); dmcBufferFull = true;
                dmcCur = dmcCur == 0xFFFF ? 0x8000 : dmcCur + 1;
                if (--dmcRemaining == 0 && dmcLoop) { dmcCur = dmcAddr; dmcRemaining = dmcLen; }
            }
            if (!dmcSilence)
            {
                if ((dmcShift & 1) != 0) { if (dmcLevel <= 125) dmcLevel += 2; } else { if (dmcLevel >= 2) dmcLevel -= 2; }
                dmcShift >>= 1;
            }
            if (--dmcBits == 0)
            {
                dmcBits = 8;
                if (dmcBufferFull) { dmcSilence = false; dmcShift = dmcBuffer; dmcBufferFull = false; } else dmcSilence = true;
            }
        }

        void WritePulse(Pulse p, int r, byte v)
        {
            switch (r)
            {
                case 0: p.DutyMode = v >> 6; p.Env.Loop = (v & 0x20) != 0; p.Env.Constant = (v & 0x10) != 0; p.Env.Period = v & 15; break;
                case 1: p.SweepEnabled = (v & 0x80) != 0; p.SweepPeriod = (v >> 4) & 7; p.SweepNegate = (v & 8) != 0; p.SweepShift = v & 7; p.SweepReload = true; break;
                case 2: p.Period = (p.Period & 0x700) | v; break;
                case 3: p.Period = (p.Period & 0xFF) | ((v & 7) << 8); if (p.Enabled) p.Length = LengthTable[v >> 3]; p.Seq = 0; p.Env.Start = true; break;
            }
        }

        public void Write(int addr, byte v)
        {
            RunTo(nes.Cpu.Cycles);
            switch (addr)
            {
                case 0x4000: case 0x4001: case 0x4002: case 0x4003: WritePulse(p1, addr & 3, v); break;
                case 0x4004: case 0x4005: case 0x4006: case 0x4007: WritePulse(p2, addr & 3, v); break;
                case 0x4008: triControl = (v & 0x80) != 0; triLinearLoad = v & 0x7F; break;
                case 0x400A: triPeriod = (triPeriod & 0x700) | v; break;
                case 0x400B: triPeriod = (triPeriod & 0xFF) | ((v & 7) << 8); if (triEnabled) triLength = LengthTable[v >> 3]; triReload = true; break;
                case 0x400C: noiEnv.Loop = (v & 0x20) != 0; noiEnv.Constant = (v & 0x10) != 0; noiEnv.Period = v & 15; break;
                case 0x400E: noiMode = (v & 0x80) != 0; noiPeriod = NoisePeriod[v & 15]; break;
                case 0x400F: if (noiEnabled) noiLength = LengthTable[v >> 3]; noiEnv.Start = true; break;
                case 0x4010: dmcLoop = (v & 0x40) != 0; dmcPeriod = DmcPeriod[v & 15]; break;
                case 0x4011: dmcLevel = v & 0x7F; break;
                case 0x4012: dmcAddr = 0xC000 + v * 64; break;
                case 0x4013: dmcLen = v * 16 + 1; break;
                case 0x4015:
                    p1.Enabled = (v & 1) != 0; if (!p1.Enabled) p1.Length = 0;
                    p2.Enabled = (v & 2) != 0; if (!p2.Enabled) p2.Length = 0;
                    triEnabled = (v & 4) != 0; if (!triEnabled) triLength = 0;
                    noiEnabled = (v & 8) != 0; if (!noiEnabled) noiLength = 0;
                    if ((v & 16) == 0) dmcRemaining = 0;
                    else if (dmcRemaining == 0) { dmcCur = dmcAddr; dmcRemaining = dmcLen; }
                    break;
                case 0x4017:
                    fiveStep = (v & 0x80) != 0; frameCycle = 0;
                    if (fiveStep) { Quarter(); Half(); }
                    break;
            }
        }

        public byte ReadStatus()
        {
            RunTo(nes.Cpu.Cycles);
            int s = 0;
            if (p1.Length > 0) s |= 1; if (p2.Length > 0) s |= 2; if (triLength > 0) s |= 4; if (noiLength > 0) s |= 8; if (dmcRemaining > 0) s |= 16;
            return (byte)s;
        }
    }
}
