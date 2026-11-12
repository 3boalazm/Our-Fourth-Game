using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Draws an Arena: the land (one texture pixel per cell, turned into rounded blobs by
/// the OurFourth/Arena shader), the players (a round head with eyes, a snake-like body
/// and a glow), particles, floating "+%" texts and a soft bokeh background.
/// </summary>
public sealed class ArenaView
{
    public static readonly Color[] Colors =
    {
        new Color(0.30f, 0.82f, 0.45f), // green
        new Color(1.00f, 0.42f, 0.66f), // pink
        new Color(0.30f, 0.62f, 1.00f), // blue
        new Color(1.00f, 0.66f, 0.22f), // orange
    };
    public static readonly Color Night = new Color(0.086f, 0.10f, 0.20f);
    static readonly Color Pupil = new Color(0.10f, 0.11f, 0.22f);

    readonly Material arenaMat, glowMat, lineMat;
    readonly Sprite disc, shadedDisc, soft, crownSprite, frameSprite, panelSprite;
    public readonly Transform root;
    Arena arena;
    Texture2D cells;
    Color32[] px;
    MeshFilter groundMesh;
    MeshRenderer groundRenderer;
    SpriteRenderer frame, frameGlow, shadow;
    readonly PlayerView[] views = new PlayerView[Arena.MaxPlayers];
    ParticleSystem burst;
    readonly List<FloatText> floats = new List<FloatText>();
    readonly List<Bokeh> bokeh = new List<Bokeh>();

    public Texture CellTexture { get { return cells; } }
    public Material ArenaMaterial { get { return arenaMat; } }
    public Arena Arena { get { return arena; } }

    sealed class PlayerView
    {
        public Transform root;
        public SpriteRenderer head, glow, shine, eyeL, eyeR, pupilL, pupilR, crown;
        public LineRenderer body;
        public TextMeshPro label;
        public readonly List<Vector3> history = new List<Vector3>();
        public int lastX = int.MinValue, lastY = int.MinValue;
        public float blinkAt, blinkUntil;
        public Vector3 shown;
        public float scale;
    }

    sealed class FloatText { public TextMeshPro text; public float born; public Vector3 from; public bool used; }
    sealed class Bokeh { public Transform t; public Vector2 norm; public Vector3 home; public float phase, speed; }

    public ArenaView(Material arena, Material glow, Material line)
    {
        arenaMat = new Material(arena);
        glowMat = new Material(glow);
        lineMat = new Material(line);
        disc = ProcArt.Disc(128, false);
        shadedDisc = ProcArt.Disc(128, true);
        soft = ProcArt.SoftCircle(128);
        crownSprite = ProcArt.Crown(96);
        frameSprite = ProcArt.RoundedRect(64, 18, true, 5);
        panelSprite = ProcArt.RoundedRect(64, 18);
        glowMat.mainTexture = soft.texture;

        root = new GameObject("ArenaView").transform;

        var g = new GameObject("Ground");
        g.transform.SetParent(root, false);
        groundMesh = g.AddComponent<MeshFilter>();
        groundRenderer = g.AddComponent<MeshRenderer>();
        groundRenderer.sharedMaterial = arenaMat;
        groundRenderer.sortingOrder = 0;

        shadow = MakeSprite("Shadow", panelSprite, -10, null);
        shadow.drawMode = SpriteDrawMode.Sliced;
        shadow.color = new Color(0f, 0f, 0.05f, 0.55f);
        frameGlow = MakeSprite("FrameGlow", panelSprite, -9, glowMat);
        frameGlow.drawMode = SpriteDrawMode.Sliced;
        frameGlow.color = new Color(0.55f, 0.62f, 1f, 0.10f);
        frame = MakeSprite("Frame", frameSprite, 2, null);
        frame.drawMode = SpriteDrawMode.Sliced;
        frame.color = new Color(1f, 1f, 1f, 0.92f);

        for (int i = 0; i < views.Length; i++) views[i] = MakePlayer(i);
        MakeParticles();
        for (int i = 0; i < 8; i++) floats.Add(MakeFloat());
        MakeBokeh();
    }

    SpriteRenderer MakeSprite(string name, Sprite s, int order, Material mat, Transform parent = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent != null ? parent : root, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s;
        sr.sortingOrder = order;
        if (mat != null) sr.sharedMaterial = mat;
        return sr;
    }

    PlayerView MakePlayer(int id)
    {
        var v = new PlayerView();
        v.root = new GameObject("Player" + id).transform;
        v.root.SetParent(root, false);
        Color c = Colors[id];

        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(root, false);
        v.body = bodyGo.AddComponent<LineRenderer>();
        v.body.sharedMaterial = lineMat;
        v.body.useWorldSpace = true;
        v.body.numCapVertices = 6;
        v.body.numCornerVertices = 4;
        v.body.widthCurve = new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(1f, 0.62f));
        v.body.sortingOrder = 18;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.Lerp(c, Color.white, 0.35f), 0f), new GradientColorKey(c, 1f) },
            new[] { new GradientAlphaKey(0.0f, 0f), new GradientAlphaKey(0.95f, 0.35f), new GradientAlphaKey(1f, 1f) });
        v.body.colorGradient = grad;
        v.body.positionCount = 0;

        v.glow = MakeSprite("Glow", soft, 15, glowMat, v.root);
        v.glow.color = new Color(c.r, c.g, c.b, 0.45f);
        v.glow.transform.localScale = Vector3.one * 2.8f;

        v.head = MakeSprite("Head", shadedDisc, 20, null, v.root);
        v.head.color = Color.Lerp(c, Color.white, 0.08f);
        v.head.transform.localScale = Vector3.one * 0.92f;

        v.shine = MakeSprite("Shine", soft, 21, null, v.root);
        v.shine.color = new Color(1f, 1f, 1f, 0.55f);
        v.shine.transform.localScale = Vector3.one * 0.32f;
        v.shine.transform.localPosition = new Vector3(-0.18f, 0.2f, 0f);

        v.eyeL = MakeSprite("EyeL", disc, 22, null, v.root);
        v.eyeR = MakeSprite("EyeR", disc, 22, null, v.root);
        v.pupilL = MakeSprite("PupilL", disc, 23, null, v.root);
        v.pupilR = MakeSprite("PupilR", disc, 23, null, v.root);
        v.pupilL.color = v.pupilR.color = Pupil;

        v.crown = MakeSprite("Crown", crownSprite, 26, null, v.root);
        v.crown.transform.localScale = Vector3.one * 0.75f;
        v.crown.enabled = false;

        var lg = new GameObject("Name");
        lg.transform.SetParent(v.root, false);
        v.label = lg.AddComponent<TextMeshPro>();
        v.label.text = "";
        v.label.fontSize = 3.4f;
        v.label.alignment = TextAlignmentOptions.Center;
        v.label.color = Color.white;
        v.label.outlineWidth = 0.28f;
        v.label.outlineColor = new Color32(20, 22, 48, 255);
        v.label.rectTransform.sizeDelta = new Vector2(8f, 1.4f);
        v.label.sortingOrder = 30;
        v.label.enableWordWrapping = false;
        v.label.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        v.blinkAt = Random.Range(1f, 4f);
        v.root.gameObject.SetActive(false);
        v.body.enabled = false;
        return v;
    }

    void MakeParticles()
    {
        var go = new GameObject("Burst");
        go.transform.SetParent(root, false);
        burst = go.AddComponent<ParticleSystem>();
        burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = burst.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.6f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 900;
        var em = burst.emission;
        em.rateOverTime = 0f;
        var shape = burst.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.35f;
        var col = burst.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var size = burst.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.15f)));
        var lim = burst.limitVelocityOverLifetime;
        lim.enabled = true;
        lim.limit = 0.5f;
        lim.dampen = 0.12f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = glowMat;
        r.sortingOrder = 32;
        burst.Play();
    }

    FloatText MakeFloat()
    {
        var go = new GameObject("FloatText");
        go.transform.SetParent(root, false);
        var t = go.AddComponent<TextMeshPro>();
        t.fontSize = 5f;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.outlineWidth = 0.3f;
        t.outlineColor = new Color32(20, 22, 48, 255);
        t.rectTransform.sizeDelta = new Vector2(8f, 2f);
        t.sortingOrder = 40;
        t.enableWordWrapping = false;
        go.SetActive(false);
        return new FloatText { text = t };
    }

    void MakeBokeh()
    {
        var rng = new System.Random(11);
        for (int i = 0; i < 34; i++)
        {
            var sr = MakeSprite("Bokeh", soft, -20, glowMat);
            Color c = Colors[i % Colors.Length];
            sr.color = new Color(c.r, c.g, c.b, 0.05f + (float)rng.NextDouble() * 0.09f);
            float s = 2f + (float)rng.NextDouble() * 7f;
            sr.transform.localScale = Vector3.one * s;
            bokeh.Add(new Bokeh
            {
                t = sr.transform,
                norm = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble()),
                phase = (float)rng.NextDouble() * 6.28f,
                speed = 0.1f + (float)rng.NextDouble() * 0.25f
            });
        }
    }

    // ------------------------------------------------------------ binding an arena

    public void Bind(Arena a)
    {
        arena = a;
        if (cells == null || cells.width != a.W || cells.height != a.H)
        {
            if (cells != null) Object.Destroy(cells);
            cells = new Texture2D(a.W, a.H, TextureFormat.RGBA32, false);
            cells.filterMode = FilterMode.Point;
            cells.wrapMode = TextureWrapMode.Clamp;
            px = new Color32[a.W * a.H];
        }
        arenaMat.mainTexture = cells;
        arenaMat.SetVector("_Grid", new Vector4(a.W, a.H, 0, 0));
        for (int i = 0; i < Colors.Length; i++) arenaMat.SetColor("_C" + (i + 1), Colors[i]);

        var m = new Mesh();
        m.vertices = new[] { new Vector3(0, 0, 0), new Vector3(a.W, 0, 0), new Vector3(0, a.H, 0), new Vector3(a.W, a.H, 0) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateBounds();
        if (groundMesh.sharedMesh != null) Object.Destroy(groundMesh.sharedMesh);
        groundMesh.sharedMesh = m;

        var center = new Vector3(a.W * 0.5f, a.H * 0.5f, 0f);
        shadow.transform.localPosition = center + new Vector3(0.35f, -0.55f, 0f);
        shadow.size = new Vector2(a.W + 1.4f, a.H + 1.4f);
        frameGlow.transform.localPosition = center;
        frameGlow.size = new Vector2(a.W + 3.2f, a.H + 3.2f);
        frame.transform.localPosition = center;
        frame.size = new Vector2(a.W + 0.7f, a.H + 0.7f);

        foreach (var b in bokeh)
        {
            b.home = new Vector3(-12f + (a.W + 24f) * b.norm.x, -12f + (a.H + 24f) * b.norm.y, 0f);
            b.t.localPosition = b.home;
        }
        for (int i = 0; i < views.Length; i++)
        {
            var v = views[i];
            v.history.Clear();
            v.lastX = int.MinValue;
            v.root.gameObject.SetActive(false);
            v.body.enabled = false;
            v.body.positionCount = 0;
        }
        foreach (var f in floats) { f.used = false; f.text.gameObject.SetActive(false); }
        burst.Clear();
    }

    // ------------------------------------------------------------ every frame

    public void Render(float realTime, float dt, int leader)
    {
        if (arena == null) return;
        var a = arena;
        float now = a.time;
        for (int c = 0; c < px.Length; c++)
        {
            float fl = Mathf.Clamp01(1f - (now - a.flash[c]) / 0.55f);
            px[c] = new Color32((byte)(a.owner[c] * 51), (byte)(a.trail[c] * 51), (byte)(fl * 255), 255);
        }
        cells.SetPixels32(px);
        cells.Apply(false, false);

        for (int i = 0; i < Arena.MaxPlayers; i++) RenderPlayer(views[i], a.players[i], realTime, dt, i == leader);

        foreach (var b in bokeh)
        {
            float t = realTime * b.speed + b.phase;
            b.t.localPosition = b.home + new Vector3(Mathf.Sin(t) * 1.6f, Mathf.Cos(t * 0.8f) * 1.2f, 0f);
        }
        foreach (var f in floats)
        {
            if (!f.used) continue;
            float k = (realTime - f.born) / 1.1f;
            if (k >= 1f)
            {
                f.used = false;
                f.text.gameObject.SetActive(false);
                continue;
            }
            f.text.transform.position = f.from + new Vector3(0f, k * 1.6f, 0f);
            var col = f.text.color;
            col.a = 1f - k * k;
            f.text.color = col;
            float s = k < 0.15f ? Mathf.Lerp(0.6f, 1.15f, k / 0.15f) : Mathf.Lerp(1.15f, 1f, (k - 0.15f) / 0.85f);
            f.text.transform.localScale = Vector3.one * s;
        }
    }

    // Where a player's head is drawn (smoothly between cells).
    public Vector3 HeadPosition(Player p)
    {
        var cell = new Vector3(p.x + 0.5f, p.y + 0.5f, 0f);
        if (!p.alive) return cell;
        if (!p.remote)
        {
            float k = Mathf.Clamp01(p.moveT);
            return cell + new Vector3(Arena.DX[p.dir], Arena.DY[p.dir], 0f) * k;
        }
        var prev = new Vector3(p.px + 0.5f, p.py + 0.5f, 0f);
        if ((prev - cell).sqrMagnitude > 2.5f) return cell; // jumped (respawn): no sliding
        float t = Mathf.Clamp01((arena.time - p.stepTime) * arena.speed);
        return Vector3.Lerp(prev, cell, t);
    }

    void RenderPlayer(PlayerView v, Player p, float realTime, float dt, bool leading)
    {
        bool show = p.active && p.alive;
        if (!show)
        {
            if (v.root.gameObject.activeSelf) v.root.gameObject.SetActive(false);
            v.body.enabled = false;
            v.history.Clear();
            v.lastX = int.MinValue;
            v.scale = 0f;
            return;
        }
        if (!v.root.gameObject.activeSelf) v.root.gameObject.SetActive(true);

        // remember the last few cells for the body
        if (p.x != v.lastX || p.y != v.lastY)
        {
            if (v.lastX != int.MinValue && Mathf.Abs(p.x - v.lastX) + Mathf.Abs(p.y - v.lastY) <= 1)
                v.history.Add(new Vector3(v.lastX + 0.5f, v.lastY + 0.5f, 0f));
            else
                v.history.Clear();
            while (v.history.Count > 9) v.history.RemoveAt(0);
            v.lastX = p.x;
            v.lastY = p.y;
        }

        Vector3 pos = HeadPosition(p);
        v.shown = pos;
        v.root.localPosition = pos;

        // pop in after a respawn
        float age = arena.time - p.spawnedAt;
        float pop = age < 0.3f ? Mathf.SmoothStep(0f, 1f, age / 0.3f) * (1f + 0.25f * Mathf.Sin(age / 0.3f * Mathf.PI)) : 1f;
        float breathe = 1f + 0.03f * Mathf.Sin(realTime * 6f + p.id);
        v.root.localScale = Vector3.one * pop * breathe;

        // body: the last cells and the head, a little longer with a trail out
        int n = v.history.Count + 1;
        var pts = new Vector3[n];
        for (int i = 0; i < v.history.Count; i++) pts[i] = v.history[i];
        pts[n - 1] = pos;
        v.body.enabled = n >= 2;
        v.body.positionCount = n;
        v.body.SetPositions(pts);
        v.body.widthMultiplier = 1.25f * pop;

        // eyes look where it goes; blink now and then
        var fwd = new Vector3(Arena.DX[p.dir], Arena.DY[p.dir], 0f);
        var side = new Vector3(-fwd.y, fwd.x, 0f);
        if (realTime > v.blinkAt)
        {
            v.blinkUntil = realTime + 0.12f;
            v.blinkAt = realTime + Random.Range(2.2f, 5f);
        }
        float eyeH = realTime < v.blinkUntil ? 0.08f : 0.3f;
        PlaceEye(v.eyeL, v.pupilL, fwd * 0.17f + side * 0.2f, fwd, eyeH);
        PlaceEye(v.eyeR, v.pupilR, fwd * 0.17f - side * 0.2f, fwd, eyeH);

        float pulse = 0.4f + 0.12f * Mathf.Sin(realTime * 5f + p.id * 1.7f);
        var gc = v.glow.color;
        gc.a = p.trail.Count > 0 ? pulse + 0.15f : pulse;
        v.glow.color = gc;

        if (v.label.text != p.name) v.label.text = p.name;
        v.crown.enabled = leading && p.owned > 0;
        if (v.crown.enabled)
            v.crown.transform.localPosition = new Vector3(0f, 1.75f + 0.06f * Mathf.Sin(realTime * 4f), 0f);
    }

    static void PlaceEye(SpriteRenderer eye, SpriteRenderer pupil, Vector3 offset, Vector3 fwd, float height)
    {
        eye.transform.localPosition = offset;
        eye.transform.localScale = new Vector3(0.3f, height, 1f);
        pupil.transform.localPosition = offset + fwd * 0.06f;
        pupil.transform.localScale = new Vector3(0.15f, Mathf.Min(0.15f, height * 0.6f), 1f);
    }

    // ------------------------------------------------------------ effects

    public void Effect(ArenaEvent e, float realTime)
    {
        var pos = new Vector3(e.x, e.y, 0f);
        Color c = Colors[e.player];
        if (e.kind == ArenaEvent.Kind.Capture)
        {
            int count = Mathf.Clamp(10 + e.count / 3, 10, 90);
            Emit(pos, c, count, 0.35f + Mathf.Sqrt(e.count) * 0.08f);
            float share = 100f * e.count / arena.Total;
            if (share >= 0.5f) Float(pos, "+" + share.ToString("0") + "%", c, realTime);
        }
        else if (e.kind == ArenaEvent.Kind.Death)
        {
            Emit(pos, c, 45, 0.3f);
            Emit(pos, Color.white, 15, 0.2f);
        }
        else if (e.kind == ArenaEvent.Kind.Respawn)
        {
            Emit(pos, Color.Lerp(c, Color.white, 0.5f), 18, 0.25f);
        }
    }

    void Emit(Vector3 pos, Color c, int count, float radius)
    {
        var shape = burst.shape;
        shape.radius = radius;
        var ep = new ParticleSystem.EmitParams { position = pos, startColor = c, applyShapeToPosition = true };
        burst.Emit(ep, count);
    }

    void Float(Vector3 pos, string text, Color c, float realTime)
    {
        FloatText f = null;
        foreach (var x in floats) if (!x.used) { f = x; break; }
        if (f == null) f = floats[0];
        f.used = true;
        f.born = realTime;
        f.from = pos + new Vector3(0f, 0.6f, 0f);
        f.text.text = text;
        f.text.color = Color.Lerp(c, Color.white, 0.25f);
        f.text.gameObject.SetActive(true);
    }
}
