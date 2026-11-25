// Things the original cannot do, done by nudging its memory: computer v computer, and special shots on demand.
// Plain C# (no Unity) so that the command-line tools run exactly the same code (Tools/CpuVsCpu.cs).
//
//   Spectate     both teams are played by the game's own AI (the same flags its title-screen demo match uses):
//                bit 7 of the team numbers $052C/$052D and of every player's $0650-$065B, pads unbound ($0568-$056B = FF).
//   AllSpecials  every shot is the shooter's own special: the decision table tbl_BB16 says "special" for every distance
//                and the two "is the ball high enough" thresholds are 0 (patched in the emulator's copy of PRG only).
//   Press()      one key, one special.  Playing: the same patch goes on for a moment, for your team only (it is called off
//                as soon as the other team has the ball), and for a little over a second, whenever your player has the
//                ball, B is pressed for him again and again (towards the other goal) until the game starts a shot -
//                a single press is often lost while he is still busy receiving the ball or finishing a move.
//                Spectating: the next shot by anyone is a special.
//   Showcase()   all 24 special shots of the game in turn (Y key): the 12 home-team members' (each has his own; only 5 of
//                them are on the pitch at a time, so the shooter is given each member's number $0532 for his turn) and the
//                12 foreign teams' (one per team: for his turn the shooter's team number $052C/D says that team, until the
//                game has picked the special).  The shooters are your 5 outfield players in turn: one is put on a spot between the
//                centre and their goal with the ball at his feet (his and the ball's state written as when he dribbles it),
//                the other team stands still meanwhile (they wait for a pad that is not there), and B is pressed for him
//                until the special is written to $050F.  Between turns both teams are the computer's, so the shot is played
//                out and a kick-off after a goal goes on by itself.  Y again stops it.  Member and team numbers are put back
//                at the end.
// (The addresses were found by reading the game's code and watching its memory.)
using NesCore;

namespace Soccer
{
    public sealed class Tweaks
    {
        public bool Spectate;
        public bool AllSpecials;
        /// <summary>Last thing worth telling the player, and the frame it happened (for the on-screen note).</summary>
        public string Message;
        public long MessageFrame = -1000;
        /// <summary>Specials seen since start (any team), and the frame of the last one.</summary>
        public int Specials;
        public long LastSpecialFrame = -1;
        /// <summary>The latest special shot proper (its number $04-$0F, $16-$21: not the later stages $10-$15 some of them
        /// go through), who shot it (object slot), and a count that goes up with each - for the effects layer.</summary>
        public int LastSpecial, LastShooter = -1, SpecialSerial;

        const int TableAt = 0x13B16, TableLen = 8;          // tbl_BB16: which distance band gives which shot ($FF = the shooter's own special)
        const int HeightAt = 0x13DD7, HeightLen = 12;       // tbl_BDD7 + tbl_BDDD: how high the ball must be
        readonly Nes nes;
        readonly byte[] origTable = new byte[TableLen], origHeight = new byte[HeightLen];
        bool patched;

        // spectating: what the match looked like before we took over
        bool captured;
        byte teamBits0, teamBits1;
        readonly byte[] pads = new byte[4];

        // one-key special
        bool pressed;
        int pending;            // frames left in which we keep pressing B for him while he has the ball
        int cycle;              // position in the press pattern: 2 frames let go, 6 frames held
        bool shotStarted;       // the game has begun a shot ($050F = 00) since the key
        int armed;              // frames the patch stays on for this one special
        int armedTeam = -1;     // ... for this team (0/1; -1 = anyone): called off when the other team has the ball
        long pressFrame;

        // the showcase
        public bool Showing { get { return showTurn >= 0; } }
        public System.Action<string> Trace;     // (for the command-line tools)
        int showTeamWas = -1;
        readonly byte[] showMembers = new byte[12];
        // turn by turn: 00-0B a home-team member (his special is 04-0F), 1t foreign team t (their special: tbl_BD12 entry t)
        static readonly byte[] ShowList = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 0x11, 0x12, 0x13, 0x15, 0x16, 0x17, 0x19, 0x1A, 0x1B, 0x1D, 0x1E, 0x1F };
        static string ShowName(byte c)
        {
            if (c < 0x10) return "主队第 " + (c + 1) + " 名队员";
            switch (c & 0x0F)
            {
                case 0x7: return "喀麦隆队"; case 0x9: return "德国队"; case 0xA: return "英格兰队"; case 0xB: return "意大利队";
                case 0xD: return "巴西队"; case 0xE: return "阿根廷队"; case 0xF: return "墨西哥队";
                case 0x5: case 0x6: return "非洲联赛的一支球队";
                default: return "亚洲联赛的一支球队";
            }
        }
        bool showReq;
        int showTurn = -1, showTeam, showTimer, showCycle, showSinceHeld, showSinceKick;
        bool showGiving, showKick, showSpecial, showCaptured, resumeSpectate;
        byte showTeam0, showTeam1; readonly byte[] showPads = new byte[4];
        const int ShowSpot = 150, ShowWait = 150;
        static int ShowTurns { get { return ShowList.Length; } }

        public Tweaks(Nes nes)
        {
            this.nes = nes;
            System.Array.Copy(nes.Prg, TableAt, origTable, 0, TableLen);
            System.Array.Copy(nes.Prg, HeightAt, origHeight, 0, HeightLen);
            nes.RamWriteHook += OnWrite;
        }

        /// <summary>The one-key special (acts on the next frame).</summary>
        public void Press() { pressed = true; }

        /// <summary>The showcase (Y key): starts it, or stops it if it is on.</summary>
        public void Showcase() { showReq = true; }

        public void ToggleSpectate() { Spectate = !Spectate; Say(Spectate ? "观战：两队都交给电脑" : "观战关闭：你来踢"); }
        public void ToggleAllSpecials() { AllSpecials = !AllSpecials; Say(AllSpecials ? "必杀全开：每一脚射门都是必杀" : "必杀全开：关"); }

        void Say(string s) { Message = s; MessageFrame = nes.FrameCount; }

        void OnWrite(int addr, byte v)
        {
            // $050F: the shot routine writes 00, then the special's number (bit 7 clear) if it is one, later ORs in $80
            if (addr != 0x50F || (v & 0x80) != 0) return;
            if (v == 0) { if (pending > 0) shotStarted = true; if (showGiving) showKick = true; return; }
            Specials++; LastSpecialFrame = nes.FrameCount;
            if (v <= 0x21 && (v < 0x10 || v > 0x15)) { LastSpecial = v; LastShooter = nes.Ram[0x4D6]; SpecialSerial++; }
            if (showGiving) showSpecial = true;
            if (armed > 0) { armed = 0; pending = 0; }
        }

        static int S16(byte lo, byte hi) { return (short)(lo | (hi << 8)); }

        void Show(byte[] r, bool play, ref byte pad1, ref byte pad2)
        {
            int k = showTeam + 2 * (showTurn % 5), other = showTeam ^ 1;
            if (showGiving)
            {
                byte c = ShowList[showTurn];
                if (c < 0x10) r[0x532 + k] = c;                    // a home-team member's special: the shooter is that member for his turn
                else if (!showSpecial)                                // a foreign team's: his team is that team until the special is picked
                {
                    if (showTeamWas < 0) showTeamWas = r[0x52C + showTeam];
                    r[0x52C + showTeam] = (byte)((showTeamWas & 0xF0) | (c & 0x0F));
                }
            }
            if (showTeamWas >= 0 && (showSpecial || !showGiving)) { r[0x52C + showTeam] = (byte)((r[0x52C + showTeam] & 0x80) | (showTeamWas & 0x7F)); showTeamWas = -1; }
            if (!showGiving)
            {
                // the shot is played out by the computer, both teams (a kick-off after a goal too); then the next one
                r[0x52C] |= 0x80; r[0x52D] |= 0x80;
                for (int i = 0; i < 12; i++) r[0x650 + i] |= 0x80;
                for (int i = 0; i < 4; i++) r[0x568 + i] = 0xFF;
                pad1 &= (byte)Pad.Start; pad2 &= (byte)Pad.Start;
                if (++showTimer > ShowWait && play)
                {
                    showTurn++; showTimer = 0; showGiving = true;
                    if (showTurn >= ShowTurns) { EndShow(r); Say("必杀展示：结束"); }
                }
                return;
            }
            if (!play) { pad1 &= (byte)Pad.Start; pad2 = 0; return; }
            if (showTimer == 0)
            {
                showKick = showSpecial = false; showSinceHeld = showSinceKick = 999; showCycle = 0;
                Say("必杀展示 " + (showTurn + 1) + " / " + ShowTurns + "：" + ShowName(ShowList[showTurn]));
            }
            // he is on the pad, his team-mates are the computer's; the other team stands still (waiting for a pad)
            r[0x52C + showTeam] = (byte)(r[0x52C + showTeam] & 0x7F); r[0x52C + other] = (byte)(r[0x52C + other] & 0x7F);
            for (int i = 0; i < 12; i++)
                r[0x650 + i] = (byte)((i & 1) == showTeam && i != k ? r[0x650 + i] | 0x80 : r[0x650 + i] & 0x7F);
            r[0x568] = (byte)k; r[0x569] = r[0x56A] = r[0x56B] = 0xFF;
            bool holding = Holding(r, k);
            if (holding) showSinceHeld = 0; else showSinceHeld++;
            if (showKick) { showKick = false; showSinceKick = 0; } else showSinceKick++;
            int kx = S16(r[0x314 + 0x0A + other], r[0x327 + 0x0A + other]);       // their keeper: the goal to shoot at
            int gx = kx > 512 ? 1 : -1;
            if (!holding && showSinceHeld > 6 && showSinceKick > 45)
            {
                // on the spot, the ball at his feet
                int px = 512 + gx * ShowSpot, py = 192 + (showTurn % 3 - 1) * 24, bx = px + 8 * gx;
                Put(r, k, px, py); Put(r, 0x0C, bx, py);
                r[0x459 + k] = 0x81; r[0x479 + k] = 0xFF; r[0x486 + k] = 0x40;
                r[0x459 + 0x0C] = 0x83; r[0x486 + 0x0C] = 0x40; r[0x4BD + 0x0C] = 0;
                for (int i = 0; i < 12; i++) if (i != k) r[0x486 + i] &= 0xBF;       // nobody else holds it (a keeper hugging it lets go)
                r[0x4D6] = (byte)k; r[0x52A + showTeam] = 0x80; r[0x52A + other] = 0;
            }
            pad2 = 0;
            byte towards = gx > 0 ? (byte)Pad.Right : (byte)Pad.Left;
            // holding it: a moment facing their goal, then B again and again with the stick let go (direction and B together
            // can leave him dribbling on; a direction let go and pressed again is a double tap - a dash, and no shot)
            if (holding) { int c = showCycle++; pad1 = c < 6 ? towards : ((c - 6) % 8) < 2 ? (byte)0 : (byte)Pad.B; }
            else { pad1 = towards; showCycle = 0; }        // (not holding it: walk onto it)
            if (Trace != null) Trace(string.Format("{0} k{1:X2} hold{2} own{3:X2} fl{4:X2}/{5:X2} st{6:X2} mov{7:X2} ball st{8:X2} mov{9:X2} pad{10:X2} teams{11:X2}/{12:X2} bits{13:X2},{14:X2} screen{15:X2}",
                showTimer, k, holding ? 1 : 0, r[0x4D6], r[0x52A], r[0x52B], r[0x486 + k], r[0x459 + k], r[0x486 + 12], r[0x459 + 12], pad1, r[0x52C], r[0x52D], r[0x650 + k], r[0x650 + (k ^ 1)], r[0x59]));
            showTimer++;
            if (showSpecial || showTimer > 240) { showGiving = false; showTimer = 0; }     // (no special in 4 seconds: on to the next)
        }

        static void Put(byte[] r, int i, int x, int y)
        {
            r[0x314 + i] = (byte)x; r[0x327 + i] = (byte)(x >> 8); r[0x34D + i] = (byte)y; r[0x360 + i] = (byte)(y >> 8);
            r[0x386 + i] = 0; r[0x399 + i] = 0;
            r[0x3E8 + i] = r[0x3F6 + i] = r[0x404 + i] = r[0x412 + i] = r[0x420 + i] = r[0x42E + i] = 0;
        }

        void EndShow(byte[] r)
        {
            if (showCaptured) for (int i = 0; i < 12; i++) r[0x532 + i] = showMembers[i];
            if (showTeamWas >= 0) { r[0x52C + showTeam] = (byte)((r[0x52C + showTeam] & 0x80) | (showTeamWas & 0x7F)); showTeamWas = -1; }
            showTurn = -1; showGiving = false;
            if (resumeSpectate) { Spectate = true; resumeSpectate = false; }
            if (!showCaptured) return;
            showCaptured = false;
            // give the teams back as they were (see the note on spectating below: only the players on a pad have bit 7 clear)
            r[0x52C] = (byte)((r[0x52C] & 0x7F) | (showTeam0 & 0x80));
            r[0x52D] = (byte)((r[0x52D] & 0x7F) | (showTeam1 & 0x80));
            for (int i = 0; i < 4; i++) r[0x568 + i] = showPads[i];
            for (int i = 0; i < 12; i++)
            {
                bool onPad = false;
                for (int q = 0; q < 4; q++) if (showPads[q] == i) onPad = true;
                r[0x650 + i] = (byte)(onPad ? r[0x650 + i] & 0x7F : r[0x650 + i] | 0x80);
            }
        }

        /// <summary>Player <paramref name="me"/>'s team has the ball and he touched it last.</summary>
        static bool Holding(byte[] r, int me) { return (r[0x52A + (me & 1)] & 0x80) != 0 && r[0x4D6] == me; }


        /// <summary>Call before every emulated frame with the pads as the player holds them; returns what the game should see.</summary>
        public void BeforeFrame(ref byte pad1, ref byte pad2)
        {
            var r = nes.Ram;
            bool match = r[0x58] == 0;                      // the match screen (normal play, kick-off, goal, ball out ...)
            bool play = match && r[0x59] == 2;              // the ball is in play

            // ---- the showcase
            if (showReq)
            {
                showReq = false;
                if (Showing) { EndShow(r); Say("必杀展示：停止"); }
                else if (!play) Say("比赛进行中才能开始必杀展示");
                // spectating: hand the teams back first (the pads are not read for a player otherwise), spectate again afterwards
                else if (Spectate) { Spectate = false; resumeSpectate = true; showReq = true; }
                else
                {
                    showTeam = r[0x568] < 12 ? r[0x568] & 1 : 0;
                    showTeam0 = r[0x52C]; showTeam1 = r[0x52D];
                    for (int i = 0; i < 4; i++) showPads[i] = r[0x568 + i];
                    for (int i = 0; i < 12; i++) showMembers[i] = r[0x532 + i];
                    showCaptured = true; showTurn = 0; showGiving = true; showTimer = 0;
                    Say("必杀展示：全部 24 种必杀");
                }
            }
            if (Showing)
            {
                if (!match) EndShow(r);
                else Show(r, play, ref pad1, ref pad2);
            }
            // ---- computer v computer
            else if (Spectate && match)
            {
                if (!captured)
                {
                    teamBits0 = (byte)(r[0x52C] & 0x80); teamBits1 = (byte)(r[0x52D] & 0x80);
                    for (int i = 0; i < 4; i++) pads[i] = r[0x568 + i];
                    captured = true;
                }
                r[0x52C] |= 0x80; r[0x52D] |= 0x80;
                for (int i = 0; i < 12; i++) r[0x650 + i] |= 0x80;
                for (int i = 0; i < 4; i++) r[0x568 + i] = 0xFF;
                pad1 &= (byte)Pad.Start; pad2 &= (byte)Pad.Start;   // nobody to steer; Start still pauses
            }
            else if (captured)
            {
                // spectating switched off, or the match screen has gone: give the teams back as they were.
                // In play only the players on a pad have bit 7 clear - their AI team-mates have it set like any computer
                // player (clearing it for a whole human team leaves those team-mates waiting for a pad: a throw-in never comes).
                r[0x52C] = (byte)((r[0x52C] & 0x7F) | teamBits0);
                r[0x52D] = (byte)((r[0x52D] & 0x7F) | teamBits1);
                for (int i = 0; i < 4; i++) r[0x568 + i] = pads[i];
                for (int i = 0; i < 12; i++)
                {
                    bool onPad = false;
                    for (int k = 0; k < 4; k++) if (pads[k] == i) onPad = true;
                    r[0x650 + i] = (byte)(onPad ? r[0x650 + i] & 0x7F : r[0x650 + i] | 0x80);
                }
                captured = false;
            }

            // ---- one key, one special
            if (pressed)
            {
                pressed = false;
                if (!play) Say("比赛进行中才能放必杀");
                else if (Spectate) { armed = 60 * 20; armedTeam = -1; Say("下一脚射门：必杀！"); }
                else if (r[0x568] >= 12) Say("没有可以操作的球员");
                else { armed = 120; armedTeam = r[0x568] & 1; pending = 75; cycle = 0; shotStarted = false; pressFrame = nes.FrameCount; }
            }
            if (pending > 0)
            {
                int me = r[0x568];
                if (!play || me >= 12 || shotStarted) pending = 0;
                else
                {
                    pending--;
                    if (Holding(r, me))
                    {
                        // press B for him, towards the goal his own keeper is not guarding
                        int keeper = 0x0A + (me & 1);
                        int kx = (short)(r[0x314 + keeper] | (r[0x327 + keeper] << 8));
                        byte dir = kx < 512 ? (byte)Pad.Right : (byte)Pad.Left;
                        if (cycle == 0) Say("必杀射门！");
                        pad1 = (cycle % 8) < 2 ? (byte)0 : (byte)(dir | (byte)Pad.B);
                        cycle++;
                    }
                    if (pending == 0 && LastSpecialFrame < pressFrame) Say("要等自己的球员拿到球再按");
                }
            }
            if (armed > 0 && armedTeam >= 0 && (r[0x52A + (armedTeam ^ 1)] & 0x80) != 0) { armed = 0; pending = 0; }    // they have the ball now
            if (armed > 0 && (!match || --armed == 0)) armed = 0;

            bool want = AllSpecials || armed > 0 || Showing;
            if (want != patched)
            {
                for (int i = 0; i < TableLen; i++) nes.Prg[TableAt + i] = want ? (byte)0xFF : origTable[i];
                for (int i = 0; i < HeightLen; i++) nes.Prg[HeightAt + i] = want ? (byte)0x00 : origHeight[i];
                patched = want;
            }
        }
    }
}
