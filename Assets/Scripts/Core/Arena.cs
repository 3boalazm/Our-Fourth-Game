using System;
using System.Collections.Generic;
using System.Text;

// The rules of the game, with no Unity code in it (so it can be tested on its own).
//
// The arena is a grid of cells. Every player owns some land (cells). Leaving your
// land draws a trail behind you; coming back home with a trail claims the trail
// AND everything it encloses (also land that belonged to someone else).
//   - crossing your own trail       -> you crash
//   - running into someone's trail  -> THEY crash (their trail is cut)
//   - being surrounded by a capture -> you crash
//   - the wall                      -> you crash
// Crashed players lose their trail and come back home after a short delay.
// When the clock runs out, whoever owns the most land wins.

public enum Cause { Wall, Self, Cut, Crash, Caught }

public sealed class Player
{
    public int id;            // seat 0..3, also the colour
    public string name = "";
    public bool active;       // takes part in the match
    public bool alive;
    public bool bot;
    public bool remote;       // moved by network messages, not by this device
    public int x, y;          // head cell
    public int px, py;        // previous head cell (for smooth drawing)
    public int dir;           // 0 up, 1 right, 2 down, 3 left
    public readonly Queue<int> turns = new Queue<int>();
    public float moveT;       // progress (0..1) towards the next cell
    public float stepTime;    // arena time of the last step (for drawing remote players)
    public readonly List<int> trail = new List<int>();
    public int owned;         // cells owned
    public float respawnAt;
    public float spawnedAt;
    public int deaths, kills, captures;
    public int seq;           // move counter (online)
    public bool freshBase;    // the last respawn built a new little base
    public BotBrain brain;
}

public struct ArenaEvent
{
    public enum Kind { Capture, Death, Respawn, Star }
    public Kind kind;
    public int player, other, count;
    public float x, y;
    public Cause cause;
}

public sealed class Arena
{
    public const int MaxPlayers = 4;
    public const float RespawnDelay = 2.5f;

    public static readonly int[] DX = { 0, 1, 0, -1 };
    public static readonly int[] DY = { 1, 0, -1, 0 };

    public readonly int W, H;
    public readonly byte[] owner;   // 0 = nobody, otherwise player id + 1
    public readonly byte[] trail;   // 0 = none,   otherwise player id + 1
    public readonly float[] flash;  // arena time when the cell was last taken (drawing only)
    public readonly Player[] players = new Player[MaxPlayers];
    public readonly List<ArenaEvent> events = new List<ArenaEvent>();
    public float time;
    public float speed = 7.5f;      // cells per second
    // Called after a player driven by THIS device moves one cell / comes back after a crash
    // (used to send the move to the other players online).
    public Action<Player> onStep;
    public Action<Player> onRespawn;
    readonly Random rng;
    readonly int starSeed;
    readonly HashSet<int> starsTaken = new HashSet<int>();
    readonly int[] mark;
    int markGen;
    readonly int[] queue;

    public Arena(int w, int h, int seed)
    {
        W = w;
        H = h;
        owner = new byte[w * h];
        trail = new byte[w * h];
        flash = new float[w * h];
        mark = new int[w * h];
        queue = new int[w * h];
        rng = new Random(seed);
        starSeed = seed;
        for (int i = 0; i < MaxPlayers; i++) players[i] = new Player { id = i };
        for (int i = 0; i < flash.Length; i++) flash[i] = -100f;
    }

    public int Idx(int x, int y) { return y * W + x; }
    public bool In(int x, int y) { return x >= 0 && y >= 0 && x < W && y < H; }
    public static int Opposite(int d) { return (d + 2) & 3; }
    public int Total { get { return W * H; } }

    public float Share(Player p) { return p.owned / (float)Total; }

    // ------------------------------------------------------------ set-up

    // Start positions: corners first (2 players use opposite corners).
    public void StartPosition(int order, int count, out int x, out int y)
    {
        int m = Math.Max(4, W / 5);
        int[,] corners = { { m, m }, { W - 1 - m, H - 1 - m }, { W - 1 - m, m }, { m, H - 1 - m } };
        x = corners[order % 4, 0];
        y = corners[order % 4, 1];
    }

    public void AddPlayer(int id, string name, bool bot, bool remote, int startX, int startY)
    {
        var p = players[id];
        p.name = name;
        p.active = true;
        p.bot = bot;
        p.remote = remote;
        p.alive = true;
        p.trail.Clear();
        p.turns.Clear();
        p.owned = 0;
        p.deaths = p.kills = p.captures = 0;
        p.seq = 0;
        p.moveT = 0f;
        ClaimBase(p, startX, startY, 2);
        p.x = p.px = startX;
        p.y = p.py = startY;
        p.dir = startY < H / 2 ? 0 : 2;
        p.spawnedAt = time;
        p.brain = bot ? new BotBrain(rng.Next()) : null;
    }

    // A square of land around (cx, cy).
    public void ClaimBase(Player p, int cx, int cy, int r)
    {
        byte me = (byte)(p.id + 1);
        for (int y = cy - r; y <= cy + r; y++)
            for (int x = cx - r; x <= cx + r; x++)
                if (In(x, y)) Take(Idx(x, y), me, false);
    }

    void Take(int c, byte me, bool animate)
    {
        byte old = owner[c];
        if (old == me) return;
        if (old != 0) players[old - 1].owned--;
        owner[c] = me;
        players[me - 1].owned++;
        if (animate) flash[c] = time;
    }

    // ------------------------------------------------------------ moving

    // Queue a turn; it happens at the next cell. U-turns are ignored.
    public void Turn(Player p, int d)
    {
        int last = p.turns.Count > 0 ? LastQueued(p) : p.dir;
        if (d == last || d == Opposite(last)) return;
        if (p.turns.Count < 2) p.turns.Enqueue(d);
    }

    static int LastQueued(Player p)
    {
        int last = -1;
        foreach (int d in p.turns) last = d;
        return last;
    }

    // Advance a locally-driven player by dt. Returns the number of cells it entered.
    public int Advance(Player p, float dt)
    {
        if (!p.alive) return 0;
        p.moveT += dt * speed;
        int steps = 0;
        while (p.moveT >= 1f && p.alive && steps < 3)
        {
            p.moveT -= 1f;
            Step(p);
            steps++;
        }
        return steps;
    }

    // One cell forward. Returns false if the player crashed.
    public bool Step(Player p)
    {
        if (!p.alive) return false;
        if (p.brain != null) p.brain.Think(this, p);
        if (p.turns.Count > 0) p.dir = p.turns.Dequeue();
        int nx = p.x + DX[p.dir];
        int ny = p.y + DY[p.dir];
        bool ok;
        if (!In(nx, ny))
        {
            Kill(p, -1, Cause.Wall);
            ok = false;
        }
        else ok = Enter(p, nx, ny);
        p.seq++;
        if (onStep != null) onStep(p);
        return ok;
    }

    // The player's head enters cell (nx, ny). Also used for moves received online.
    public bool Enter(Player p, int nx, int ny)
    {
        if (!In(nx, ny)) return false;
        p.px = p.x;
        p.py = p.y;
        p.stepTime = time;
        int c = Idx(nx, ny);
        byte me = (byte)(p.id + 1);

        if (trail[c] == me)
        {
            p.x = nx;
            p.y = ny;
            Kill(p, p.id, Cause.Self);
            return false;
        }
        if (trail[c] != 0)
        {
            var q = players[trail[c] - 1];
            if (q.alive) Kill(q, p.id, Cause.Cut);
        }
        // head to head
        for (int i = 0; i < MaxPlayers; i++)
        {
            var q = players[i];
            if (q == p || !q.active || !q.alive || q.x != nx || q.y != ny) continue;
            bool pHome = owner[c] == me;
            bool qHome = owner[c] == q.id + 1;
            if (pHome && !qHome) Kill(q, p.id, Cause.Crash);
            else if (qHome && !pHome)
            {
                p.x = nx;
                p.y = ny;
                Kill(p, q.id, Cause.Crash);
                return false;
            }
            else
            {
                Kill(q, p.id, Cause.Crash);
                p.x = nx;
                p.y = ny;
                Kill(p, q.id, Cause.Crash);
                return false;
            }
        }

        p.x = nx;
        p.y = ny;
        if (owner[c] == me)
        {
            if (p.trail.Count > 0) Capture(p);
        }
        else
        {
            trail[c] = me;
            p.trail.Add(c);
        }
        int star = StarAt(c);
        if (star >= 0 && p.alive) TakeStar(p, star, nx, ny);
        return true;
    }

    // ------------------------------------------------------------ stars
    // Stars come at fixed times and places worked out from the match seed, so every
    // device in an online match sees the same ones. Grabbing one gives a little round
    // patch of land around it (cells with a tail on them are left alone).

    public const float StarFirst = 4f, StarEvery = 5f, StarLife = 16f;
    public const float StarRadius = 2.6f;

    public int StarCell(int k)
    {
        uint h = (uint)(starSeed * 73856093) ^ (uint)((k + 1) * 19349663);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        int m = 4;
        int x = m + (int)(h % (uint)Math.Max(1, W - 2 * m));
        int y = m + (int)((h / 977u) % (uint)Math.Max(1, H - 2 * m));
        return Idx(x, y);
    }

    public static float StarBorn(int k) { return StarFirst + k * StarEvery; }

    /// <summary>Stars that are out right now (index k), oldest first.</summary>
    public void LiveStars(List<int> into)
    {
        into.Clear();
        if (time < StarFirst) return;
        int last = (int)Math.Floor((time - StarFirst) / StarEvery);
        int first = Math.Max(0, (int)Math.Ceiling((time - StarFirst - StarLife) / StarEvery));
        for (int k = first; k <= last; k++)
            if (!starsTaken.Contains(k) && time < StarBorn(k) + StarLife) into.Add(k);
    }

    readonly List<int> starScratch = new List<int>();

    public int StarAt(int c)
    {
        LiveStars(starScratch);
        foreach (int k in starScratch) if (StarCell(k) == c) return k;
        return -1;
    }

    void TakeStar(Player p, int k, int cx, int cy)
    {
        starsTaken.Add(k);
        byte me = (byte)(p.id + 1);
        int gained = 0;
        int r = (int)Math.Ceiling(StarRadius);
        for (int y = cy - r; y <= cy + r; y++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (!In(x, y)) continue;
                float dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy > StarRadius * StarRadius) continue;
                int c = Idx(x, y);
                if (trail[c] != 0 || owner[c] == me) continue;
                Take(c, me, true);
                gained++;
            }
        events.Add(new ArenaEvent { kind = ArenaEvent.Kind.Star, player = p.id, count = gained, x = cx + 0.5f, y = cy + 0.5f });
    }

    // ------------------------------------------------------------ capture

    void Capture(Player p)
    {
        byte me = (byte)(p.id + 1);
        int gained = 0;
        long sx = 0, sy = 0;
        foreach (int c in p.trail)
        {
            trail[c] = 0;
            if (owner[c] != me)
            {
                Take(c, me, true);
                gained++;
                sx += c % W;
                sy += c / W;
            }
        }
        p.trail.Clear();

        // Flood in from the border over everything that is not ours; what the
        // flood cannot reach is enclosed by our land and becomes ours.
        markGen++;
        int head = 0, tail = 0;
        for (int x = 0; x < W; x++)
        {
            Seed(x, 0, me, ref tail);
            Seed(x, H - 1, me, ref tail);
        }
        for (int y = 0; y < H; y++)
        {
            Seed(0, y, me, ref tail);
            Seed(W - 1, y, me, ref tail);
        }
        while (head < tail)
        {
            int c = queue[head++];
            int cx = c % W, cy = c / W;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + DX[d], ny = cy + DY[d];
                if (In(nx, ny)) Seed(nx, ny, me, ref tail);
            }
        }

        bool[] caught = new bool[MaxPlayers];
        for (int c = 0; c < owner.Length; c++)
        {
            if (owner[c] == me || mark[c] == markGen) continue;
            if (trail[c] != 0)
            {
                caught[trail[c] - 1] = true; // their trail got swallowed
                trail[c] = 0;
            }
            for (int i = 0; i < MaxPlayers; i++)
            {
                var q = players[i];
                if (q != p && q.active && q.alive && Idx(q.x, q.y) == c) caught[i] = true;
            }
            Take(c, me, true);
            gained++;
            sx += c % W;
            sy += c / W;
        }
        for (int i = 0; i < MaxPlayers; i++)
            if (caught[i] && players[i].alive && i != p.id) Kill(players[i], p.id, Cause.Caught);

        if (gained > 0)
        {
            p.captures++;
            events.Add(new ArenaEvent
            {
                kind = ArenaEvent.Kind.Capture, player = p.id, count = gained,
                x = sx / (float)gained + 0.5f, y = sy / (float)gained + 0.5f
            });
        }
    }

    void Seed(int x, int y, byte me, ref int tail)
    {
        int c = Idx(x, y);
        if (mark[c] == markGen || owner[c] == me) return;
        mark[c] = markGen;
        queue[tail++] = c;
    }

    // ------------------------------------------------------------ crashing and coming back

    public void Kill(Player q, int killer, Cause cause)
    {
        if (!q.alive) return;
        q.alive = false;
        foreach (int c in q.trail) if (trail[c] == q.id + 1) trail[c] = 0;
        q.trail.Clear();
        q.turns.Clear();
        q.respawnAt = time + RespawnDelay;
        q.deaths++;
        if (killer >= 0 && killer != q.id) players[killer].kills++;
        events.Add(new ArenaEvent
        {
            kind = ArenaEvent.Kind.Death, player = q.id, other = killer, cause = cause,
            x = q.x + 0.5f, y = q.y + 0.5f
        });
    }

    // Where a crashed player comes back: deep inside its own land, away from the
    // others. With no land left, a fresh little base somewhere free.
    public void ChooseRespawn(Player q, out int x, out int y, out bool newBase)
    {
        byte me = (byte)(q.id + 1);
        int best = -1;
        float bestScore = float.MinValue;
        for (int c = 0; c < owner.Length; c++)
        {
            if (owner[c] != me || trail[c] != 0) continue;
            int cx = c % W, cy = c / W;
            int inner = 0;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + DX[d] * 2, ny = cy + DY[d] * 2;
                if (In(nx, ny) && owner[Idx(nx, ny)] == me) inner++;
            }
            float score = inner * 10f + NearestEnemy(q, cx, cy) * 0.5f;
            if (score > bestScore)
            {
                bestScore = score;
                best = c;
            }
        }
        if (best >= 0)
        {
            x = best % W;
            y = best / W;
            newBase = false;
            return;
        }
        newBase = true;
        for (int attempt = 0; attempt < 400; attempt++)
        {
            int cx = 3 + rng.Next(W - 6), cy = 3 + rng.Next(H - 6);
            bool free = true;
            for (int yy = cy - 2; yy <= cy + 2 && free; yy++)
                for (int xx = cx - 2; xx <= cx + 2 && free; xx++)
                    if (owner[Idx(xx, yy)] != 0 || trail[Idx(xx, yy)] != 0) free = false;
            if (free)
            {
                x = cx;
                y = cy;
                return;
            }
        }
        x = W / 2;
        y = H / 2;
    }

    float NearestEnemy(Player q, int x, int y)
    {
        float best = W + H;
        for (int i = 0; i < MaxPlayers; i++)
        {
            var o = players[i];
            if (o == q || !o.active || !o.alive) continue;
            best = Math.Min(best, Math.Abs(o.x - x) + Math.Abs(o.y - y));
        }
        return Math.Min(best, 20f);
    }

    public void Respawn(Player q, int x, int y, bool newBase, int dir)
    {
        q.freshBase = newBase;
        if (newBase) ClaimBase(q, x, y, 1);
        q.x = q.px = x;
        q.y = q.py = y;
        q.dir = dir & 3;
        q.turns.Clear();
        q.trail.Clear();
        q.moveT = 0f;
        q.alive = true;
        q.spawnedAt = time;
        events.Add(new ArenaEvent { kind = ArenaEvent.Kind.Respawn, player = q.id, x = x + 0.5f, y = y + 0.5f });
    }

    // A direction from (x, y) that does not run straight into the wall.
    public int SafeDirection(int x, int y)
    {
        int best = 0, bestRoom = -1;
        for (int d = 0; d < 4; d++)
        {
            int room = 0, cx = x, cy = y;
            while (In(cx + DX[d], cy + DY[d]) && room < 30)
            {
                cx += DX[d];
                cy += DY[d];
                room++;
            }
            if (room > bestRoom)
            {
                bestRoom = room;
                best = d;
            }
        }
        return best;
    }

    // ------------------------------------------------------------ one frame for local players

    // Moves every player this device drives (the local human and bots), brings
    // crashed ones back, and advances the clock. Remote players are not touched.
    public void Tick(float dt, Func<Player, bool> drivesHere)
    {
        time += dt;
        for (int i = 0; i < MaxPlayers; i++)
        {
            var p = players[i];
            if (!p.active || !drivesHere(p)) continue;
            if (p.alive) Advance(p, dt);
            else if (time >= p.respawnAt)
            {
                int x, y;
                bool fresh;
                ChooseRespawn(p, out x, out y, out fresh);
                Respawn(p, x, y, fresh, SafeDirection(x, y));
                if (onRespawn != null) onRespawn(p);
            }
        }
    }

    public int Leader()
    {
        int best = -1, most = -1;
        for (int i = 0; i < MaxPlayers; i++)
            if (players[i].active && players[i].owned > most)
            {
                most = players[i].owned;
                best = i;
            }
        return best;
    }

    // ------------------------------------------------------------ sharing the land online

    // Run-length encoded land map, as base64 (a few hundred characters).
    public string EncodeLand()
    {
        var bytes = new List<byte>(512);
        int i = 0;
        while (i < owner.Length)
        {
            byte v = owner[i];
            int run = 1;
            while (i + run < owner.Length && owner[i + run] == v && run < 255) run++;
            bytes.Add(v);
            bytes.Add((byte)run);
            i += run;
        }
        return Convert.ToBase64String(bytes.ToArray());
    }

    // Replaces the land map with an encoded one. Returns false if it does not fit.
    public bool DecodeLand(string data, bool animate)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(data); }
        catch (FormatException) { return false; }
        var next = new byte[owner.Length];
        int i = 0;
        for (int k = 0; k + 1 < bytes.Length; k += 2)
        {
            byte v = bytes[k];
            int run = bytes[k + 1];
            if (v > MaxPlayers || i + run > next.Length) return false;
            for (int r = 0; r < run; r++) next[i++] = v;
        }
        if (i != next.Length) return false;
        for (int c = 0; c < owner.Length; c++)
        {
            if (owner[c] == next[c]) continue;
            if (next[c] == 0)
            {
                if (owner[c] != 0) players[owner[c] - 1].owned--;
                owner[c] = 0;
            }
            else Take(c, next[c], animate);
        }
        return true;
    }

    // Consistency check used by the self test.
    public string Validate()
    {
        var counts = new int[MaxPlayers];
        for (int c = 0; c < owner.Length; c++) if (owner[c] != 0) counts[owner[c] - 1]++;
        var sb = new StringBuilder();
        for (int i = 0; i < MaxPlayers; i++)
        {
            if (counts[i] != players[i].owned) sb.Append("owned count of " + i + " is " + players[i].owned + " but " + counts[i] + " cells; ");
            foreach (int c in players[i].trail)
                if (trail[c] != i + 1) sb.Append("trail of " + i + " lost a cell; ");
            if (!players[i].alive && players[i].trail.Count > 0) sb.Append("dead player " + i + " still has a trail; ");
        }
        for (int c = 0; c < trail.Length; c++)
        {
            if (trail[c] == 0) continue;
            var q = players[trail[c] - 1];
            if (!q.trail.Contains(c)) sb.Append("orphan trail cell " + c + "; ");
            if (owner[c] == trail[c]) sb.Append("trail on own land at " + c + "; ");
        }
        return sb.ToString();
    }
}

// A simple opponent: leaves home, draws a rectangle and comes back to close it.
// It runs home early when someone gets close to its trail, and goes for other
// players' trails when they are near.
public sealed class BotBrain
{
    readonly Random rng;
    public float boldness;     // how far it dares to go
    int legLeft;               // cells left on the current leg
    int legIndex;              // 0 out, 1 across, 2 back, 3 home
    int turnSide;              // +1 right, -1 left
    int leg0, leg1;

    public BotBrain(int seed)
    {
        rng = new Random(seed);
        boldness = 0.6f + (float)rng.NextDouble() * 0.6f;
    }

    public void Think(Arena a, Player p)
    {
        if (p.turns.Count > 0) return;
        byte me = (byte)(p.id + 1);
        bool home = a.owner[a.Idx(p.x, p.y)] == me;

        // 1. go for a trail that is close
        int target = NearestEnemyTrail(a, p, 5);
        if (target >= 0)
        {
            int d = DirTowards(a, p, target % a.W, target / a.W);
            if (d >= 0) { Choose(a, p, d); return; }
        }

        // 2. danger: an enemy head near our trail -> go home the short way
        if (!home && p.trail.Count > 0 && Danger(a, p) )
        {
            int d = HomeDirection(a, p);
            if (d >= 0) { Choose(a, p, d); return; }
        }

        // 3. plan a rectangle
        if (home)
        {
            legIndex = 0;
            leg0 = 3 + (int)(rng.Next(3, 8) * boldness);
            leg1 = 3 + (int)(rng.Next(3, 9) * boldness);
            legLeft = -1;
            turnSide = rng.Next(2) == 0 ? 1 : -1;
            // leave in a direction that has room
            if (!Safe(a, p, p.dir, 3))
            {
                for (int k = 1; k < 4; k++)
                {
                    int d = (p.dir + k) & 3;
                    if (d != Arena.Opposite(p.dir) && Safe(a, p, d, 3)) { Choose(a, p, d); break; }
                }
            }
            else if (rng.Next(6) == 0)
            {
                int d = (p.dir + (rng.Next(2) == 0 ? 1 : 3)) & 3;
                if (Safe(a, p, d, 3)) Choose(a, p, d);
            }
            return;
        }

        if (legLeft < 0) legLeft = leg0;
        legLeft--;
        if (legLeft <= 0 && legIndex < 3)
        {
            legIndex++;
            legLeft = legIndex == 1 ? leg1 : legIndex == 2 ? leg0 + 1 : 999;
            int d = (p.dir + (turnSide > 0 ? 1 : 3)) & 3;
            if (legIndex == 3)
            {
                int hd = HomeDirection(a, p);
                if (hd >= 0) d = hd;
            }
            Choose(a, p, d);
            return;
        }
        if (legIndex >= 3)
        {
            int hd = HomeDirection(a, p);
            if (hd >= 0) { Choose(a, p, hd); return; }
        }
        if (!Safe(a, p, p.dir, 1))
        {
            int hd = HomeDirection(a, p);
            if (hd >= 0) Choose(a, p, hd);
            else
            {
                for (int k = 1; k < 4; k++)
                {
                    int d = (p.dir + k) & 3;
                    if (d != Arena.Opposite(p.dir) && Safe(a, p, d, 1)) { Choose(a, p, d); break; }
                }
            }
        }
    }

    void Choose(Arena a, Player p, int d)
    {
        if (d == p.dir || d == Arena.Opposite(p.dir)) return;
        p.turns.Enqueue(d);
    }

    // The next `cells` cells in direction d are inside the arena and not our own trail.
    static bool Safe(Arena a, Player p, int d, int cells)
    {
        int x = p.x, y = p.y;
        byte me = (byte)(p.id + 1);
        for (int i = 0; i < cells; i++)
        {
            x += Arena.DX[d];
            y += Arena.DY[d];
            if (!a.In(x, y) || a.trail[a.Idx(x, y)] == me) return false;
        }
        return true;
    }

    static bool Danger(Arena a, Player p)
    {
        for (int i = 0; i < Arena.MaxPlayers; i++)
        {
            var q = a.players[i];
            if (q == p || !q.active || !q.alive) continue;
            int near = Math.Abs(q.x - p.x) + Math.Abs(q.y - p.y);
            if (near < 6) return true;
            for (int k = Math.Max(0, p.trail.Count - 40); k < p.trail.Count; k++)
            {
                int c = p.trail[k];
                if (Math.Abs(q.x - c % a.W) + Math.Abs(q.y - c / a.W) < 4) return true;
            }
        }
        return false;
    }

    // First step of the shortest path home (breadth first, avoiding our trail).
    static int HomeDirection(Arena a, Player p)
    {
        byte me = (byte)(p.id + 1);
        return FirstStep(a, p, c => a.owner[c] == me, 400);
    }

    static int DirTowards(Arena a, Player p, int tx, int ty)
    {
        int goal = a.Idx(tx, ty);
        return FirstStep(a, p, c => c == goal, 60);
    }

    static int FirstStep(Arena a, Player p, Func<int, bool> isGoal, int limit)
    {
        byte me = (byte)(p.id + 1);
        var first = new Dictionary<int, int>();
        var q = new Queue<int>();
        for (int d = 0; d < 4; d++)
        {
            if (d == Arena.Opposite(p.dir)) continue;
            int nx = p.x + Arena.DX[d], ny = p.y + Arena.DY[d];
            if (!a.In(nx, ny)) continue;
            int c = a.Idx(nx, ny);
            if (a.trail[c] == me) continue;
            if (isGoal(c)) return d;
            first[c] = d;
            q.Enqueue(c);
        }
        int seen = 0;
        while (q.Count > 0 && seen++ < limit)
        {
            int c = q.Dequeue();
            int cx = c % a.W, cy = c / a.W;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + Arena.DX[d], ny = cy + Arena.DY[d];
                if (!a.In(nx, ny)) continue;
                int n = a.Idx(nx, ny);
                if (first.ContainsKey(n) || a.trail[n] == me || n == a.Idx(p.x, p.y)) continue;
                if (isGoal(n)) return first[c];
                first[n] = first[c];
                q.Enqueue(n);
            }
        }
        return -1;
    }

    static int NearestEnemyTrail(Arena a, Player p, int range)
    {
        int best = -1, bestDist = range + 1;
        for (int y = Math.Max(0, p.y - range); y <= Math.Min(a.H - 1, p.y + range); y++)
            for (int x = Math.Max(0, p.x - range); x <= Math.Min(a.W - 1, p.x + range); x++)
            {
                int t = a.trail[a.Idx(x, y)];
                if (t == 0 || t == p.id + 1) continue;
                int dist = Math.Abs(x - p.x) + Math.Abs(y - p.y);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = a.Idx(x, y);
                }
            }
        return best;
    }
}
