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
        new Color(0.36f, 0.86f, 0.30f), // green
        new Color(1.00f, 0.36f, 0.66f), // pink
        new Color(0.20f, 0.62f, 1.00f), // blue
        new Color(1.00f, 0.78f, 0.12f), // yellow
    };
    public static readonly Color Night = new Color(0.20f, 0.44f, 0.95f);
    static readonly Color SkyTop = new Color(0.42f, 0.80f, 1.00f);
    static readonly Color SkyBottom = new Color(0.42f, 0.34f, 0.96f);
    const float HeadSize = 1.32f;
    // the whole character is drawn this much bigger than a cell, like paper.io 2
    const float CharScale = 1.32f;
    // Not used any more: TMP's SDF outline looked washed out in the web build, texts sit on pills instead.
    public static Material OutlineMaterial;
    static readonly Color Pupil = new Color(0.10f, 0.11f, 0.22f);

    readonly Material arenaMat, glowMat, lineMat;
    readonly Sprite disc, shadedDisc, soft, crownSprite, frameSprite, panelSprite, pillSprite, ringSprite, cubeSprite;
    public readonly Transform root;
    Arena arena;
    Texture2D cells;
    Color32[] px;
    MeshFilter groundMesh;
    MeshRenderer groundRenderer;
    SpriteRenderer frame, frameGlow, shadow, sky;
    readonly PlayerView[] views = new PlayerView[Arena.MaxPlayers];
    ParticleSystem burst;
    readonly List<FloatText> floats = new List<FloatText>();
    readonly List<Bokeh> bokeh = new List<Bokeh>();
    readonly List<Wave> waves = new List<Wave>();
    readonly List<StarView> stars = new List<StarView>();
    readonly List<int> liveStars = new List<int>();
    static readonly Color Gold = new Color(1f, 0.8f, 0.15f);

    public Texture CellTexture { get { return cells; } }
    public Material ArenaMaterial { get { return arenaMat; } }
    public Arena Arena { get { return arena; } }

    sealed class PlayerView
    {
        public Transform root;
        public SpriteRenderer head, side, glow, shine, eyeL, eyeR, pupilL, pupilR, rimL, rimR, sparkL, sparkR, crestA, crestB, crown, tag;
        public Transform cube;
        public int lastDir;
        public float squash;
        public LineRenderer body;
        public TextMeshPro label;
        public readonly List<Vector3> history = new List<Vector3>();
        public int lastX = int.MinValue, lastY = int.MinValue;
        public float blinkAt, blinkUntil;
        public Vector3 shown;
        public float scale;
    }

    sealed class FloatText { public TextMeshPro text; public SpriteRenderer bg; public float born; public Vector3 from; public bool used; }
    sealed class StarView { public Transform root; public SpriteRenderer star, glow, shadow; }
    sealed class Wave { public SpriteRenderer sr; public float born, life, size; public Color color; public bool used; }
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
        pillSprite = ProcArt.RoundedRect(64, 28);
        ringSprite = ProcArt.Ring(256, 0.035f);
        cubeSprite = ProcArt.CubeTop(160, 0.3f);
        glowMat.mainTexture = soft.texture;

        root = new GameObject("ArenaView").transform;

        var g = new GameObject("Ground");
        g.transform.SetParent(root, false);
        groundMesh = g.AddComponent<MeshFilter>();
        groundRenderer = g.AddComponent<MeshRenderer>();
        groundRenderer.sharedMaterial = arenaMat;
        groundRenderer.sortingOrder = 0;

        sky = MakeSprite("Sky", ProcArt.Gradient(SkyTop, SkyBottom), -40, null);
        shadow = MakeSprite("Shadow", panelSprite, -10, null);
        shadow.drawMode = SpriteDrawMode.Sliced;
        shadow.color = new Color(0.04f, 0.1f, 0.4f, 0.35f);
        frameGlow = MakeSprite("FrameGlow", panelSprite, -9, glowMat);
        frameGlow.drawMode = SpriteDrawMode.Sliced;
        frameGlow.color = new Color(1f, 1f, 1f, 0.12f);
        frame = MakeSprite("Frame", frameSprite, 2, null);
        frame.drawMode = SpriteDrawMode.Sliced;
        frame.color = new Color(1f, 1f, 1f, 1f);

        for (int i = 0; i < views.Length; i++) views[i] = MakePlayer(i);
        MakeParticles();
        for (int i = 0; i < 8; i++) floats.Add(MakeFloat());
        for (int i = 0; i < 10; i++) waves.Add(MakeWave());
        var starSprite = ProcArt.Star(128);
        for (int i = 0; i < 6; i++) stars.Add(MakeStar(starSprite));
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

        // (kept for the old snake look; the shader draws the tail now)
        var bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(root, false);
        v.body = bodyGo.AddComponent<LineRenderer>();
        v.body.sharedMaterial = lineMat;
        v.body.positionCount = 0;
        v.body.enabled = false;

        v.glow = MakeSprite("Glow", soft, 15, glowMat, v.root);
        v.glow.color = new Color(c.r, c.g, c.b, 0.2f);
        v.glow.transform.localScale = Vector3.one * 3.0f;

        // a glossy rounded cube seen from above: shadow, side face, top face, shine
        v.cube = new GameObject("Cube").transform;
        v.cube.SetParent(v.root, false);
        var shade = MakeSprite("Shadow", soft, 16, null, v.cube);
        shade.color = new Color(0.05f, 0.08f, 0.25f, 0.42f);
        shade.transform.localScale = new Vector3(HeadSize * 1.55f, HeadSize * 1.15f, 1f);
        shade.transform.localPosition = new Vector3(0.12f, -0.46f, 0f);
        v.side = MakeSprite("Side", cubeSprite, 17, null, v.cube);
        v.side.color = Color.Lerp(c, Color.black, 0.42f);
        v.side.transform.localScale = Vector3.one * HeadSize;
        v.side.transform.localPosition = new Vector3(0f, -0.27f, 0f);
        v.head = MakeSprite("Top", cubeSprite, 20, null, v.cube);
        v.head.color = Color.Lerp(c, Color.white, 0.2f);
        v.head.transform.localScale = Vector3.one * HeadSize;
        v.shine = MakeSprite("Shine", soft, 21, null, v.cube);
        v.shine.color = new Color(1f, 1f, 1f, 0.6f);
        v.shine.transform.localScale = new Vector3(0.62f, 0.36f, 1f);
        v.shine.transform.localPosition = new Vector3(-0.27f, 0.33f, 0f);
        v.shine.transform.localRotation = Quaternion.Euler(0f, 0f, 28f);

        // a little crest on top, like a chick
        v.crestA = MakeSprite("CrestA", pillSprite, 22, null, v.cube);
        v.crestB = MakeSprite("CrestB", pillSprite, 22, null, v.cube);
        v.crestA.drawMode = v.crestB.drawMode = SpriteDrawMode.Sliced;
        v.crestA.size = v.crestB.size = new Vector2(0.2f, 0.5f);
        v.crestA.color = v.crestB.color = Color.Lerp(c, Color.black, 0.12f);

        // big cartoon eyes: coloured rim, white, pupil and a sparkle
        v.rimL = MakeSprite("RimL", disc, 23, null, v.cube);
        v.rimR = MakeSprite("RimR", disc, 23, null, v.cube);
        v.rimL.color = v.rimR.color = Color.Lerp(c, Color.black, 0.45f);
        v.eyeL = MakeSprite("EyeL", disc, 24, null, v.cube);
        v.eyeR = MakeSprite("EyeR", disc, 24, null, v.cube);
        v.pupilL = MakeSprite("PupilL", disc, 25, null, v.cube);
        v.pupilR = MakeSprite("PupilR", disc, 25, null, v.cube);
        v.pupilL.color = v.pupilR.color = Pupil;
        v.sparkL = MakeSprite("SparkL", disc, 26, null, v.cube);
        v.sparkR = MakeSprite("SparkR", disc, 26, null, v.cube);

        v.crown = MakeSprite("Crown", crownSprite, 27, null, v.root);
        v.crown.transform.localScale = Vector3.one * 1.0f;
        v.crown.enabled = false;

        var lg = new GameObject("Name");
        lg.transform.SetParent(v.root, false);
        v.label = lg.AddComponent<TextMeshPro>();
        v.label.text = "";
        v.label.fontSize = 4.2f;
        v.label.fontStyle = FontStyles.Bold;
        v.label.alignment = TextAlignmentOptions.Center;
        v.label.color = Color.white;
        v.label.rectTransform.sizeDelta = new Vector2(8f, 1.4f);
        v.label.sortingOrder = 30;
        v.label.enableWordWrapping = false;
        v.label.transform.localPosition = new Vector3(0f, 1.9f, 0f);
        v.tag = MakeSprite("Tag", pillSprite, 29, null, v.root);
        v.tag.drawMode = SpriteDrawMode.Sliced;
        v.tag.color = new Color(0.07f, 0.08f, 0.2f, 0.62f);
        v.tag.transform.localPosition = new Vector3(0f, 1.9f, 0f);
        v.blinkAt = Random.Range(1f, 4f);
        v.lastDir = -1;
        v.root.gameObject.SetActive(false);
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
        t.color = Color.white;
        t.rectTransform.sizeDelta = new Vector2(8f, 2f);
        t.sortingOrder = 40;
        t.enableWordWrapping = false;
        var bg = MakeSprite("Bubble", pillSprite, 39, null, go.transform);
        bg.drawMode = SpriteDrawMode.Sliced;
        go.SetActive(false);
        return new FloatText { text = t, bg = bg };
    }

    StarView MakeStar(Sprite sprite)
    {
        var s = new StarView();
        s.root = new GameObject("Star").transform;
        s.root.SetParent(root, false);
        s.glow = MakeSprite("Glow", soft, 12, glowMat, s.root);
        s.glow.color = new Color(1f, 0.85f, 0.3f, 0.5f);
        s.glow.transform.localScale = Vector3.one * 3.2f;
        s.shadow = MakeSprite("Shadow", soft, 13, null, s.root);
        s.shadow.color = new Color(0.2f, 0.1f, 0.35f, 0.35f);
        s.shadow.transform.localScale = new Vector3(1.3f, 0.55f, 1f);
        s.shadow.transform.localPosition = new Vector3(0.1f, -0.62f, 0f);
        s.star = MakeSprite("Star", sprite, 14, null, s.root);
        s.star.transform.localScale = Vector3.one * 1.55f;
        s.root.gameObject.SetActive(false);
        return s;
    }

    void RenderStars(float realTime)
    {
        arena.LiveStars(liveStars);
        for (int i = 0; i < stars.Count; i++)
        {
            var s = stars[i];
            bool on = i < liveStars.Count;
            if (s.root.gameObject.activeSelf != on) s.root.gameObject.SetActive(on);
            if (!on) continue;
            int k = liveStars[i];
            int c = arena.StarCell(k);
            float age = arena.time - Arena.StarBorn(k);
            float left = Arena.StarLife - age;
            s.root.localPosition = new Vector3(c % arena.W + 0.5f, c / arena.W + 0.5f, 0f);
            float pop = age < 0.35f ? Mathf.SmoothStep(0f, 1f, age / 0.35f) * (1f + 0.3f * Mathf.Sin(age / 0.35f * Mathf.PI)) : 1f;
            float shrink = left < 0.3f ? Mathf.Clamp01(left / 0.3f) : 1f;
            s.root.localScale = Vector3.one * pop * shrink;
            float bob = Mathf.Sin(realTime * 3.2f + k) * 0.12f;
            s.star.transform.localPosition = new Vector3(0f, 0.12f + bob, 0f);
            s.star.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(realTime * 2.1f + k) * 14f);
            // blink in the last 3 seconds
            bool visible = left > 3f || Mathf.Repeat(realTime * 6f, 1f) < 0.65f;
            s.star.enabled = visible;
            var gc = s.glow.color;
            gc.a = 0.38f + 0.14f * Mathf.Sin(realTime * 4f + k);
            s.glow.color = gc;
        }
    }

    Wave MakeWave()
    {
        var sr = MakeSprite("Wave", ringSprite, 31, null);
        sr.gameObject.SetActive(false);
        return new Wave { sr = sr };
    }

    // An expanding ring (captures, crashes, respawns).
    void Ring(Vector3 pos, Color c, float size, float life, float realTime)
    {
        Wave w = null;
        foreach (var x in waves) if (!x.used) { w = x; break; }
        if (w == null) w = waves[0];
        w.used = true;
        w.born = realTime;
        w.life = life;
        w.size = size;
        w.color = c;
        w.sr.transform.localPosition = pos;
        w.sr.gameObject.SetActive(true);
    }

    void MakeBokeh()
    {
        var rng = new System.Random(11);
        for (int i = 0; i < 34; i++)
        {
            var sr = MakeSprite("Bokeh", soft, -20, glowMat);
            Color c = Colors[i % Colors.Length];
            sr.color = new Color(Mathf.Lerp(c.r, 1f, 0.6f), Mathf.Lerp(c.g, 1f, 0.6f), Mathf.Lerp(c.b, 1f, 0.6f), 0.06f + (float)rng.NextDouble() * 0.1f);
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

    /// <summary>Stretches the sky gradient over everything the camera sees.</summary>
    public void FitBackdrop(Camera cam)
    {
        float h = cam.orthographicSize * 2f + 2f;
        float w = h * cam.aspect + 2f;
        var p = cam.transform.position;
        sky.transform.position = new Vector3(p.x, p.y, 0f);
        // the gradient sprite is 4 x 64 pixels at 64 pixels per unit
        sky.transform.localScale = new Vector3(w / (4f / 64f), h, 1f);
    }

    // ------------------------------------------------------------ binding an arena

    public void Bind(Arena a)
    {
        arena = a;
        if (cells == null || cells.width != a.W || cells.height != a.H)
        {
            if (cells != null) Object.Destroy(cells);
            // linear: the pixels are data (owner ids), not colours, and must not be gamma-converted
            cells = new Texture2D(a.W, a.H, TextureFormat.RGBA32, false, true);
            cells.filterMode = FilterMode.Point;
            cells.wrapMode = TextureWrapMode.Clamp;
            px = new Color32[a.W * a.H];
        }
        arenaMat.mainTexture = cells;
        arenaMat.SetVector("_Grid", new Vector4(a.W, a.H, 0, 0));
        for (int i = 0; i < Colors.Length; i++) arenaMat.SetColor("_C" + (i + 1), Colors[i]);
        arenaMat.SetColor("_Ground", new Color(0.93f, 0.91f, 1f));
        arenaMat.SetColor("_Ground2", new Color(0.84f, 0.81f, 0.97f));

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
        foreach (var w in waves) { w.used = false; w.sr.gameObject.SetActive(false); }
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
        RenderStars(realTime);

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
            float fade = 1f - k * k;
            var col = f.text.color;
            col.a = fade;
            f.text.color = col;
            var bc = f.bg.color;
            bc.a = 0.92f * fade;
            f.bg.color = bc;
            float s = k < 0.15f ? Mathf.Lerp(0.6f, 1.15f, k / 0.15f) : Mathf.Lerp(1.15f, 1f, (k - 0.15f) / 0.85f);
            f.text.transform.localScale = Vector3.one * s;
        }
        foreach (var w in waves)
        {
            if (!w.used) continue;
            float k = (realTime - w.born) / w.life;
            if (k >= 1f)
            {
                w.used = false;
                w.sr.gameObject.SetActive(false);
                continue;
            }
            float ease = 1f - (1f - k) * (1f - k) * (1f - k);
            w.sr.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, w.size, ease);
            var wc = w.color;
            wc.a = (1f - k) * 0.85f;
            w.sr.color = wc;
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
        v.root.localScale = Vector3.one * pop;

        // squash and stretch: a little bounce while it slides, a squish on every turn
        if (p.dir != v.lastDir) { if (v.lastDir >= 0) v.squash = 1f; v.lastDir = p.dir; }
        v.squash = Mathf.Max(0f, v.squash - dt * 5f);
        var fwd = new Vector3(Arena.DX[p.dir], Arena.DY[p.dir], 0f);
        var side = new Vector3(-fwd.y, fwd.x, 0f);
        float bob = 0.035f * Mathf.Sin(realTime * 14f + p.id * 2f);
        float sq = 0.16f * Mathf.Sin(v.squash * Mathf.PI) + bob;
        // stretch along the way it goes, squeeze across it
        float along = 1f + sq, across = 1f - sq * 0.8f;
        bool vertical = fwd.y != 0f;
        v.cube.localScale = new Vector3(vertical ? across : along, vertical ? along : across, 1f) * CharScale;
        v.cube.localRotation = Quaternion.Euler(0f, 0f, -6f * Mathf.Sin(v.squash * Mathf.PI) * (side.x + side.y));

        // a crest on the back of the head, pointing backwards
        float ang = Mathf.Atan2(fwd.y, fwd.x) * Mathf.Rad2Deg - 90f;
        v.crestA.transform.localPosition = -fwd * 0.18f + side * 0.09f;
        v.crestB.transform.localPosition = -fwd * 0.26f - side * 0.09f;
        v.crestA.transform.localRotation = Quaternion.Euler(0f, 0f, ang + 18f);
        v.crestB.transform.localRotation = Quaternion.Euler(0f, 0f, ang - 18f);

        // big eyes at the front, looking where it goes; blink now and then
        if (realTime > v.blinkAt)
        {
            v.blinkUntil = realTime + 0.12f;
            v.blinkAt = realTime + Random.Range(2.2f, 5f);
        }
        bool blink = realTime < v.blinkUntil;
        // the eyes face the camera, side by side, and lean towards where it goes
        Vector3 front = new Vector3(0f, -0.06f, 0f) + fwd * 0.13f;
        PlaceEye(v, v.rimL, v.eyeL, v.pupilL, v.sparkL, front + new Vector3(-0.23f, 0f, 0f), fwd, side, blink);
        PlaceEye(v, v.rimR, v.eyeR, v.pupilR, v.sparkR, front + new Vector3(0.23f, 0f, 0f), fwd, side, blink);

        float pulse = 0.14f + 0.05f * Mathf.Sin(realTime * 5f + p.id * 1.7f);
        var gc = v.glow.color;
        gc.a = p.trail.Count > 0 ? pulse + 0.12f : pulse;
        v.glow.color = gc;

        if (v.label.text != p.name)
        {
            v.label.text = p.name;
            float w = string.IsNullOrEmpty(p.name) ? 0f : v.label.GetPreferredValues(p.name).x;
            v.tag.enabled = w > 0f;
            v.tag.size = new Vector2(w + 0.7f, 0.78f);
        }
        v.crown.enabled = leading && p.owned > 0;
        if (v.crown.enabled)
            v.crown.transform.localPosition = new Vector3(0f, 2.8f + 0.08f * Mathf.Sin(realTime * 4f), 0f);
    }

    static void PlaceEye(PlayerView v, SpriteRenderer rim, SpriteRenderer eye, SpriteRenderer pupil, SpriteRenderer spark,
        Vector3 offset, Vector3 fwd, Vector3 side, bool blink)
    {
        // eyes are a bit taller than wide, whichever way the cube faces
        Vector3 sz = new Vector3(0.38f, blink ? 0.07f : 0.46f, 1f);
        rim.transform.localPosition = offset;
        rim.transform.localScale = sz + new Vector3(0.1f, 0.1f, 0f);
        eye.transform.localPosition = offset;
        eye.transform.localScale = sz;
        pupil.enabled = spark.enabled = !blink;
        pupil.transform.localPosition = offset + fwd * 0.085f + new Vector3(0f, -0.02f, 0f);
        pupil.transform.localScale = new Vector3(0.24f, 0.24f, 1f);
        spark.transform.localPosition = offset + fwd * 0.06f + new Vector3(-0.05f, 0.07f, 0f);
        spark.transform.localScale = new Vector3(0.09f, 0.09f, 1f);
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
            Ring(pos, Color.Lerp(c, Color.white, 0.35f), 3f + Mathf.Sqrt(e.count) * 0.9f, 0.7f, realTime);
        }
        else if (e.kind == ArenaEvent.Kind.Death)
        {
            Emit(pos, c, 45, 0.3f);
            Emit(pos, Color.white, 15, 0.2f);
            Ring(pos, Color.white, 6f, 0.5f, realTime);
            Ring(pos, c, 4f, 0.65f, realTime);
        }
        else if (e.kind == ArenaEvent.Kind.Star)
        {
            Emit(pos, Gold, 40, 0.4f);
            Emit(pos, c, 30, 1.6f);
            Ring(pos, Gold, 7f, 0.6f, realTime);
            Ring(pos, Color.white, 4.5f, 0.45f, realTime);
            float share = 100f * e.count / arena.Total;
            Float(pos, "+" + Mathf.Max(1f, share).ToString("0") + "%", Color.Lerp(Gold, Color.black, 0.05f), realTime);
        }
        else if (e.kind == ArenaEvent.Kind.Respawn)
        {
            Emit(pos, Color.Lerp(c, Color.white, 0.5f), 18, 0.25f);
            Ring(pos, c, 3.5f, 0.55f, realTime);
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
        f.text.color = Color.white;
        f.bg.color = Color.Lerp(c, Color.black, 0.18f);
        f.bg.size = new Vector2(f.text.GetPreferredValues(text).x + 0.8f, 0.95f);
        f.text.gameObject.SetActive(true);
    }
}
