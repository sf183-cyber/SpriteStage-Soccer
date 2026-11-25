// A recorded run of the game: the buttons of both pads for every video frame, as written by FCEUX (.fm2).
// Played back from power-on through the core it reproduces the recorded matches exactly, which makes it a
// convenient way to bring up special shots, weather and the like for testing.
using System.Collections.Generic;

namespace Soccer
{
    public sealed class Movie
    {
        public readonly List<byte> Pad1 = new List<byte>(), Pad2 = new List<byte>(), Command = new List<byte>();
        public int Length { get { return Pad1.Count; } }

        public static Movie Parse(string[] lines)
        {
            var m = new Movie();
            foreach (var l in lines)
            {
                if (l.Length == 0 || l[0] != '|') continue;
                var parts = l.Split('|');
                int cmd; int.TryParse(parts[1], out cmd);
                m.Command.Add((byte)cmd); m.Pad1.Add(Buttons(parts.Length > 2 ? parts[2] : "")); m.Pad2.Add(Buttons(parts.Length > 3 ? parts[3] : ""));
            }
            return m;
        }

        // FCEUX writes a pad as "RLDUTSBA"; the NES bit order is A B Select Start Up Down Left Right
        static byte Buttons(string s)
        {
            byte b = 0;
            for (int k = 0; k < 8 && k < s.Length; k++) if (s[k] != '.' && s[k] != ' ') b |= (byte)(0x80 >> k);
            return b;
        }

        /// <summary>Feeds frame number f (1 = the first after power-on) to the game.</summary>
        public void Step(GameHost host, int f)
        {
            int i = f - 1;
            if (i < 0 || i >= Length) { host.StepFrame(0); return; }
            if ((Command[i] & 1) != 0) host.Nes.Reset();
            host.StepFrame(Pad1[i], Pad2[i]);
        }
    }
}
