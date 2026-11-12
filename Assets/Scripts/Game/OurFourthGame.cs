using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// "Color Clash" (Our Fourth Game): 2 to 4 players race around an arena, leave
/// trails and close loops to take land. Solo against bots, or online with friends
/// anywhere through the room relay (see /relay and NetBridge.jslib).
///
/// Online, every device drives its own player (and the host also drives the bots)
/// and tells the others each cell it enters. The host sends the land map now and
/// then so small differences between devices are corrected, and decides the end.
/// </summary>
public sealed class OurFourthGame : MonoBehaviour
{
    public Material arenaMaterial;
    public Material glowMaterial;
    public Material lineMaterial;

    enum State { Name, Menu, Solo, Join, Lobby, Playing, Results }

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly float[] Lengths = { 90f, 120f, 180f };
    static readonly string[] LengthNames = { "1:30", "2:00", "3:00" };
    static readonly string[] BotNames = { "Zigzag", "Blinky", "Noodle", "Pixel", "Turbo", "Mango" };
    const float CountdownSecs = 3.2f;

    State state;
    Camera cam;
    ArenaView view;
    Sfx sfx;
    Arena arena;      // the match (or the demo behind the menus)
    bool demo;
    float shake;
    Vector3 camVel;

    // settings
    string myName = "";
    int soloBots = 2;
    int lengthIndex = 1;
    bool muted;

    // match
    bool online, iAmHost, inMatch, matchOver;
    int mySeat;
    int matchId;
    float countdownEnd, matchEnd, duration;
    int lastCountdown;
    bool firstMatch = true;
    float leftAt; // when the clock ran out on a guest (waiting for the host's result)

    // seats (online lobby and match)
    sealed class Seat { public bool used, bot, connected; public string name = ""; }
    readonly Seat[] seats = { new Seat(), new Seat(), new Seat(), new Seat() };

    // online
    string roomCode = "", joinCode = "", pendingRoom;
    bool helloSent;
    readonly int[] lastSeq = new int[Arena.MaxPlayers];
    readonly float[] ignoreMovesUntil = new float[Arena.MaxPlayers];
    readonly HashSet<int> acked = new HashSet<int>();
    string startMsg, endMsg;
    float nextGoResend, nextSnapshot, nextEndResend;
    int goResends, endResends;

    // input
    int swipeFinger = -1;
    Vector2 swipeStart;
    bool mouseSwipe;

    // UI
    Canvas canvas;
    RectTransform namePanel, menuPanel, soloPanel, joinPanel, lobbyPanel, hudPanel, countPanel, resultPanel, howPanel;
    TextMeshProUGUI toast, nameShown, menuWho, joinCodeText, joinStatus, lobbyTitle, lobbyInfo, lobbyLink, lobbyStatus;
    TextMeshProUGUI countText, timerText, myShareText, resultTitle, resultSub, hintText;
    readonly TextMeshProUGUI[] seatLabels = new TextMeshProUGUI[4];
    readonly Image[] seatDots = new Image[4];
    readonly TextMeshProUGUI[] boardLabels = new TextMeshProUGUI[4];
    readonly Image[] barSegments = new Image[4];
    readonly TextMeshProUGUI[] resultRows = new TextMeshProUGUI[4];
    readonly Image[] resultDots = new Image[4];
    Image resultCrown;
    RectTransform barRect;
    RawImage minimap;
    readonly Image[] miniDots = new Image[4];
    Button startButton, botButton, lobbyCopy, lobbyShare, againButton, lobbyButton, muteButton;
    readonly List<Image> botChips = new List<Image>();
    readonly List<Image> lengthChipsSolo = new List<Image>();
    readonly List<Image> lengthChipsLobby = new List<Image>();
    float toastUntil;
    Sprite disc, crown;

    // ============================================================ start-up

    void Start()
    {
        Application.targetFrameRate = 60;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Application.runInBackground = true;

        cam = Camera.main;
        if (cam == null)
        {
            var cg = new GameObject("Main Camera", typeof(Camera));
            cg.tag = "MainCamera";
            cam = cg.GetComponent<Camera>();
        }
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = ArenaView.Night;
        cam.transform.position = new Vector3(20, 20, -10);

        UiKit.Round = ProcArt.RoundedRect(64, 22);
        disc = ProcArt.Disc(64, false);
        crown = ProcArt.Crown(96);
        view = new ArenaView(arenaMaterial, glowMaterial, lineMaterial);
        sfx = new Sfx(gameObject);

        myName = Clean(Net.GetStored("name"));
        int.TryParse(Net.GetStored("bots"), out soloBots);
        soloBots = Mathf.Clamp(soloBots == 0 ? 2 : soloBots, 1, 3);
        int li;
        if (int.TryParse(Net.GetStored("length"), out li)) lengthIndex = Mathf.Clamp(li, 0, Lengths.Length - 1);
        muted = Net.GetStored("muted") == "1";
        sfx.SetMuted(muted);

        BuildUi();
        StartDemo();

        string r = Net.UrlParam("room");
        if (!string.IsNullOrEmpty(r) && r.Length == 4 && IsDigits(r)) pendingRoom = r;
        if (string.IsNullOrEmpty(myName)) SetState(State.Name);
        else if (pendingRoom != null) { joinCode = pendingRoom; pendingRoom = null; GoJoin(); TryJoin(); }
        else SetState(State.Menu);
    }

    static bool IsDigits(string s)
    {
        foreach (char c in s) if (c < '0' || c > '9') return false;
        return true;
    }

    // Names travel in messages separated by "|", and the font only has Latin letters.
    static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ' ' || c == '-' || c == '_')
                sb.Append(c);
            if (sb.Length >= 12) break;
        }
        return sb.ToString().Trim();
    }

    // ============================================================ demo behind the menus

    void StartDemo()
    {
        demo = true;
        inMatch = false;
        var a = new Arena(44, 44, Random.Range(1, 99999));
        for (int i = 0; i < 4; i++)
        {
            int x, y;
            a.StartPosition(i, 4, out x, out y);
            a.AddPlayer(i, BotNames[i], true, false, x, y);
        }
        arena = a;
        view.Bind(a);
    }

    // ============================================================ UI

    static readonly Color Green = ArenaView.Colors[0];
    static readonly Color Pink = ArenaView.Colors[1];
    static readonly Color Blue = ArenaView.Colors[2];
    static readonly Color Orange = ArenaView.Colors[3];
    static readonly Color Grey = new Color(0.36f, 0.39f, 0.55f);

    string Title()
    {
        string[] letters = { "C", "O", "L", "O", "R", " ", "C", "L", "A", "S", "H" };
        var sb = new StringBuilder();
        int k = 0;
        foreach (var l in letters)
        {
            if (l == " ") { sb.Append(" "); continue; }
            sb.Append("<color=" + UiKit.Hex(ArenaView.Colors[k % 4]) + ">" + l + "</color>");
            k++;
        }
        return sb.ToString();
    }

    void BuildUi()
    {
        if (EventSystem.current == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        var cgo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = cgo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        var ct = cgo.transform;

        hudPanel = UiKit.Fill(ct, "Hud");
        countPanel = UiKit.Fill(ct, "Countdown");
        namePanel = UiKit.Fill(ct, "Name");
        menuPanel = UiKit.Fill(ct, "Menu");
        soloPanel = UiKit.Fill(ct, "Solo");
        joinPanel = UiKit.Fill(ct, "Join");
        lobbyPanel = UiKit.Fill(ct, "Lobby");
        resultPanel = UiKit.Fill(ct, "Result");
        howPanel = UiKit.Fill(ct, "How");

        BuildName();
        BuildMenu();
        BuildSolo();
        BuildJoin();
        BuildLobby();
        BuildHud();
        BuildCountdown();
        BuildResult();
        BuildHow();

        toast = UiKit.Text(ct, "", 32, Color.white, new Vector2(0, -120), new Vector2(900, 70), TextAlignmentOptions.Center, new Vector2(0.5f, 1f), true, true);
        toast.gameObject.SetActive(false);

        muteButton = UiKit.Button(ct, "", Grey, new Vector2(-80, -46), new Vector2(130, 58), ToggleMute, 26, new Vector2(1f, 1f));
        RefreshMute();
    }

    RectTransform Card(RectTransform parent, Vector2 size, Vector2 pos)
    {
        var img = UiKit.Box(parent, UiKit.Panel, pos, size, null, true);
        return img.rectTransform;
    }

    void BuildName()
    {
        var card = Card(namePanel, new Vector2(760, 560), Vector2.zero);
        UiKit.Text(card, Title(), 86, Color.white, new Vector2(0, 190), new Vector2(700, 110), TextAlignmentOptions.Center, null, true, true);
        UiKit.Text(card, "Our Fourth Game", 30, UiKit.Soft, new Vector2(0, 118), new Vector2(600, 40));
        UiKit.Text(card, "What's your name?", 36, Color.white, new Vector2(0, 52), new Vector2(600, 50), TextAlignmentOptions.Center, null, true);
        var nameBtn = UiKit.Button(card, "", new Color(0.2f, 0.23f, 0.42f), new Vector2(0, -30), new Vector2(520, 86), () => Net.AskName(myName), 40);
        nameShown = nameBtn.GetComponentInChildren<TextMeshProUGUI>();
        UiKit.Button(card, "Mustafa", Green, new Vector2(-140, -130), new Vector2(240, 64), () => SetName("Mustafa"), 30);
        UiKit.Button(card, "Doha", Pink, new Vector2(140, -130), new Vector2(240, 64), () => SetName("Doha"), 30);
        UiKit.Button(card, "Let's go", Blue, new Vector2(0, -222), new Vector2(360, 84), ConfirmName, 40);
        RefreshName();
    }

    void SetName(string n)
    {
        myName = Clean(n);
        RefreshName();
    }

    void RefreshName()
    {
        if (nameShown != null) nameShown.text = string.IsNullOrEmpty(myName) ? "<color=#9aa3c8>Tap to type</color>" : myName + "  <size=70%><color=#9aa3c8>(edit)</color></size>";
        if (menuWho != null) menuWho.text = "Playing as <b>" + myName + "</b>  <size=80%><color=#9aa3c8>change</color></size>";
    }

    void ConfirmName()
    {
        if (string.IsNullOrEmpty(myName)) { Toast("Type a name or pick one"); return; }
        Net.SetStored("name", myName);
        RefreshName();
        if (pendingRoom != null) { joinCode = pendingRoom; pendingRoom = null; GoJoin(); TryJoin(); }
        else SetState(State.Menu);
    }

    void BuildMenu()
    {
        UiKit.Text(menuPanel, Title(), 110, Color.white, new Vector2(0, -110), new Vector2(1000, 140), TextAlignmentOptions.Center, new Vector2(0.5f, 1f), true, true);
        UiKit.Text(menuPanel, "Swipe to move  -  leave your land  -  come back to take everything inside", 26, UiKit.Soft,
            new Vector2(0, -190), new Vector2(1100, 40), TextAlignmentOptions.Center, new Vector2(0.5f, 1f));
        var card = Card(menuPanel, new Vector2(560, 430), new Vector2(0, -40));
        UiKit.Button(card, "Play vs Bots", Green, new Vector2(0, 140), new Vector2(460, 90), () => SetState(State.Solo), 40);
        UiKit.Button(card, "Create a Room", Pink, new Vector2(0, 38), new Vector2(460, 90), StartHosting, 40);
        UiKit.Button(card, "Join a Room", Blue, new Vector2(0, -64), new Vector2(460, 90), GoJoin, 40);
        UiKit.Button(card, "How to play", Grey, new Vector2(0, -160), new Vector2(300, 64), () => howPanel.gameObject.SetActive(true), 28);
        var who = UiKit.Button(menuPanel, "", new Color(0.2f, 0.23f, 0.42f, 0.9f), new Vector2(0, 60), new Vector2(520, 64),
            () => SetState(State.Name), 28, new Vector2(0.5f, 0f));
        menuWho = who.GetComponentInChildren<TextMeshProUGUI>();
        RefreshName();
    }

    Image Chip(RectTransform parent, string text, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        var b = UiKit.Button(parent, text, Grey, pos, size, onClick, 32);
        return (Image)b.targetGraphic;
    }

    void BuildSolo()
    {
        var card = Card(soloPanel, new Vector2(700, 520), Vector2.zero);
        UiKit.Text(card, "Play vs Bots", 56, Color.white, new Vector2(0, 200), new Vector2(600, 70), TextAlignmentOptions.Center, null, true, true);
        UiKit.Text(card, "Bots", 30, UiKit.Soft, new Vector2(0, 120), new Vector2(400, 40));
        for (int i = 0; i < 3; i++)
        {
            int n = i + 1;
            botChips.Add(Chip(card, n.ToString(Inv), new Vector2(-150 + i * 150, 62), new Vector2(120, 70), () => { soloBots = n; Net.SetStored("bots", n.ToString(Inv)); RefreshChips(); }));
        }
        UiKit.Text(card, "Match length", 30, UiKit.Soft, new Vector2(0, -12), new Vector2(400, 40));
        for (int i = 0; i < Lengths.Length; i++)
        {
            int k = i;
            lengthChipsSolo.Add(Chip(card, LengthNames[i], new Vector2(-170 + i * 170, -70), new Vector2(150, 70), () => SetLength(k)));
        }
        UiKit.Button(card, "Start", Green, new Vector2(0, -170), new Vector2(360, 90), StartSolo, 44);
        UiKit.Button(soloPanel, "Back", Grey, new Vector2(90, 60), new Vector2(150, 64), () => SetState(State.Menu), 30, new Vector2(0f, 0f));
        RefreshChips();
    }

    void SetLength(int k)
    {
        lengthIndex = k;
        Net.SetStored("length", k.ToString(Inv));
        RefreshChips();
        if (state == State.Lobby && iAmHost) BroadcastRoster();
    }

    void RefreshChips()
    {
        for (int i = 0; i < botChips.Count; i++) botChips[i].color = i + 1 == soloBots ? Green : Grey;
        for (int i = 0; i < lengthChipsSolo.Count; i++) lengthChipsSolo[i].color = i == lengthIndex ? Orange : Grey;
        for (int i = 0; i < lengthChipsLobby.Count; i++) lengthChipsLobby[i].color = i == lengthIndex ? Orange : Grey;
    }

    void BuildJoin()
    {
        var card = Card(joinPanel, new Vector2(560, 640), Vector2.zero);
        UiKit.Text(card, "Join a Room", 50, Color.white, new Vector2(0, 260), new Vector2(500, 64), TextAlignmentOptions.Center, null, true, true);
        UiKit.Text(card, "Enter your friend's 4 digit code", 26, UiKit.Soft, new Vector2(0, 205), new Vector2(500, 40));
        joinCodeText = UiKit.Text(card, "_ _ _ _", 72, Color.white, new Vector2(0, 135), new Vector2(460, 90), TextAlignmentOptions.Center, null, true);
        string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "DEL", "0", "GO" };
        for (int i = 0; i < keys.Length; i++)
        {
            string k = keys[i];
            int row = i / 3, col = i % 3;
            Color c = k == "GO" ? Green : k == "DEL" ? Grey : new Color(0.22f, 0.25f, 0.46f);
            UiKit.Button(card, k == "DEL" ? "<" : k == "GO" ? "Join" : k, c, new Vector2(-140 + col * 140, 40 - row * 92), new Vector2(124, 78), () => KeyPressed(k), 36);
        }
        joinStatus = UiKit.Text(card, "", 26, Orange, new Vector2(0, -290), new Vector2(500, 40));
        UiKit.Button(joinPanel, "Back", Grey, new Vector2(90, 60), new Vector2(150, 64), ToMenu, 30, new Vector2(0f, 0f));
    }

    void BuildLobby()
    {
        var card = Card(lobbyPanel, new Vector2(820, 640), Vector2.zero);
        lobbyTitle = UiKit.Text(card, "Room", 60, Color.white, new Vector2(0, 260), new Vector2(760, 76), TextAlignmentOptions.Center, null, true, true);
        lobbyInfo = UiKit.Text(card, "", 26, UiKit.Soft, new Vector2(0, 205), new Vector2(760, 40));
        lobbyLink = UiKit.Text(card, "", 22, new Color(0.6f, 0.75f, 1f), new Vector2(0, 168), new Vector2(760, 34));
        lobbyCopy = UiKit.Button(card, "Copy link", Blue, new Vector2(-130, 112), new Vector2(230, 60), CopyLink, 28);
        lobbyShare = UiKit.Button(card, "Share", Pink, new Vector2(130, 112), new Vector2(230, 60), ShareInvite, 28);
        for (int i = 0; i < 4; i++)
        {
            float y = 30 - i * 66;
            UiKit.Box(card, new Color(0.18f, 0.2f, 0.38f), new Vector2(0, y), new Vector2(620, 56));
            seatDots[i] = UiKit.Pic(card, disc, ArenaView.Colors[i], new Vector2(-270, y), new Vector2(36, 36));
            seatLabels[i] = UiKit.Text(card, "", 28, Color.white, new Vector2(30, y), new Vector2(560, 50), TextAlignmentOptions.Left);
        }
        UiKit.Text(card, "Length", 24, UiKit.Soft, new Vector2(-300, -246), new Vector2(120, 40));
        for (int i = 0; i < Lengths.Length; i++)
        {
            int k = i;
            lengthChipsLobby.Add(Chip(card, LengthNames[i], new Vector2(-180 + i * 110, -246), new Vector2(100, 54), () => { if (iAmHost) SetLength(k); }));
        }
        botButton = UiKit.Button(card, "Add a bot", Grey, new Vector2(230, -246), new Vector2(220, 54), ToggleBot, 26);
        startButton = UiKit.Button(lobbyPanel, "Start!", Green, new Vector2(0, 70), new Vector2(340, 90), HostStartMatch, 44, new Vector2(0.5f, 0f));
        lobbyStatus = UiKit.Text(lobbyPanel, "", 28, Color.white, new Vector2(0, 70), new Vector2(900, 50), TextAlignmentOptions.Center, new Vector2(0.5f, 0f), true, true);
        UiKit.Button(lobbyPanel, "Leave", Grey, new Vector2(90, 60), new Vector2(150, 64), ToMenu, 30, new Vector2(0f, 0f));
        RefreshChips();
    }

    void BuildHud()
    {
        // stacked bar of everybody's land along the top
        var bar = UiKit.Box(hudPanel, new Color(0f, 0f, 0.05f, 0.45f), new Vector2(0, -30), new Vector2(620, 26), new Vector2(0.5f, 1f));
        barRect = bar.rectTransform;
        for (int i = 0; i < 4; i++)
        {
            var seg = new GameObject("Seg" + i, typeof(RectTransform), typeof(Image));
            seg.transform.SetParent(barRect, false);
            var rt = (RectTransform)seg.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.offsetMin = new Vector2(0f, 3f);
            rt.offsetMax = new Vector2(0f, -3f);
            barSegments[i] = seg.GetComponent<Image>();
            barSegments[i].color = ArenaView.Colors[i];
            barSegments[i].raycastTarget = false;
        }
        timerText = UiKit.Text(hudPanel, "2:00", 40, Color.white, new Vector2(0, -76), new Vector2(200, 50), TextAlignmentOptions.Center, new Vector2(0.5f, 1f), true, true);
        myShareText = UiKit.Text(hudPanel, "", 54, Color.white, new Vector2(110, -64), new Vector2(200, 70), TextAlignmentOptions.Center, new Vector2(0f, 1f), true, true);
        for (int i = 0; i < 4; i++)
            boardLabels[i] = UiKit.Text(hudPanel, "", 24, Color.white, new Vector2(-150, -120 - i * 34), new Vector2(260, 32), TextAlignmentOptions.Right, new Vector2(1f, 1f), true, true);
        UiKit.Button(hudPanel, "X", Grey, new Vector2(50, -140), new Vector2(64, 64), LeaveMatch, 30, new Vector2(0f, 1f));

        // minimap: the same cell texture, drawn by the arena shader
        var frame = UiKit.Box(hudPanel, new Color(1f, 1f, 1f, 0.85f), new Vector2(120, 120), new Vector2(206, 206), new Vector2(0f, 0f));
        var mgo = new GameObject("Minimap", typeof(RectTransform), typeof(RawImage));
        UiKit.Place(mgo, frame.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(194, 194));
        minimap = mgo.GetComponent<RawImage>();
        minimap.raycastTarget = false;
        for (int i = 0; i < 4; i++)
        {
            miniDots[i] = UiKit.Pic(mgo.transform, disc, Color.white, Vector2.zero, new Vector2(14, 14), new Vector2(0f, 0f));
            var o = miniDots[i].gameObject.AddComponent<Outline>();
            o.effectColor = ArenaView.Colors[i];
            o.effectDistance = new Vector2(2, 2);
        }
        hintText = UiKit.Text(hudPanel, "", 36, Color.white, new Vector2(0, 190), new Vector2(1000, 60), TextAlignmentOptions.Center, new Vector2(0.5f, 0f), true, true);
    }

    void BuildCountdown()
    {
        countText = UiKit.Text(countPanel, "3", 220, Color.white, new Vector2(0, 40), new Vector2(600, 260), TextAlignmentOptions.Center, null, true, true);
    }

    void BuildResult()
    {
        var card = Card(resultPanel, new Vector2(760, 600), Vector2.zero);
        resultCrown = UiKit.Pic(card, crown, Color.white, new Vector2(0, 262), new Vector2(120, 120));
        resultTitle = UiKit.Text(card, "", 64, Color.white, new Vector2(0, 178), new Vector2(700, 80), TextAlignmentOptions.Center, null, true, true);
        resultSub = UiKit.Text(card, "", 26, UiKit.Soft, new Vector2(0, 124), new Vector2(700, 40));
        for (int i = 0; i < 4; i++)
        {
            float y = 60 - i * 62;
            resultDots[i] = UiKit.Pic(card, disc, Color.white, new Vector2(-280, y), new Vector2(38, 38));
            resultRows[i] = UiKit.Text(card, "", 30, Color.white, new Vector2(30, y), new Vector2(580, 52), TextAlignmentOptions.Left, null, true);
        }
        againButton = UiKit.Button(card, "Play again", Green, new Vector2(-150, -235), new Vector2(270, 80), PlayAgain, 34);
        lobbyButton = UiKit.Button(card, "Menu", Grey, new Vector2(150, -235), new Vector2(270, 80), ResultBack, 34);
    }

    void BuildHow()
    {
        var shade = UiKit.Box(howPanel, new Color(0f, 0f, 0.05f, 0.6f), Vector2.zero, new Vector2(4000, 4000));
        shade.raycastTarget = true;
        var card = Card(howPanel, new Vector2(820, 560), Vector2.zero);
        UiKit.Text(card, "How to play", 54, Color.white, new Vector2(0, 220), new Vector2(700, 70), TextAlignmentOptions.Center, null, true, true);
        string body =
            "<color=" + UiKit.Hex(Green) + "><b>Swipe</b></color> (or use the arrow keys) to turn.\n\n" +
            "Leave your land and you draw a <color=" + UiKit.Hex(Pink) + "><b>tail</b></color>. Come back home and " +
            "everything inside your loop becomes yours - even other players' land.\n\n" +
            "Run into someone's tail to <color=" + UiKit.Hex(Orange) + "><b>cut</b></color> it and send them home. " +
            "Don't cross your own tail, don't hit the wall.\n\n" +
            "When the time is up, the most land <color=" + UiKit.Hex(Blue) + "><b>wins</b></color>.";
        var t = UiKit.Text(card, body, 30, Color.white, new Vector2(0, -10), new Vector2(720, 360), TextAlignmentOptions.Left);
        t.enableAutoSizing = true;
        UiKit.Button(card, "Got it", Green, new Vector2(0, -225), new Vector2(260, 76), () => howPanel.gameObject.SetActive(false), 34);
        howPanel.gameObject.SetActive(false);
    }

    void SetState(State s)
    {
        state = s;
        namePanel.gameObject.SetActive(s == State.Name);
        menuPanel.gameObject.SetActive(s == State.Menu);
        soloPanel.gameObject.SetActive(s == State.Solo);
        joinPanel.gameObject.SetActive(s == State.Join);
        lobbyPanel.gameObject.SetActive(s == State.Lobby);
        hudPanel.gameObject.SetActive(s == State.Playing);
        countPanel.gameObject.SetActive(false);
        resultPanel.gameObject.SetActive(s == State.Results);
        if (s == State.Menu || s == State.Name || s == State.Solo || s == State.Join || s == State.Lobby)
        {
            if (!demo) StartDemo();
            sfx.PlayMusic(true);
        }
        if (s == State.Lobby) RefreshLobby();
        RefreshName();
    }

    void Toast(string msg, float secs = 2.4f)
    {
        toast.text = msg;
        toast.gameObject.SetActive(true);
        toastUntil = Time.unscaledTime + secs;
    }

    void ToggleMute()
    {
        muted = !muted;
        sfx.SetMuted(muted);
        Net.SetStored("muted", muted ? "1" : "0");
        RefreshMute();
    }

    void RefreshMute()
    {
        UiKit.SetLabel(muteButton, muted ? "Muted" : "Sound");
    }

    // ============================================================ solo

    void StartSolo()
    {
        online = false;
        iAmHost = true;
        mySeat = 0;
        for (int i = 0; i < 4; i++)
        {
            seats[i].used = i <= soloBots;
            seats[i].bot = i > 0;
            seats[i].connected = true;
            seats[i].name = i == 0 ? myName : BotNames[(i + Random.Range(0, 3)) % BotNames.Length];
        }
        BeginMatch(Random.Range(1, 1000000), Random.Range(1, 1000000), Lengths[lengthIndex]);
    }

    // ============================================================ rooms

    void StartHosting()
    {
        Net.Close();
        online = true;
        iAmHost = true;
        mySeat = 0;
        roomCode = Random.Range(1000, 10000).ToString(Inv);
        for (int i = 0; i < 4; i++) { seats[i].used = false; seats[i].bot = false; seats[i].name = ""; seats[i].connected = false; }
        seats[0].used = true;
        seats[0].connected = true;
        seats[0].name = myName;
        helloSent = false;
        Net.Start(true, roomCode);
        SetState(State.Lobby);
        if (!Net.Available) lobbyStatus.text = "Online play works in the web version";
    }

    void GoJoin()
    {
        Net.Close();
        online = true;
        iAmHost = false;
        helloSent = false;
        joinCode = joinCode ?? "";
        SetState(State.Join);
        RefreshJoinCode();
        joinStatus.text = "";
    }

    void RefreshJoinCode()
    {
        string s = "";
        for (int i = 0; i < 4; i++) s += (i < joinCode.Length ? joinCode[i].ToString() : "_") + (i < 3 ? " " : "");
        joinCodeText.text = s;
    }

    void KeyPressed(string k)
    {
        if (state != State.Join) return;
        if (k == "DEL") { if (joinCode.Length > 0) joinCode = joinCode.Substring(0, joinCode.Length - 1); }
        else if (k == "GO") TryJoin();
        else if (joinCode.Length < 4) { joinCode += k; if (joinCode.Length == 4) TryJoin(); }
        RefreshJoinCode();
    }

    void TryJoin()
    {
        if (joinCode.Length != 4) { Toast("Enter all 4 digits"); return; }
        helloSent = false;
        Net.Start(false, joinCode);
        roomCode = joinCode;
        joinStatus.text = "Connecting...";
        if (!Net.Available) joinStatus.text = "Online play works in the web version";
    }

    void ToMenu()
    {
        Net.Close();
        online = false;
        inMatch = false;
        SetState(State.Menu);
    }

    string InviteLink() { return Net.PageUrl() + "?room=" + roomCode; }

    void CopyLink()
    {
        Toast(Net.CopyText(InviteLink()) ? "Invite link copied!" : "Send your friends the code " + roomCode);
    }

    void ShareInvite()
    {
        if (!Net.Share("Come play Color Clash with me! Room " + roomCode, InviteLink())) CopyLink();
    }

    int SeatCount()
    {
        int n = 0;
        foreach (var s in seats) if (s.used) n++;
        return n;
    }

    void ToggleBot()
    {
        if (!iAmHost) return;
        // remove the last bot, or add one in a free seat
        for (int i = 3; i >= 1; i--)
            if (seats[i].used && seats[i].bot && SeatCount() >= 4)
            {
                seats[i].used = false;
                BroadcastRoster();
                return;
            }
        for (int i = 1; i < 4; i++)
            if (!seats[i].used)
            {
                seats[i].used = true;
                seats[i].bot = true;
                seats[i].connected = true;
                seats[i].name = BotNames[Random.Range(0, BotNames.Length)];
                BroadcastRoster();
                return;
            }
        // full of people and bots: remove a bot
        for (int i = 3; i >= 1; i--)
            if (seats[i].used && seats[i].bot) { seats[i].used = false; BroadcastRoster(); return; }
    }

    void RefreshLobby()
    {
        if (lobbyTitle == null) return;
        lobbyTitle.text = "Room <color=" + UiKit.Hex(Orange) + ">" + roomCode + "</color>";
        lobbyLink.text = iAmHost ? InviteLink() : "";
        lobbyCopy.gameObject.SetActive(iAmHost);
        lobbyShare.gameObject.SetActive(iAmHost);
        botButton.gameObject.SetActive(iAmHost);
        startButton.gameObject.SetActive(iAmHost);
        var st = Net.Current;
        if (iAmHost)
        {
            lobbyInfo.text = st == Net.Status.InRoom ? "Send your friends the code or the link (up to 4 players)"
                : st == Net.Status.Reconnecting ? "Reconnecting to the room server..." : "Opening the room...";
            int n = SeatCount();
            startButton.interactable = n >= 2;
            lobbyStatus.text = "";
            UiKit.SetLabel(startButton, n >= 2 ? "Start!" : "Waiting for players");
            bool anyBot = false;
            foreach (var s in seats) if (s.used && s.bot) anyBot = true;
            UiKit.SetLabel(botButton, SeatCount() >= 4 || (anyBot && SeatCount() >= 3) ? "Remove a bot" : "Add a bot");
            if (SeatCount() < 4) UiKit.SetLabel(botButton, "Add a bot");
            else if (anyBot) UiKit.SetLabel(botButton, "Remove a bot");
        }
        else
        {
            lobbyInfo.text = st == Net.Status.Reconnecting ? "Reconnecting..." : "You're in! The host starts the match";
            lobbyStatus.text = "Waiting for " + (seats[0].used && seats[0].name != "" ? seats[0].name : "the host") + " to start...";
        }
        for (int i = 0; i < 4; i++)
        {
            var s = seats[i];
            string tag = "";
            if (s.used)
            {
                if (i == mySeat) tag = "  <size=75%><color=#9aa3c8>(you)</color></size>";
                else if (s.bot) tag = "  <size=75%><color=#9aa3c8>(bot)</color></size>";
                else if (i == 0) tag = "  <size=75%><color=#9aa3c8>(host)</color></size>";
            }
            seatLabels[i].text = s.used ? (s.name == "" ? "Joining..." : s.name) + tag : "<color=#6c7396>waiting for a friend...</color>";
            seatDots[i].color = s.used ? ArenaView.Colors[i] : new Color(0.3f, 0.32f, 0.45f);
        }
        RefreshChips();
    }

    // ============================================================ online messages

    string RosterString()
    {
        var parts = new string[4];
        for (int i = 0; i < 4; i++)
            parts[i] = seats[i].used ? (seats[i].bot ? "b:" : "h:") + seats[i].name : "";
        return string.Join(",", parts) + "," + lengthIndex.ToString(Inv);
    }

    void ApplyRoster(string roster)
    {
        var parts = roster.Split(',');
        for (int i = 0; i < 4 && i < parts.Length; i++)
        {
            string p = parts[i];
            seats[i].used = p.Length >= 2;
            seats[i].bot = p.StartsWith("b:");
            seats[i].name = p.Length >= 2 ? p.Substring(2) : "";
            seats[i].connected = seats[i].used;
        }
        int li;
        if (parts.Length > 4 && int.TryParse(parts[4], out li)) lengthIndex = Mathf.Clamp(li, 0, Lengths.Length - 1);
    }

    void BroadcastRoster()
    {
        if (!online || !iAmHost) return;
        Net.Send("Y|" + RosterString());
        RefreshLobby();
    }

    void PumpNet()
    {
        if (!online) return;
        string m;
        int guard = 0;
        while ((m = Net.Poll()) != null && guard++ < 400) Handle(m);

        var st = Net.Current;
        if (state == State.Join)
        {
            if (st == Net.Status.NotFound) { joinStatus.text = Net.Error == "full" ? "That room is full (4 players)" : "Room not found - check the code"; Net.Close(); }
            else if (st == Net.Status.Error) { joinStatus.text = "Can't reach the room server"; Net.Close(); }
        }
        if (state == State.Lobby && iAmHost && st == Net.Status.Taken)
        {
            // someone else uses this code right now: pick another
            roomCode = Random.Range(1000, 10000).ToString(Inv);
            Net.Start(true, roomCode);
            RefreshLobby();
        }
        if (state == State.Lobby && Time.frameCount % 30 == 0) RefreshLobby();

        // the host keeps re-sending the start until everybody confirmed it
        if (inMatch && iAmHost && startMsg != null && goResends < 15 && Time.unscaledTime >= nextGoResend)
        {
            nextGoResend = Time.unscaledTime + 0.6f;
            goResends++;
            for (int i = 1; i < 4; i++)
                if (seats[i].used && !seats[i].bot && !acked.Contains(i)) Net.Send("@" + i.ToString(Inv) + "|" + startMsg);
        }
        if (matchOver && iAmHost && endMsg != null && endResends < 4 && Time.unscaledTime >= nextEndResend)
        {
            nextEndResend = Time.unscaledTime + 0.5f;
            endResends++;
            Net.Send(endMsg);
        }
    }

    void Handle(string m)
    {
        if (m.StartsWith("N|"))
        {
            // the name typed in the browser's text box
            string n = Clean(m.Substring(2));
            if (n != "") myName = n;
            RefreshName();
            return;
        }
        if (m.Length < 2) return;
        char first = m[0];
        if (first == 'W')
        {
            var p = m.Split('|');
            int slot;
            if (p.Length >= 2 && int.TryParse(p[1], out slot)) mySeat = slot;
            if (!iAmHost)
            {
                if (state == State.Join) { sfx.Join(); SetState(State.Lobby); }
                Net.Send("H|" + myName);
            }
            else
            {
                seats[0].used = true;
                seats[0].name = myName;
                BroadcastRoster();
            }
            return;
        }
        if (first == 'J')
        {
            int slot;
            if (!int.TryParse(m.Substring(2), out slot) || slot < 0 || slot > 3) return;
            if (iAmHost)
            {
                if (seats[slot].bot && seats[slot].used)
                {
                    // a friend arrived in a seat a bot was keeping warm: move the bot
                    string botName = seats[slot].name;
                    seats[slot].used = false;
                    for (int i = 1; i < 4; i++)
                        if (!seats[i].used && i != slot) { seats[i].used = true; seats[i].bot = true; seats[i].name = botName; break; }
                }
                seats[slot].used = true;
                seats[slot].bot = false;
                seats[slot].connected = true;
                if (!inMatch) sfx.Join();
                BroadcastRoster();
            }
            return;
        }
        if (first == 'L')
        {
            int slot;
            if (!int.TryParse(m.Substring(2), out slot) || slot < 0 || slot > 3) return;
            OnLeft(slot);
            return;
        }
        // "<slot>|<payload>" from another player
        int bar = m.IndexOf('|');
        if (bar <= 0) return;
        int from;
        if (!int.TryParse(m.Substring(0, bar), out from) || from < 0 || from > 3) return;
        HandlePayload(from, m.Substring(bar + 1));
    }

    void OnLeft(int slot)
    {
        if (slot == 0 && !iAmHost)
        {
            if (inMatch && !matchOver)
            {
                Toast("The host left the game", 3f);
                FinishMatch(true);
            }
            else if (state == State.Lobby)
            {
                Toast("The host closed the room", 3f);
                ToMenu();
            }
            return;
        }
        if (iAmHost && !inMatch)
        {
            seats[slot].used = false;
            seats[slot].connected = false;
            BroadcastRoster();
        }
        if (inMatch)
        {
            seats[slot].connected = false;
            var p = arena.players[slot];
            if (p.active && p.alive) arena.Kill(p, -1, Cause.Crash);
            p.respawnAt = float.MaxValue;
            Toast((p.name != "" ? p.name : "A player") + " left", 2.5f);
        }
    }

    void HandlePayload(int from, string payload)
    {
        var p = payload.Split('|');
        if (p.Length == 0) return;
        switch (p[0])
        {
            case "H":
                if (iAmHost && p.Length >= 2)
                {
                    seats[from].used = true;
                    seats[from].bot = false;
                    seats[from].connected = true;
                    seats[from].name = Clean(p[1]);
                    if (seats[from].name == "") seats[from].name = "Player " + (from + 1);
                    BroadcastRoster();
                }
                break;
            case "Y":
                if (!iAmHost && from == 0 && p.Length >= 2) { ApplyRoster(p[1]); RefreshLobby(); }
                break;
            case "S":
                if (!iAmHost && from == 0 && p.Length >= 6) OnStart(p);
                break;
            case "A":
                if (iAmHost && p.Length >= 2 && p[1] == matchId.ToString(Inv)) acked.Add(from);
                break;
            case "M": OnMove(from, p); break;
            case "D": OnDeath(from, p); break;
            case "R": OnRespawn(from, p); break;
            case "G": if (!iAmHost && from == 0) OnSnapshot(p); break;
            case "E": if (!iAmHost && from == 0) OnEnd(p); break;
            case "B":
                if (!iAmHost && from == 0 && state == State.Results) SetState(State.Lobby);
                break;
        }
    }

    bool CurrentMatch(string id) { return inMatch && id == matchId.ToString(Inv); }

    // May `from` speak for player `pid`? Yourself, or the host for its bots.
    bool Speaks(int from, int pid)
    {
        if (pid < 0 || pid > 3) return false;
        if (from == pid) return true;
        return from == 0 && seats[pid].bot;
    }

    void OnStart(string[] p)
    {
        // S|matchId|seed|size|duration|roster
        int id, seed, size;
        float dur;
        if (!int.TryParse(p[1], out id) || !int.TryParse(p[2], out seed) || !int.TryParse(p[3], out size) ||
            !float.TryParse(p[4], NumberStyles.Float, Inv, out dur)) return;
        Net.Send("A|" + p[1]);
        if (inMatch && id == matchId) return; // a repeat of the start we already have
        ApplyRoster(p[5]);
        matchId = id;
        BeginMatchShared(seed, size, dur);
    }

    void OnMove(int from, string[] p)
    {
        // M|matchId|pid|seq|x|y|dir
        if (p.Length < 7 || !CurrentMatch(p[1])) return;
        int pid, seq, x, y, dir;
        if (!int.TryParse(p[2], out pid) || !Speaks(from, pid) || pid == mySeat) return;
        if (!int.TryParse(p[3], out seq) || !int.TryParse(p[4], out x) || !int.TryParse(p[5], out y) || !int.TryParse(p[6], out dir)) return;
        if (seq <= lastSeq[pid]) return;
        lastSeq[pid] = seq;
        var q = arena.players[pid];
        if (!q.active) return;
        if (!q.alive)
        {
            // we think they crashed (we cut them); give their device a moment to agree
            if (Time.unscaledTime < ignoreMovesUntil[pid]) return;
            q.alive = true;
            q.spawnedAt = arena.time;
            q.x = q.px = x;
            q.y = q.py = y;
            q.dir = dir & 3;
            return;
        }
        q.dir = dir & 3;
        arena.Enter(q, x, y);
    }

    void OnDeath(int from, string[] p)
    {
        // D|matchId|pid
        int pid;
        if (p.Length < 3 || !CurrentMatch(p[1]) || !int.TryParse(p[2], out pid) || !Speaks(from, pid) || pid == mySeat) return;
        var q = arena.players[pid];
        if (q.alive) arena.Kill(q, -1, Cause.Crash);
        q.respawnAt = float.MaxValue; // their device brings them back
    }

    void OnRespawn(int from, string[] p)
    {
        // R|matchId|pid|x|y|newBase|dir
        int pid, x, y, nb, dir;
        if (p.Length < 7 || !CurrentMatch(p[1]) || !int.TryParse(p[2], out pid) || !Speaks(from, pid) || pid == mySeat) return;
        if (!int.TryParse(p[3], out x) || !int.TryParse(p[4], out y) || !int.TryParse(p[5], out nb) || !int.TryParse(p[6], out dir)) return;
        if (!arena.In(x, y)) return;
        var q = arena.players[pid];
        if (q.alive) arena.Kill(q, -1, Cause.Crash);
        arena.Respawn(q, x, y, nb == 1, dir);
        q.respawnAt = float.MaxValue;
    }

    void OnSnapshot(string[] p)
    {
        // G|matchId|seq0,seq1,seq2,seq3|land
        if (p.Length < 4 || !CurrentMatch(p[1]) || matchOver) return;
        var seqs = p[2].Split(',');
        int mine;
        if (seqs.Length < 4 || !int.TryParse(seqs[mySeat], out mine)) return;
        var me = arena.players[mySeat];
        // only when the host has seen all our moves and we are safely home
        if (mine != me.seq || me.trail.Count > 0) return;
        arena.DecodeLand(p[3], false);
    }

    void OnEnd(string[] p)
    {
        // E|matchId|land
        if (p.Length < 3 || !CurrentMatch(p[1])) return;
        if (state == State.Results && matchOver) return;
        arena.DecodeLand(p[2], false);
        FinishMatch(false);
    }

    void SendSnapshot()
    {
        var parts = new string[4];
        for (int i = 0; i < 4; i++)
        {
            var pl = arena.players[i];
            parts[i] = (i == mySeat || seats[i].bot ? pl.seq : lastSeq[i]).ToString(Inv);
        }
        Net.Send("G|" + matchId.ToString(Inv) + "|" + string.Join(",", parts) + "|" + arena.EncodeLand());
    }

    // ============================================================ matches

    void HostStartMatch()
    {
        if (!iAmHost || SeatCount() < 2) return;
        if (Net.Current != Net.Status.InRoom) { Toast("Still connecting to the room server..."); return; }
        matchId = Random.Range(1, 1000000);
        int seed = Random.Range(1, 1000000);
        float dur = Lengths[lengthIndex];
        int size = ArenaSize(SeatCount());
        startMsg = "S|" + matchId.ToString(Inv) + "|" + seed.ToString(Inv) + "|" + size.ToString(Inv) + "|" +
                   dur.ToString(Inv) + "|" + RosterString();
        acked.Clear();
        goResends = 0;
        nextGoResend = Time.unscaledTime + 0.6f;
        Net.Send(startMsg);
        BeginMatchShared(seed, size, dur);
    }

    static int ArenaSize(int players) { return players <= 2 ? 40 : players == 3 ? 46 : 52; }

    void BeginMatch(int id, int seed, float dur)
    {
        matchId = id;
        BeginMatchShared(seed, ArenaSize(SeatCount()), dur);
    }

    void BeginMatchShared(int seed, int size, float dur)
    {
        var a = new Arena(size, size, seed);
        int order = 0, count = SeatCount();
        for (int i = 0; i < 4; i++)
        {
            lastSeq[i] = 0;
            ignoreMovesUntil[i] = 0f;
            if (!seats[i].used) continue;
            int x, y;
            a.StartPosition(order++, count, out x, out y);
            bool bot = seats[i].bot;
            bool remote = online && i != mySeat && !(iAmHost && bot);
            a.AddPlayer(i, seats[i].name, bot, remote, x, y);
            if (remote) a.players[i].respawnAt = float.MaxValue;
        }
        a.onStep = OnLocalStep;
        a.onRespawn = OnLocalRespawn;
        arena = a;
        demo = false;
        inMatch = true;
        matchOver = false;
        duration = dur;
        countdownEnd = Time.unscaledTime + CountdownSecs;
        matchEnd = countdownEnd + dur;
        lastCountdown = 99;
        nextSnapshot = countdownEnd + 1.5f;
        endMsg = null;
        leftAt = 0f;
        view.Bind(a);
        SetState(State.Playing);
        countPanel.gameObject.SetActive(true);
        hintText.text = firstMatch ? "Swipe to turn - make a loop and come home!" : "";
        minimap.texture = view.CellTexture;
        minimap.material = view.ArenaMaterial;
        var me = a.players[mySeat];
        cam.transform.position = new Vector3(me.x + 0.5f, me.y + 0.5f, -10f);
        sfx.PlayMusic(true);
    }

    bool DrivesHere(Player p) { return !p.remote && (!online || p.id == mySeat || (iAmHost && p.bot)); }

    void OnLocalStep(Player p)
    {
        if (!online || demo || !inMatch) return;
        Net.Send("M|" + matchId.ToString(Inv) + "|" + p.id.ToString(Inv) + "|" + p.seq.ToString(Inv) + "|" +
                 p.x.ToString(Inv) + "|" + p.y.ToString(Inv) + "|" + p.dir.ToString(Inv));
    }

    void OnLocalRespawn(Player p)
    {
        if (!online || demo || !inMatch) return;
        // a fresh base is announced so the others build the same little square
        bool fresh = p.freshBase;
        Net.Send("R|" + matchId.ToString(Inv) + "|" + p.id.ToString(Inv) + "|" + p.x.ToString(Inv) + "|" + p.y.ToString(Inv) + "|" +
                 (fresh ? "1" : "0") + "|" + p.dir.ToString(Inv));
    }

    void LeaveMatch()
    {
        if (online) ToMenu();
        else
        {
            inMatch = false;
            SetState(State.Menu);
        }
    }

    void PlayAgain()
    {
        if (!online) { StartSolo(); return; }
        if (!iAmHost) return;
        if (Net.Current != Net.Status.InRoom) { Toast("Your friends are gone"); ToMenu(); return; }
        HostStartMatch();
    }

    void ResultBack()
    {
        if (online && iAmHost)
        {
            Net.Send("B|" + matchId.ToString(Inv));
            inMatch = false;
            SetState(State.Lobby);
        }
        else if (online) ToMenu();
        else
        {
            inMatch = false;
            SetState(State.Menu);
        }
    }

    void FinishMatch(bool hostGone)
    {
        if (matchOver && state == State.Results) return;
        matchOver = true;
        firstMatch = false;
        if (online && iAmHost && !hostGone)
        {
            endMsg = "E|" + matchId.ToString(Inv) + "|" + arena.EncodeLand();
            endResends = 0;
            nextEndResend = Time.unscaledTime;
        }
        ShowResults(hostGone);
    }

    void ShowResults(bool hostGone)
    {
        SetState(State.Results);
        var order = new List<Player>();
        foreach (var p in arena.players) if (p.active) order.Add(p);
        order.Sort((x, y) => y.owned.CompareTo(x.owned));
        var winner = order.Count > 0 ? order[0] : null;
        bool iWon = winner != null && winner.id == mySeat;
        bool tie = order.Count > 1 && order[0].owned == order[1].owned;
        resultTitle.text = tie ? "It's a tie!" : iWon ? "You win!" : "<color=" + UiKit.Hex(ArenaView.Colors[winner.id]) + ">" + winner.name + "</color> wins!";
        resultSub.text = hostGone ? "The host left - final land when they went" : "Most land when the time ran out";
        resultCrown.color = winner != null ? Color.white : Color.clear;
        for (int i = 0; i < 4; i++)
        {
            bool has = i < order.Count;
            resultRows[i].gameObject.SetActive(has);
            resultDots[i].gameObject.SetActive(has);
            if (!has) continue;
            var p = order[i];
            resultDots[i].color = ArenaView.Colors[p.id];
            string you = p.id == mySeat ? " <size=70%><color=#9aa3c8>(you)</color></size>" : "";
            resultRows[i].text = (i + 1).ToString(Inv) + ".  " + p.name + you + "   <b>" + (100f * arena.Share(p)).ToString("0.0", Inv) +
                                 "%</b>   <size=65%><color=#9aa3c8>" + p.captures + " loops, " + p.kills + " cuts</color></size>";
        }
        bool canAgain = !online || iAmHost;
        againButton.gameObject.SetActive(canAgain);
        UiKit.SetLabel(lobbyButton, online && iAmHost ? "Lobby" : online ? "Leave" : "Menu");
        if (!canAgain) UiKit.SetLabel(againButton, "Waiting for host");
        if (iWon && !tie) sfx.Win(); else sfx.Lose();
        Net.Vibrate(iWon ? 120 : 40);
    }

    // ============================================================ main loop

    void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        PumpNet();
        if (toast.gameObject.activeSelf && Time.unscaledTime > toastUntil) toast.gameObject.SetActive(false);

        if (demo)
        {
            arena.Tick(dt, p => true);
            arena.events.Clear();
            if (arena.time > 100f) StartDemo();
        }
        else if (inMatch)
        {
            UpdateMatch(dt);
        }
        int leader = arena.Leader();
        view.Render(Time.unscaledTime, dt, leader);
        UpdateCamera(dt);
        if (state == State.Playing) UpdateHud(leader);
    }

    void UpdateMatch(float dt)
    {
        float now = Time.unscaledTime;
        var me = arena.players[mySeat];

        if (now < countdownEnd)
        {
            int n = Mathf.CeilToInt(countdownEnd - now - 0.2f);
            if (n != lastCountdown)
            {
                lastCountdown = n;
                if (n > 0) sfx.Tick();
                countText.text = n > 0 ? n.ToString(Inv) : "GO!";
                countText.transform.localScale = Vector3.one * 1.3f;
            }
            countText.transform.localScale = Vector3.Lerp(countText.transform.localScale, Vector3.one, dt * 8f);
            HandleInput(me); // turns can be queued during the countdown
            return;
        }
        if (countPanel.gameObject.activeSelf)
        {
            if (lastCountdown != 0) { sfx.Go(); lastCountdown = 0; }
            countText.text = "GO!";
            if (now > countdownEnd + 0.6f) countPanel.gameObject.SetActive(false);
        }
        if (matchOver) return;

        bool clockDone = now >= matchEnd;
        if (clockDone)
        {
            if (!online || iAmHost) { FinishMatch(false); return; }
            // a guest waits for the host's final map (but not forever)
            if (leftAt == 0f) { leftAt = now; hintText.text = "Time!"; }
            if (now - leftAt > 4f) FinishMatch(false);
            return;
        }

        HandleInput(me);
        arena.Tick(dt, DrivesHere);
        HandleEvents();
        if (online && iAmHost && now >= nextSnapshot)
        {
            nextSnapshot = now + 1.5f;
            SendSnapshot();
        }
        if (hintText.text != "" && now > countdownEnd + 5f && leftAt == 0f) hintText.text = "";
    }

    void HandleEvents()
    {
        float now = Time.unscaledTime;
        foreach (var e in arena.events)
        {
            view.Effect(e, now);
            var p = arena.players[e.player];
            bool mine = e.player == mySeat;
            if (e.kind == ArenaEvent.Kind.Capture)
            {
                if (mine)
                {
                    sfx.Capture(e.count > 40);
                    Net.Vibrate(25);
                    shake = Mathf.Max(shake, Mathf.Min(0.35f, e.count / 300f));
                }
            }
            else if (e.kind == ArenaEvent.Kind.Death)
            {
                if (online && DrivesHere(p))
                {
                    Net.Send("D|" + matchId.ToString(Inv) + "|" + p.id.ToString(Inv));
                }
                if (p.remote) ignoreMovesUntil[p.id] = now + 0.6f;
                if (mine)
                {
                    sfx.Crash();
                    Net.Vibrate(90);
                    shake = 0.6f;
                    string killer = e.other >= 0 && e.other != mySeat ? arena.players[e.other].name : "";
                    switch (e.cause)
                    {
                        case Cause.Wall: Toast("Ouch, the wall!"); break;
                        case Cause.Self: Toast("You crossed your own tail"); break;
                        case Cause.Cut: Toast(killer + " cut your tail!"); break;
                        case Cause.Caught: Toast(killer + " surrounded you!"); break;
                        default: Toast("Crash!"); break;
                    }
                }
                else if (e.other == mySeat)
                {
                    sfx.Cut();
                    Net.Vibrate(40);
                    Toast("You got " + p.name + "!");
                }
            }
        }
        arena.events.Clear();
    }

    // ------------------------------------------------------------ input: swipe, drag, keys

    void HandleInput(Player me)
    {
        if (me == null || !me.active) return;
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) TurnMe(me, 0);
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) TurnMe(me, 1);
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) TurnMe(me, 2);
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) TurnMe(me, 3);

        float threshold = Mathf.Max(16f, Mathf.Min(Screen.width, Screen.height) * 0.035f);
        if (Input.touchCount > 0)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                if (t.phase == TouchPhase.Began)
                {
                    if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(t.fingerId)) continue;
                    swipeFinger = t.fingerId;
                    swipeStart = t.position;
                }
                else if (t.fingerId == swipeFinger && (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary))
                {
                    var d = t.position - swipeStart;
                    if (d.magnitude >= threshold)
                    {
                        TurnMe(me, DirOf(d));
                        swipeStart = t.position;
                    }
                }
                else if (t.fingerId == swipeFinger && (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled))
                {
                    swipeFinger = -1;
                }
            }
            return;
        }
        // mouse drag works like a swipe on a computer
        if (Input.GetMouseButtonDown(0))
        {
            mouseSwipe = EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject();
            swipeStart = Input.mousePosition;
        }
        else if (Input.GetMouseButton(0) && mouseSwipe)
        {
            var d = (Vector2)Input.mousePosition - swipeStart;
            if (d.magnitude >= threshold)
            {
                TurnMe(me, DirOf(d));
                swipeStart = Input.mousePosition;
            }
        }
        else if (Input.GetMouseButtonUp(0)) mouseSwipe = false;
    }

    static int DirOf(Vector2 d)
    {
        if (Mathf.Abs(d.x) > Mathf.Abs(d.y)) return d.x > 0 ? 1 : 3;
        return d.y > 0 ? 0 : 2;
    }

    void TurnMe(Player me, int d)
    {
        int before = me.turns.Count;
        arena.Turn(me, d);
        if (me.turns.Count > before) sfx.Turn();
    }

    // ------------------------------------------------------------ camera and HUD

    void UpdateCamera(float dt)
    {
        float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        Vector3 target;
        float size;
        if (demo || state == State.Results)
        {
            // the whole arena (the menus float on top)
            target = new Vector3(arena.W * 0.5f, arena.H * 0.5f, -10f);
            size = Mathf.Max(arena.H * 0.5f + 3f, (arena.W * 0.5f + 3f) / aspect);
            if (demo) size *= 0.78f;
        }
        else
        {
            var me = arena.players[mySeat];
            var head = view.HeadPosition(me);
            target = new Vector3(head.x, head.y, -10f);
            size = aspect >= 1f ? 11f : 11f / aspect;
            size = Mathf.Min(size, arena.H * 0.5f + 4f);
        }
        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, size, Mathf.Min(1f, dt * 3f));
        var pos = Vector3.SmoothDamp(cam.transform.position, target, ref camVel, demo ? 1.2f : 0.18f);
        shake = Mathf.Max(0f, shake - dt * 1.8f);
        var jitter = shake > 0f ? (Vector3)(Random.insideUnitCircle * shake) : Vector3.zero;
        cam.transform.position = new Vector3(pos.x, pos.y, -10f) + jitter;
    }

    void UpdateHud(int leader)
    {
        float left = Mathf.Max(0f, matchEnd - Mathf.Max(Time.unscaledTime, countdownEnd));
        int secs = Mathf.CeilToInt(left);
        timerText.text = (secs / 60).ToString(Inv) + ":" + (secs % 60).ToString("00", Inv);
        timerText.color = secs <= 10 ? Orange : Color.white;

        var me = arena.players[mySeat];
        myShareText.text = "<color=" + UiKit.Hex(ArenaView.Colors[mySeat]) + ">" + (100f * arena.Share(me)).ToString("0", Inv) + "%</color>";

        float width = barRect.rect.width;
        float x = 0f;
        var order = new List<Player>();
        for (int i = 0; i < 4; i++)
        {
            var p = arena.players[i];
            float w = p.active ? width * arena.Share(p) : 0f;
            var rt = barSegments[i].rectTransform;
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
            x += w;
            if (p.active) order.Add(p);
        }
        order.Sort((a, b) => b.owned.CompareTo(a.owned));
        for (int i = 0; i < 4; i++)
        {
            bool has = i < order.Count;
            boardLabels[i].gameObject.SetActive(has);
            if (!has) continue;
            var p = order[i];
            string crownMark = p.id == leader ? "* " : "";
            boardLabels[i].text = "<color=" + UiKit.Hex(ArenaView.Colors[p.id]) + ">" + crownMark + p.name + "</color>  " +
                                  (100f * arena.Share(p)).ToString("0", Inv) + "%";
        }
        // minimap dots
        var mrt = minimap.rectTransform.rect;
        for (int i = 0; i < 4; i++)
        {
            var p = arena.players[i];
            bool show = p.active && p.alive;
            miniDots[i].gameObject.SetActive(show);
            if (!show) continue;
            var hp = view.HeadPosition(p);
            ((RectTransform)miniDots[i].transform).anchoredPosition = new Vector2(hp.x / arena.W * mrt.width, hp.y / arena.H * mrt.height);
        }
    }
}
