// 6502 CPU core (Ricoh 2A03: no decimal mode).  Instruction-stepped with cycle counts.
using System;

namespace NesCore
{
    public sealed class Cpu
    {
        public byte A, X, Y, S, P;
        public ushort PC;
        public long Cycles;
        public bool NmiPending;
        public Func<bool> IrqLine;          // level triggered
        readonly Nes nes;

        const byte C = 1, Z = 2, I = 4, D = 8, B = 16, U = 32, V = 64, N = 128;

        enum M : byte { IMP, ACC, IMM, ZP, ZPX, ZPY, ABS, ABX, ABY, IND, IZX, IZY, REL }
        enum O : byte
        {
            ADC, AND, ASL, BCC, BCS, BEQ, BIT, BMI, BNE, BPL, BRK, BVC, BVS, CLC, CLD, CLI, CLV, CMP, CPX, CPY, DEC, DEX, DEY, EOR,
            INC, INX, INY, JMP, JSR, LDA, LDX, LDY, LSR, NOP, ORA, PHA, PHP, PLA, PLP, ROL, ROR, RTI, RTS, SBC, SEC, SED, SEI, STA,
            STX, STY, TAX, TAY, TSX, TXA, TXS, TYA,
            // unofficial
            LAX, SAX, DCP, ISC, SLO, RLA, SRE, RRA, KIL
        }

        static readonly O[] op = new O[256];
        static readonly M[] mode = new M[256];
        static readonly byte[] cyc = new byte[256];
        static readonly bool[] pagePenalty = new bool[256];

        static void Def(int code, O o, M m, int c, bool pp = false) { op[code] = o; mode[code] = m; cyc[code] = (byte)c; pagePenalty[code] = pp; }

        static Cpu()
        {
            for (int i = 0; i < 256; i++) Def(i, O.NOP, M.IMP, 2);
            // group helpers for the regular "cc = 01" instructions
            void G1(int baseCode, O o, bool store = false)
            {
                Def(baseCode + 0x01, o, M.IZX, 6); Def(baseCode + 0x05, o, M.ZP, 3);
                if (!store) Def(baseCode + 0x09, o, M.IMM, 2);
                Def(baseCode + 0x0D, o, M.ABS, 4);
                Def(baseCode + 0x11, o, M.IZY, store ? 6 : 5, !store); Def(baseCode + 0x15, o, M.ZPX, 4);
                Def(baseCode + 0x19, o, M.ABY, store ? 5 : 4, !store); Def(baseCode + 0x1D, o, M.ABX, store ? 5 : 4, !store);
            }
            G1(0x00, O.ORA); G1(0x20, O.AND); G1(0x40, O.EOR); G1(0x60, O.ADC); G1(0x80, O.STA, true); G1(0xA0, O.LDA); G1(0xC0, O.CMP); G1(0xE0, O.SBC);
            void Rmw(int baseCode, O o, bool acc = true)
            {
                Def(baseCode + 0x06, o, M.ZP, 5); if (acc) Def(baseCode + 0x0A, o, M.ACC, 2);
                Def(baseCode + 0x0E, o, M.ABS, 6); Def(baseCode + 0x16, o, M.ZPX, 6); Def(baseCode + 0x1E, o, M.ABX, 7);
            }
            Rmw(0x00, O.ASL); Rmw(0x20, O.ROL); Rmw(0x40, O.LSR); Rmw(0x60, O.ROR); Rmw(0xC0, O.DEC, false); Rmw(0xE0, O.INC, false);
            Def(0x00, O.BRK, M.IMP, 7); Def(0x08, O.PHP, M.IMP, 3); Def(0x10, O.BPL, M.REL, 2); Def(0x18, O.CLC, M.IMP, 2);
            Def(0x20, O.JSR, M.ABS, 6); Def(0x24, O.BIT, M.ZP, 3); Def(0x28, O.PLP, M.IMP, 4); Def(0x2C, O.BIT, M.ABS, 4);
            Def(0x30, O.BMI, M.REL, 2); Def(0x38, O.SEC, M.IMP, 2); Def(0x40, O.RTI, M.IMP, 6); Def(0x48, O.PHA, M.IMP, 3);
            Def(0x4C, O.JMP, M.ABS, 3); Def(0x50, O.BVC, M.REL, 2); Def(0x58, O.CLI, M.IMP, 2); Def(0x60, O.RTS, M.IMP, 6);
            Def(0x68, O.PLA, M.IMP, 4); Def(0x6C, O.JMP, M.IND, 5); Def(0x70, O.BVS, M.REL, 2); Def(0x78, O.SEI, M.IMP, 2);
            Def(0x84, O.STY, M.ZP, 3); Def(0x86, O.STX, M.ZP, 3); Def(0x88, O.DEY, M.IMP, 2); Def(0x8A, O.TXA, M.IMP, 2);
            Def(0x8C, O.STY, M.ABS, 4); Def(0x8E, O.STX, M.ABS, 4); Def(0x90, O.BCC, M.REL, 2); Def(0x94, O.STY, M.ZPX, 4);
            Def(0x96, O.STX, M.ZPY, 4); Def(0x98, O.TYA, M.IMP, 2); Def(0x9A, O.TXS, M.IMP, 2);
            Def(0xA0, O.LDY, M.IMM, 2); Def(0xA2, O.LDX, M.IMM, 2); Def(0xA4, O.LDY, M.ZP, 3); Def(0xA6, O.LDX, M.ZP, 3);
            Def(0xA8, O.TAY, M.IMP, 2); Def(0xAA, O.TAX, M.IMP, 2); Def(0xAC, O.LDY, M.ABS, 4); Def(0xAE, O.LDX, M.ABS, 4);
            Def(0xB0, O.BCS, M.REL, 2); Def(0xB4, O.LDY, M.ZPX, 4); Def(0xB6, O.LDX, M.ZPY, 4); Def(0xB8, O.CLV, M.IMP, 2);
            Def(0xBA, O.TSX, M.IMP, 2); Def(0xBC, O.LDY, M.ABX, 4, true); Def(0xBE, O.LDX, M.ABY, 4, true);
            Def(0xC0, O.CPY, M.IMM, 2); Def(0xC4, O.CPY, M.ZP, 3); Def(0xC8, O.INY, M.IMP, 2); Def(0xCA, O.DEX, M.IMP, 2);
            Def(0xCC, O.CPY, M.ABS, 4); Def(0xD0, O.BNE, M.REL, 2); Def(0xD8, O.CLD, M.IMP, 2);
            Def(0xE0, O.CPX, M.IMM, 2); Def(0xE4, O.CPX, M.ZP, 3); Def(0xE8, O.INX, M.IMP, 2); Def(0xEA, O.NOP, M.IMP, 2);
            Def(0xEC, O.CPX, M.ABS, 4); Def(0xF0, O.BEQ, M.REL, 2); Def(0xF8, O.SED, M.IMP, 2);
            // unofficial NOPs with operands (so the PC stays in sync if a game ever executes them)
            foreach (int c in new[] { 0x80, 0x82, 0x89, 0xC2, 0xE2 }) Def(c, O.NOP, M.IMM, 2);
            foreach (int c in new[] { 0x04, 0x44, 0x64 }) Def(c, O.NOP, M.ZP, 3);
            foreach (int c in new[] { 0x14, 0x34, 0x54, 0x74, 0xD4, 0xF4 }) Def(c, O.NOP, M.ZPX, 4);
            Def(0x0C, O.NOP, M.ABS, 4);
            foreach (int c in new[] { 0x1C, 0x3C, 0x5C, 0x7C, 0xDC, 0xFC }) Def(c, O.NOP, M.ABX, 4, true);
            Def(0xEB, O.SBC, M.IMM, 2);
            void Ill(int b, O o, bool rmw)
            {
                Def(b + 0x03, o, M.IZX, rmw ? 8 : 6); Def(b + 0x07, o, M.ZP, rmw ? 5 : 3); Def(b + 0x0F, o, M.ABS, rmw ? 6 : 4);
                if (rmw) { Def(b + 0x13, o, M.IZY, 8); Def(b + 0x17, o, M.ZPX, 6); Def(b + 0x1B, o, M.ABY, 7); Def(b + 0x1F, o, M.ABX, 7); }
            }
            Ill(0x00, O.SLO, true); Ill(0x20, O.RLA, true); Ill(0x40, O.SRE, true); Ill(0x60, O.RRA, true); Ill(0xC0, O.DCP, true); Ill(0xE0, O.ISC, true);
            Ill(0x80, O.SAX, false); Def(0x97, O.SAX, M.ZPY, 4);
            Ill(0xA0, O.LAX, false); Def(0xB3, O.LAX, M.IZY, 5, true); Def(0xB7, O.LAX, M.ZPY, 4); Def(0xBF, O.LAX, M.ABY, 4, true);
        }

        public Cpu(Nes n) { nes = n; }

        byte Rd(int a) => nes.CpuRead((ushort)a);
        void Wr(int a, byte v) => nes.CpuWrite((ushort)a, v);
        void Push(byte v) { Wr(0x100 | S, v); S--; }
        byte Pop() { S++; return Rd(0x100 | S); }
        void SetZN(byte v) { P = (byte)((P & ~(Z | N)) | (v == 0 ? Z : 0) | (v & 0x80)); }

        public void Reset()
        {
            A = X = Y = 0; S = 0xFD; P = (byte)(I | U);
            PC = (ushort)(Rd(0xFFFC) | (Rd(0xFFFD) << 8));
            Cycles = 7; NmiPending = false;
        }

        void Interrupt(ushort vector, bool brk)
        {
            Push((byte)(PC >> 8)); Push((byte)PC);
            Push((byte)((P & ~B) | U | (brk ? B : 0)));
            P |= I;
            PC = (ushort)(Rd(vector) | (Rd(vector + 1) << 8));
            Cycles += 7;
        }

        void Branch(bool cond, int target)
        {
            if (!cond) return;
            Cycles += ((PC & 0xFF00) != (target & 0xFF00)) ? 2 : 1;
            PC = (ushort)target;
        }

        void Adc(byte v)
        {
            int r = A + v + (P & C);
            P = (byte)((P & ~(C | V)) | (r > 0xFF ? C : 0) | (((~(A ^ v) & (A ^ r)) & 0x80) != 0 ? V : 0));
            A = (byte)r; SetZN(A);
        }

        void Cmp(byte reg, byte v)
        {
            int r = reg - v;
            P = (byte)((P & ~C) | (reg >= v ? C : 0));
            SetZN((byte)r);
        }

        public void Step()
        {
            if (NmiPending) { NmiPending = false; Interrupt(0xFFFA, false); return; }
            if ((P & I) == 0 && IrqLine != null && IrqLine()) { Interrupt(0xFFFE, false); return; }

            byte code = Rd(PC++);
            Cycles += cyc[code];
            int addr = 0;
            bool pp = pagePenalty[code];
            switch (mode[code])
            {
                case M.IMM: addr = PC++; break;
                case M.ZP: addr = Rd(PC++); break;
                case M.ZPX: addr = (Rd(PC++) + X) & 0xFF; break;
                case M.ZPY: addr = (Rd(PC++) + Y) & 0xFF; break;
                case M.ABS: addr = Rd(PC) | (Rd(PC + 1) << 8); PC += 2; break;
                case M.ABX: { int b = Rd(PC) | (Rd(PC + 1) << 8); PC += 2; addr = (b + X) & 0xFFFF; if (pp && ((b ^ addr) & 0xFF00) != 0) Cycles++; break; }
                case M.ABY: { int b = Rd(PC) | (Rd(PC + 1) << 8); PC += 2; addr = (b + Y) & 0xFFFF; if (pp && ((b ^ addr) & 0xFF00) != 0) Cycles++; break; }
                case M.IND: { int p = Rd(PC) | (Rd(PC + 1) << 8); PC += 2; addr = Rd(p) | (Rd((p & 0xFF00) | ((p + 1) & 0xFF)) << 8); break; }
                case M.IZX: { int z = (Rd(PC++) + X) & 0xFF; addr = Rd(z) | (Rd((z + 1) & 0xFF) << 8); break; }
                case M.IZY: { int z = Rd(PC++); int b = Rd(z) | (Rd((z + 1) & 0xFF) << 8); addr = (b + Y) & 0xFFFF; if (pp && ((b ^ addr) & 0xFF00) != 0) Cycles++; break; }
                case M.REL: { int off = (sbyte)Rd(PC++); addr = (PC + off) & 0xFFFF; break; }
            }
            bool acc = mode[code] == M.ACC;
            byte v, t;
            switch (op[code])
            {
                case O.LDA: A = Rd(addr); SetZN(A); break;
                case O.LDX: X = Rd(addr); SetZN(X); break;
                case O.LDY: Y = Rd(addr); SetZN(Y); break;
                case O.STA: Wr(addr, A); break;
                case O.STX: Wr(addr, X); break;
                case O.STY: Wr(addr, Y); break;
                case O.TAX: X = A; SetZN(X); break;
                case O.TAY: Y = A; SetZN(Y); break;
                case O.TXA: A = X; SetZN(A); break;
                case O.TYA: A = Y; SetZN(A); break;
                case O.TSX: X = S; SetZN(X); break;
                case O.TXS: S = X; break;
                case O.PHA: Push(A); break;
                case O.PHP: Push((byte)(P | B | U)); break;
                case O.PLA: A = Pop(); SetZN(A); break;
                case O.PLP: P = (byte)((Pop() & ~B) | U); break;
                case O.AND: A &= Rd(addr); SetZN(A); break;
                case O.ORA: A |= Rd(addr); SetZN(A); break;
                case O.EOR: A ^= Rd(addr); SetZN(A); break;
                case O.ADC: Adc(Rd(addr)); break;
                case O.SBC: Adc((byte)~Rd(addr)); break;
                case O.CMP: Cmp(A, Rd(addr)); break;
                case O.CPX: Cmp(X, Rd(addr)); break;
                case O.CPY: Cmp(Y, Rd(addr)); break;
                case O.BIT: v = Rd(addr); P = (byte)((P & ~(Z | V | N)) | ((A & v) == 0 ? Z : 0) | (v & 0xC0)); break;
                case O.INC: v = (byte)(Rd(addr) + 1); Wr(addr, v); SetZN(v); break;
                case O.DEC: v = (byte)(Rd(addr) - 1); Wr(addr, v); SetZN(v); break;
                case O.INX: X++; SetZN(X); break;
                case O.INY: Y++; SetZN(Y); break;
                case O.DEX: X--; SetZN(X); break;
                case O.DEY: Y--; SetZN(Y); break;
                case O.ASL: v = acc ? A : Rd(addr); P = (byte)((P & ~C) | (v >> 7)); v <<= 1; if (acc) A = v; else Wr(addr, v); SetZN(v); break;
                case O.LSR: v = acc ? A : Rd(addr); P = (byte)((P & ~C) | (v & 1)); v >>= 1; if (acc) A = v; else Wr(addr, v); SetZN(v); break;
                case O.ROL: v = acc ? A : Rd(addr); t = (byte)(P & C); P = (byte)((P & ~C) | (v >> 7)); v = (byte)((v << 1) | t); if (acc) A = v; else Wr(addr, v); SetZN(v); break;
                case O.ROR: v = acc ? A : Rd(addr); t = (byte)((P & C) << 7); P = (byte)((P & ~C) | (v & 1)); v = (byte)((v >> 1) | t); if (acc) A = v; else Wr(addr, v); SetZN(v); break;
                case O.JMP: PC = (ushort)addr; break;
                case O.JSR: { int ret = PC - 1; Push((byte)(ret >> 8)); Push((byte)ret); PC = (ushort)addr; break; }
                case O.RTS: { int lo = Pop(); int hi = Pop(); PC = (ushort)(((hi << 8) | lo) + 1); break; }
                case O.RTI: { P = (byte)((Pop() & ~B) | U); int lo = Pop(); int hi = Pop(); PC = (ushort)((hi << 8) | lo); break; }
                case O.BRK: PC++; Cycles -= 7; Interrupt(0xFFFE, true); break;
                case O.BCC: Branch((P & C) == 0, addr); break;
                case O.BCS: Branch((P & C) != 0, addr); break;
                case O.BEQ: Branch((P & Z) != 0, addr); break;
                case O.BNE: Branch((P & Z) == 0, addr); break;
                case O.BMI: Branch((P & N) != 0, addr); break;
                case O.BPL: Branch((P & N) == 0, addr); break;
                case O.BVS: Branch((P & V) != 0, addr); break;
                case O.BVC: Branch((P & V) == 0, addr); break;
                case O.CLC: P &= unchecked((byte)~C); break;
                case O.SEC: P |= C; break;
                case O.CLI: P &= unchecked((byte)~I); break;
                case O.SEI: P |= I; break;
                case O.CLV: P &= unchecked((byte)~V); break;
                case O.CLD: P &= unchecked((byte)~D); break;
                case O.SED: P |= D; break;
                case O.NOP: if (mode[code] != M.IMP && mode[code] != M.IMM) Rd(addr); break;
                // unofficial
                case O.LAX: A = X = Rd(addr); SetZN(A); break;
                case O.SAX: Wr(addr, (byte)(A & X)); break;
                case O.DCP: v = (byte)(Rd(addr) - 1); Wr(addr, v); Cmp(A, v); break;
                case O.ISC: v = (byte)(Rd(addr) + 1); Wr(addr, v); Adc((byte)~v); break;
                case O.SLO: v = Rd(addr); P = (byte)((P & ~C) | (v >> 7)); v <<= 1; Wr(addr, v); A |= v; SetZN(A); break;
                case O.RLA: v = Rd(addr); t = (byte)(P & C); P = (byte)((P & ~C) | (v >> 7)); v = (byte)((v << 1) | t); Wr(addr, v); A &= v; SetZN(A); break;
                case O.SRE: v = Rd(addr); P = (byte)((P & ~C) | (v & 1)); v >>= 1; Wr(addr, v); A ^= v; SetZN(A); break;
                case O.RRA: v = Rd(addr); t = (byte)((P & C) << 7); P = (byte)((P & ~C) | (v & 1)); v = (byte)((v >> 1) | t); Wr(addr, v); Adc(v); break;
            }
        }
    }
}
