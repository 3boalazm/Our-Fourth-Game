using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor tooling for "Color Clash" (Our Fourth Game). Everything is made from code.
///   Menu: Color Clash / Create Scene    (re)creates the scene and materials
///   Menu: Color Clash / Build WebGL     builds the web version into ./WebBuild
///   Menu: Color Clash / Render Preview  saves Preview/*.png pictures of bot matches
/// All of them also run from the command line with -executeMethod FourthBuild.&lt;name&gt;.
/// </summary>
public static class FourthBuild
{
    const string ScenePath = "Assets/Scenes/ColorClash.unity";
    const string MatDir = "Assets/Materials";

    static Material MakeMaterial(string path, string shaderName)
    {
        var shader = Shader.Find(shaderName);
        if (shader == null) throw new System.Exception("Shader not found: " + shaderName);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else mat.shader = shader;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void Materials(out Material arena, out Material glow, out Material line)
    {
        Directory.CreateDirectory(MatDir);
        AssetDatabase.Refresh();
        arena = MakeMaterial(MatDir + "/Arena.mat", "OurFourth/Arena");
        glow = MakeMaterial(MatDir + "/Glow.mat", "OurFourth/Glow");
        line = MakeMaterial(MatDir + "/Line.mat", "Sprites/Default");
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Color Clash/Create Scene")]
    public static void CreateScene()
    {
        Material arena, glow, line;
        Materials(out arena, out glow, out line);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 12f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = ArenaView.Night;
        camGo.transform.position = new Vector3(20, 20, -10);

        var go = new GameObject("ColorClash");
        var game = go.AddComponent<OurFourthGame>();
        game.arenaMaterial = arena;
        game.glowMaterial = glow;
        game.lineMaterial = line;

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        ApplySettings();
        AssetDatabase.SaveAssets();
        Debug.Log("FourthBuild: scene created");
    }

    public static void ApplySettings()
    {
        PlayerSettings.productName = "Color Clash";
        PlayerSettings.companyName = "Our Games";
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.SplashScreen.showUnityLogo = false;
        PlayerSettings.runInBackground = true;
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.template = "PROJECT:OurFourthGame";
        PlayerSettings.WebGL.dataCaching = false; // the service worker does the caching
        // keep the custom shaders in every build
        var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
        if (gs != null)
        {
            var so = new SerializedObject(gs);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in new[] { "OurFourth/Arena", "OurFourth/Glow", "Sprites/Default" })
            {
                var sh = Shader.Find(name);
                if (sh == null) continue;
                bool found = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == sh) found = true;
                if (!found)
                {
                    list.InsertArrayElementAtIndex(list.arraySize);
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = sh;
                }
            }
            so.ApplyModifiedProperties();
        }
    }

    [MenuItem("Color Clash/Build WebGL")]
    public static void BuildWebGL()
    {
        CreateScene();
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "WebBuild",
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };
        var report = BuildPipeline.BuildPlayer(opts);
        Debug.Log("BUILD RESULT: " + report.summary.result + " size " + report.summary.totalSize);
        if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    // ------------------------------------------------------------ preview pictures (no play mode needed)

    [MenuItem("Color Clash/Render Preview")]
    public static void RenderPreview()
    {
        Material arenaMat, glow, line;
        Materials(out arenaMat, out glow, out line);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Directory.CreateDirectory("Preview");

        var view = new ArenaView(arenaMat, glow, line);
        var camGo = new GameObject("Cam", typeof(Camera));
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = ArenaView.Night;

        int[] counts = { 4, 2 };
        foreach (int n in counts)
        {
            int size = n <= 2 ? 40 : 52;
            var a = new Arena(size, size, 7 + n);
            for (int i = 0; i < n; i++)
            {
                int x, y;
                a.StartPosition(i, n, out x, out y);
                a.AddPlayer(i, new[] { "Mustafa", "Doha", "Zigzag", "Noodle" }[i], true, false, x, y);
            }
            float dt = 1f / 60f;
            for (int f = 0; f < 60 * 50; f++) a.Tick(dt, p => true);
            a.events.Clear();
            view.Bind(a);
            view.Render(10f, dt, a.Leader());

            // whole arena
            cam.transform.position = new Vector3(size * 0.5f, size * 0.5f, -10f);
            Shoot(cam, 1280, 720, size * 0.5f + 3f, "Preview/arena-" + n + "p.png");
            // close-up on the leader, like a phone in portrait
            var lead = a.players[a.Leader()];
            cam.transform.position = new Vector3(lead.x + 0.5f, lead.y + 0.5f, -10f);
            Shoot(cam, 540, 1080, 8.5f / 0.5f, "Preview/close-" + n + "p.png");
            Shoot(cam, 1280, 600, 8.5f, "Preview/landscape-" + n + "p.png");
            // a moment when somebody is out with a long tail
            int who = -1;
            for (int f = 0; f < 60 * 30 && who < 0; f++)
            {
                a.Tick(dt, p => true);
                foreach (var p in a.players) if (p.active && p.alive && p.trail.Count >= 9) who = p.id;
            }
            a.events.Clear();
            if (who >= 0)
            {
                view.Render(12f, dt, a.Leader());
                var tp = a.players[who];
                cam.transform.position = new Vector3(tp.x + 0.5f, tp.y + 0.5f, -10f);
                Shoot(cam, 1280, 600, 8.5f, "Preview/trail-" + n + "p.png");
            }
        }
        Debug.Log("FourthBuild: previews written to ./Preview");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    static void Shoot(Camera cam, int w, int h, float orthoSize, string path)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.aspect = w / (float)h;
        cam.orthographicSize = orthoSize;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
    }
}
