// "Standing on the ball": makes the pads-only movie Tools/movies/ride_on_ball.fm2 (versus, home team v Germany).
//
// The move (found 2026-11-21, in the game's own code at 06:AE0E): a player WITHOUT the ball who is in the air and coming down
// (A+B is the jump) and touches a ball lying on the ground lands on it - sound $43, the ball's movement $12 then $11, his
// state $50, his height 13.  Every player can (all 12 home-team members, the foreign teams, the keepers: tried with RAM set up
// by hand).  On the ball the stick walks it about (about 0.76 px a frame, a walk is 0.83), A or B alone do nothing, A+B jumps off.
// A ball at another player's feet cannot be jumped onto.  With the ball, A+B flicks it up instead.
//
// Here pad 1 does it for real: nobody is on pad 2, so Germany's pad player gets the ball and stands with it until the game
// takes it off him (match frame ~1130) and it lies free; pad 1's player has walked to 66 px beside it, runs up, jumps 52 px
// before it, lands on it, walks about on it and jumps off.   The pads are chosen by reading the RAM while playing; the
// movie written is pads only.
//
//   Tools/build_test.sh /tmp/ride.exe Tools/RideOnBall.cs
//   mono /tmp/ride.exe ../soccer.nes Tools/movies/ride_on_ball.fm2 [trace 0/1] [dribble-back frames] [jump at, px before the ball: 52] [opponent: 2 = Germany] [song 1-7, 0 = leave it to the game] [2 = the opponent's song is picked on pad 2]
//   Song (the pre-match locker room's list of cheer songs, 1-7): 1 = music id $055A 04, 2 = 06, 3 = 03,
//   4 = 05 (the one the game itself plays when left to it), 5 = 0D, 6 and 7 the last two of the list.
//   The other team's locker answers to pad 2 only.  Tools/movies/ride_on_ball_carnival.fm2 = 0 60 52 2 1 2 (the ride comes at match frame 2051;
//   with song 3 the ball never lies free for him and there is no ride).  Brazil (opponent 4) plays differently: no ride either.
using System; using System.IO; using System.Text; using System.Collections.Generic; using NesCore;
static class RideOnBall {
  const byte A = 1, B = 2, T = 8, U = 16, D = 32, L = 64, R = 128;
  static List<byte> p1 = new List<byte>(), p2 = new List<byte>();
  static void Wait(int n) { for (int i = 0; i < n; i++) { p1.Add(0); p2.Add(0); } }
  static void Tap(byte a, byte b) { for (int i = 0; i < 6; i++) { p1.Add(a); p2.Add(b); } Wait(14); }
  static int Song = 0, Opp = 2; static bool SongByPad2 = false;
  static void PickSong(bool pad2) {            // in a team's locker room: right to "ouen kyoku", A, down Song times from "makaseru" (wraps to song 1), A
    if (Song <= 0) return;
    byte z = 0;
    if (pad2) { Tap(0, R); Tap(0, A); Wait(30); for (int i = 0; i < Song; i++) Tap(0, D); Tap(0, A); }
    else { Tap(R, z); Tap(A, z); Wait(30); for (int i = 0; i < Song; i++) Tap(D, z); Tap(A, z); }
    Wait(40);
  }
  static void Menu(int team) {
    Wait(300); Tap(T, 0); Wait(120); Tap(T, 0); Wait(90);
    Tap(D, 0); Tap(A, 0); Tap(A, 0); Wait(40); Tap(A, 0); Wait(60);
    for (int i = 0; i < team; i++) Tap(D, 0);
    Tap(A, 0); for (int i = 0; i < Opp; i++) Tap(0, D); Tap(0, A); Wait(60);
    for (int i = 0; i < 3; i++) Tap(R, 0);
    Tap(D, 0); Tap(D, 0);
    Wait(10); Tap(A, 0); Wait(40); Tap(T, 0); Wait(90); PickSong(false); Tap(T, 0); Wait(90); PickSong(SongByPad2); Tap(T, 0); Wait(90); if (Song <= 0) { Tap(T, 0); Wait(60); } else Wait(150);
  }
  static string Line(byte a, byte b) { var sb = new StringBuilder("|0|"); foreach (byte q in new[] { a, b }) { for (int k = 0; k < 8; k++) sb.Append((q & (0x80 >> k)) != 0 ? "RLDUTSBA"[k] : '.'); sb.Append('|'); } sb.Append('|'); return sb.ToString(); }
  static int Main(string[] a) {
    bool trace = a.Length > 2 && a[2] == "1"; int back = a.Length > 3 ? int.Parse(a[3]) : 60; int was = -1; int jump = a.Length > 4 ? int.Parse(a[4]) : 52;
    Opp = a.Length > 5 ? int.Parse(a[5]) : 2; Song = a.Length > 6 ? int.Parse(a[6]) : 0; SongByPad2 = a.Length > 7 && a[7] == "2";
    Menu(0); int menu = p1.Count;
    var nes = new Nes(); nes.LoadRom(File.ReadAllBytes(a[0])); var r = nes.Ram;
    int phase = 0, t = 0, total = menu + 2400, rideFrames = 0, firstRide = -1; var plan = new Queue<byte>();
    for (int f = 1; f <= total; f++) {
      int m = f - menu; byte q1 = 0, q2 = 0;
      if (f <= menu) { q1 = p1[f - 1]; q2 = p2[f - 1]; }
      else {
        int me = r[0x568]; if (me > 11) me = 0;
        int x = r[0x314 + me] | r[0x327 + me] << 8, y = r[0x34D + me], z = r[0x386 + me];
        int bx = r[0x320] | r[0x333] << 8, by = r[0x359], bz = r[0x392], bm = r[0x465] & 0x7F;
        bool hold = (r[0x52A + (me & 1)] & 0x80) != 0 && r[0x4D6] == me, ride = bm == 0x11 || bm == 0x12;
        if (ride) { rideFrames++; if (firstRide < 0) firstRide = m; }
        t++;
        switch (phase) {
          case 0:
            if (bm == 0x07 && bz == 0 && ((r[0x52A] | r[0x52B]) & 0x80) == 0 && m > 600) {             // it lies there, nobody's
              int tx = bx - 66; q1 = x < tx - 6 ? R : x > tx + 6 ? L : (byte)0; if (y < by) q1 |= D; else if (y > by) q1 |= U;
              if (q1 == 0) { phase = 3; t = 0; }
            } else
if (hold && r[0x486 + me] == 0x40) { phase = 7; t = 0; }
            else if (m > 600 && r[0x4D6] == 1 && (r[0x52B] & 0x80) != 0 && (r[0x465] & 0x7F) == 0x03) {       // their pad player (nobody on pad 2) stands there with it
              int tx = bx - 70; q1 = x < tx - 2 ? R : x > tx + 2 ? L : (byte)0; if (y < by) q1 |= D; else if (y > by) q1 |= U;
              
            }
            else if ((r[0x52A + (me & 1)] & 0x80) != 0 && r[0x4D6] != me) q1 = (byte)(t % 24 < 4 ? A : 0);      // a team-mate has it: "pass it to me"
            else { q1 = bx > x + 4 ? R : bx < x - 4 ? L : (byte)0; if (by > y + 2 && by - y < 128) q1 |= D; else if (by != y) q1 |= U; }      // run for it
            break;
          case 7: q1 = (byte)(L | (y < 150 ? D : 0)); if (!hold) { phase = 0; t = 0; } else if (t > back) { phase = 8; t = 0; } break;     // dribble away from them
          case 8: q1 = 0; if (!hold) { phase = 0; t = 0; } else if (t > 12) { phase = 1; t = 0; } break;
          case 1: q1 = (byte)(t <= 4 ? A | B : 0); if (t > 6 && bm == 0x0A) { phase = 2; t = 0; } else if (t > 40) { phase = 0; t = 0; } break;       // flick it up
          case 2: q1 = bx - x < 62 ? L : (byte)0; if (t > 200) { phase = 0; t = 0; } if (bm == 0x07 && bz == 0) { phase = 3; t = 0; } if (hold) { phase = 1; t = 0; } break;            // step back, let it come to rest
          case 3: q1 = R; if (bx - x <= jump) { phase = 4; t = 0; } if (y != by) q1 |= by > y ? D : U; break;                     // run up
          case 4: q1 = (byte)(R | (t <= 4 ? A | B : 0)); if (ride) { phase = 5; t = 0; } else if (t > 80) { phase = 0; t = 0; } break;        // jump onto it
          case 5: q1 = t < 30 ? (byte)0 : t < 150 ? R : t < 210 ? D : t < 300 ? L : t < 360 ? U : t < 420 ? R : (byte)0; if (!ride) { phase = 6; t = 0; } else if (t >= 440 && t < 446) { q1 = A | B; } break;   // ride it about, then hop off
          default: break;
        }
        p1.Add(q1); p2.Add(q2);
        if (trace && (phase != was || m % 60 == 0)) was = phase; if (trace && (was == phase && (t == 0 || m % 60 == 0))) Console.WriteLine("{0,4} ph{1} me{2:X2} pad{3:X2} x{4} y{5} z{6} st{7:X2} mov{8:X2} | ball x{9} y{10} z{11} mov{12:X2} st{13:X2} | holder{14:X2} fl{15:X2}/{16:X2} teams{17:X2}/{18:X2} scr{19:X2}",
          m, phase, me, q1, x, y, z, r[0x486 + me], r[0x459 + me], bx, by, bz, r[0x465], r[0x492], r[0x4D6], r[0x52A], r[0x52B], r[0x52C], r[0x52D], r[0x59]);
      }
      nes.Pad1 = q1; nes.Pad2 = q2; nes.RunFrame(); nes.Apu.OutCount = 0;
      if (phase == 6 && t > 90) { total = f; break; }
    }
    Console.WriteLine("menu {0} frames; riding from match frame {1}, {2} frames; movie {3} frames", menu, firstRide, rideFrames, p1.Count);
    var o = new List<string>();
    o.Add("version 3"); o.Add("emuVersion 22020"); o.Add("port0 1"); o.Add("port1 1");
    o.Add("comment generated by Tools/RideOnBall.cs: home team v opponent " + Opp + ", song " + Song + " (versus, menu " + menu + " frames); pad 1 waits by the ball, jumps onto it when it lies free (match frame " + firstRide + ") and rides it about");
    for (int i = 0; i < p1.Count; i++) o.Add(Line(p1[i], p2[i]));
    File.WriteAllLines(a[1], o.ToArray());
    return 0;
  }
}
