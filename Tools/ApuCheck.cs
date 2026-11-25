// Unit test of the sound chip: writes raw float32/48k mono files (apu_pulse.raw, apu_tri.raw, apu_env.raw, apu_noise.raw)
// for a 440.4 Hz pulse, a 440.4 Hz triangle, a decaying envelope and noise.   usage: ApuCheck soccer.nes
using System;
using System.IO;
using System.Collections.Generic;
using NesCore;

static class ApuCheck
{
    static List<float> Run(Nes nes, int cycles)
    {
        var l = new List<float>();
        long end = nes.Cpu.Cycles + cycles;
        while (nes.Cpu.Cycles < end) { nes.Cpu.Cycles += 1000; nes.Apu.RunTo(nes.Cpu.Cycles); for (int i = 0; i < nes.Apu.OutCount; i++) l.Add(nes.Apu.Out[i]); nes.Apu.OutCount = 0; }
        return l;
    }
    static void Save(string name, List<float> l) { byte[] b = new byte[l.Count * 4]; Buffer.BlockCopy(l.ToArray(), 0, b, 0, b.Length); File.WriteAllBytes(name, b); }
    static int Main(string[] a)
    {
        var nes = new Nes();
        nes.LoadRom(File.ReadAllBytes(a[0]));
        var A = nes.Apu;
        // pulse 1: period 253 -> 1789773 / (16 * 254) = 440.40 Hz, 50% duty, constant volume 15
        A.Write(0x4015, 0x01); A.Write(0x4000, 0xBF); A.Write(0x4001, 0x08); A.Write(0x4002, 253); A.Write(0x4003, 0x08);
        Save("apu_pulse.raw", Run(nes, 1789773));
        // triangle: period 126 -> 1789773 / (32 * 127) = 440.40 Hz
        A.Write(0x4015, 0x04); A.Write(0x4008, 0xFF); A.Write(0x400A, 126); A.Write(0x400B, 0x08);
        Save("apu_tri.raw", Run(nes, 1789773));
        // pulse 2 with a decaying envelope (period 3 -> volume drops one step every 4 quarter frames = 60 steps/s): should fade out in 0.25 s and stay silent
        A.Write(0x4015, 0x02); A.Write(0x4004, 0x83); A.Write(0x4005, 0x08); A.Write(0x4006, 253); A.Write(0x4007, 0x08);
        Save("apu_env.raw", Run(nes, 1789773));
        // noise, long mode, period index 8
        A.Write(0x4015, 0x08); A.Write(0x400C, 0x3F); A.Write(0x400E, 0x08); A.Write(0x400F, 0x08);
        Save("apu_noise.raw", Run(nes, 1789773));
        // record the note periods the game itself asks for, to see what it considers "in tune"
        nes.Reset();
        return 0;
    }
}
