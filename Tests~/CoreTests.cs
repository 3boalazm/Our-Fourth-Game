// Plain C# checks of the game rules (Assets/Scripts/Core/Arena.cs).
// Folders ending in "~" are ignored by Unity. Build and run with:
//   tools/test-core.sh
using System;
using System.Collections.Generic;

public static class CoreTests
{
    static int failed;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
        if (!ok) failed++;
    }

    static Arena TwoPlayers()
    {
        var a = new Arena(30, 30, 1);
        a.AddPlayer(0, "A", false, false, 5, 5);
        a.AddPlayer(1, "B", false, false, 24, 24);
        return a;
    }

    // Walk a player along a list of directions, one cell each.
    static void Walk(Arena a, Player p, params int[] dirs)
    {
        foreach (int d in dirs)
        {
            p.turns.Clear();
            if (d != p.dir) p.turns.Enqueue(d);
            a.Step(p);
        }
    }

    static int[] Repeat(int d, int n)
    {
        var r = new int[n];
        for (int i = 0; i < n; i++) r[i] = d;
        return r;
    }

    static int[] Concat(params int[][] parts)
    {
        var l = new List<int>();
        foreach (var p in parts) l.AddRange(p);
        return l.ToArray();
    }

    static void Main()
    {
        Environment.Exit(Run());
    }

    // Returns the number of failed checks (also callable from PowerShell, see tools/test-core.ps1).
    public static int Run()
    {
        failed = 0;
        // 1. base
        {
            var a = TwoPlayers();
            Check(a.players[0].owned == 25 && a.players[1].owned == 25, "each player starts with a 5x5 base");
            Check(a.Validate() == "", "fresh arena is consistent");
        }

        // 2. a loop out of the base and back claims the inside
        {
            var a = TwoPlayers();
            var p = a.players[0];
            p.dir = 0; // up, starting at (5,5), base is x 3..7, y 3..7
            // up out of the base (x 5, y 8..12), right to x 13, down to y 7, left back into the base
            Walk(a, p, Concat(Repeat(0, 7), Repeat(1, 8), Repeat(2, 5), Repeat(3, 6)));
            Check(p.alive, "player survives a clean loop");
            Check(p.trail.Count == 0, "trail is turned into land when you get home");
            // enclosed area: x 5..11, y 8..12 roughly; owned must have grown well beyond the trail alone
            Check(p.owned > 25 + 20, "the inside of the loop is claimed too (owned " + p.owned + ")");
            Check(a.owner[a.Idx(8, 10)] == 1, "a cell inside the loop is now yours");
            Check(a.Validate() == "", "arena consistent after capture: " + a.Validate());
            bool sawCapture = a.events.Exists(e => e.kind == ArenaEvent.Kind.Capture && e.player == 0);
            Check(sawCapture, "a capture event is reported");
        }

        // 3. crossing your own trail is a crash
        {
            var a = TwoPlayers();
            var p = a.players[0];
            p.dir = 0;
            Walk(a, p, Concat(Repeat(0, 6), Repeat(1, 3), Repeat(2, 2), Repeat(3, 4)));
            Check(!p.alive, "crossing your own trail crashes you");
            Check(p.trail.Count == 0, "a crashed player's trail disappears");
            Check(a.Validate() == "", "consistent after self crash");
        }

        // 4. the wall is a crash
        {
            var a = TwoPlayers();
            var p = a.players[0];
            p.dir = 3;
            Walk(a, p, Repeat(3, 10));
            Check(!p.alive, "running into the wall crashes you");
        }

        // 5. running into someone's trail cuts them
        {
            var a = TwoPlayers();
            var b = a.players[1];
            b.dir = 3; // B leaves its base to the left: trail at y = 24, x = 21..15
            Walk(a, b, Repeat(3, 9));
            Check(b.trail.Count > 0, "B has a trail out in the open");
            var p = a.players[0];
            // teleport A next to B's trail and step onto it
            a.Respawn(p, 17, 22, false, 0);
            Walk(a, p, 0, 0);
            Check(!b.alive, "stepping on B's trail crashes B");
            Check(p.alive, "and A is fine");
            Check(a.players[0].kills == 1, "A gets the kill");
            Check(a.Validate() == "", "consistent after a cut");
        }

        // 6. a capture that surrounds a player catches them
        {
            var a = new Arena(30, 30, 2);
            a.AddPlayer(0, "A", false, false, 5, 5);
            a.AddPlayer(1, "B", false, false, 25, 25);
            var b = a.players[1];
            a.Respawn(b, 9, 11, false, 0); // B stands in the open, no trail
            var p = a.players[0];
            p.dir = 0;
            Walk(a, p, Concat(Repeat(0, 9), Repeat(1, 8), Repeat(2, 7), Repeat(3, 6)));
            Check(!b.alive, "a player inside a captured area is caught");
            Check(a.Validate() == "", "consistent after catching someone");
        }

        // 7. capturing takes land from someone else
        {
            var a = new Arena(30, 30, 3);
            a.AddPlayer(0, "A", false, false, 5, 5);
            a.AddPlayer(1, "B", false, false, 10, 15); // B base x 8..12, y 13..17
            var p = a.players[0];
            int bBefore = a.players[1].owned;
            p.dir = 0;
            // go up along x=5 to y=20, right to x=15, down to y=8, left back to x=7 (into own base at y 7)... simpler: big loop
            Walk(a, p, Concat(Repeat(0, 15), Repeat(1, 10), Repeat(2, 14), Repeat(3, 8)));
            Check(a.players[1].owned < bBefore, "B lost land to A's capture (B " + bBefore + " -> " + a.players[1].owned + ")");
            Check(a.Validate() == "", "consistent after stealing land");
        }

        // 8. U-turns are ignored, two turns can be queued
        {
            var a = TwoPlayers();
            var p = a.players[0];
            p.dir = 0;
            a.Turn(p, 2);
            Check(p.turns.Count == 0, "a U-turn is ignored");
            a.Turn(p, 1);
            a.Turn(p, 2);
            Check(p.turns.Count == 2, "two turns can be queued (right, then down)");
        }

        // 9. land encoding round trip
        {
            var a = TwoPlayers();
            var p = a.players[0];
            p.dir = 0;
            Walk(a, p, Concat(Repeat(0, 7), Repeat(1, 8), Repeat(2, 5), Repeat(3, 6)));
            string code = a.EncodeLand();
            var b = TwoPlayers();
            bool ok = b.DecodeLand(code, false);
            bool same = true;
            for (int c = 0; c < a.owner.Length; c++) if (a.owner[c] != b.owner[c]) same = false;
            Check(ok && same, "land map survives encode/decode (" + code.Length + " chars)");
            Check(b.players[0].owned == a.players[0].owned, "owned counts follow the decoded map");
            Check(!b.DecodeLand("not base64!!", false), "a broken map is rejected");
        }

        // 10. long bot matches: no crashes, rules stay consistent, things happen
        {
            int totalCaptures = 0, totalDeaths = 0;
            string problems = "";
            for (int seed = 1; seed <= 12; seed++)
            {
                int n = 2 + seed % 3;
                int size = n == 2 ? 44 : n == 3 ? 50 : 56;
                var a = new Arena(size, size, seed);
                for (int i = 0; i < n; i++)
                {
                    int x, y;
                    a.StartPosition(i, n, out x, out y);
                    a.AddPlayer(i, "Bot" + i, true, false, x, y);
                }
                float dt = 1f / 60f;
                for (int frame = 0; frame < 60 * 120; frame++)
                {
                    a.Tick(dt, pl => true);
                    if (frame % 600 == 0)
                    {
                        string v = a.Validate();
                        if (v != "" && problems == "") problems = "seed " + seed + " frame " + frame + ": " + v;
                    }
                }
                foreach (var e in a.events)
                {
                    if (e.kind == ArenaEvent.Kind.Capture) totalCaptures++;
                    if (e.kind == ArenaEvent.Kind.Death) totalDeaths++;
                }
                string end = a.Validate();
                if (end != "" && problems == "") problems = "seed " + seed + " end: " + end;
                int sum = 0;
                for (int i = 0; i < n; i++) sum += a.players[i].owned;
                if (sum > a.Total && problems == "") problems = "more land than the arena";
                if (seed <= 3)
                {
                    var parts = new List<string>();
                    for (int i = 0; i < n; i++)
                        parts.Add(a.players[i].name + " " + (100f * a.Share(a.players[i])).ToString("0") + "% (" +
                                  a.players[i].captures + " captures, " + a.players[i].deaths + " crashes)");
                    Console.WriteLine("     match " + seed + ": " + string.Join(", ", parts));
                }
            }
            Check(problems == "", "12 bot matches stay consistent " + problems);
            Check(totalCaptures > 100, "bots capture land (" + totalCaptures + " captures)");
            Check(totalDeaths > 5, "bots also crash sometimes (" + totalDeaths + ")");
        }

        // stars: same places for everyone, grabbing one gives a round patch of land
        {
            var a = TwoPlayers();
            var b = TwoPlayers();
            Check(a.StarCell(0) == b.StarCell(0) && a.StarCell(3) == b.StarCell(3), "stars are in the same places on every device");
            a.time = Arena.StarFirst + 0.5f;
            var live = new List<int>();
            a.LiveStars(live);
            Check(live.Count == 1 && live[0] == 0, "the first star is out after StarFirst seconds");
            int sc = a.StarCell(0);
            int sx = sc % a.W, sy = sc / a.W;
            var p = a.players[0];
            // put the player right next to the star (keep its trail out of the way) and step onto it
            p.x = sx - 1; p.y = sy; p.dir = 1; p.trail.Clear();
            int before = p.owned;
            a.events.Clear();
            a.Step(p);
            Check(p.alive && p.x == sx && p.y == sy, "the player reaches the star");
            Check(p.owned >= before + 15, "the star gives a patch of land (" + (p.owned - before) + " cells)");
            Check(a.events.Exists(e => e.kind == ArenaEvent.Kind.Star && e.player == 0), "a star event is reported");
            a.LiveStars(live);
            Check(!live.Contains(0), "a taken star is gone");
            Check(a.trail[sc] == 1 || a.owner[sc] == 1, "the star cell itself keeps the trail or is yours");
            Check(a.Validate() == "", "consistent after a star: " + a.Validate());
            a.time = Arena.StarBorn(1) + Arena.StarLife + 0.1f;
            a.LiveStars(live);
            Check(!live.Contains(1), "stars go away after StarLife seconds");
        }

        Console.WriteLine(failed == 0 ? "all core tests passed" : failed + " FAILED");
        return failed;
    }
}
