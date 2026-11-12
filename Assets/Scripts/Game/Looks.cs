using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The characters players can pick: a shape and a colour. The colour is also the colour
/// of their land and tail. Sprites are drawn once in code (see ProcArt).
/// </summary>
public static class Looks
{
    public enum Shape { Cube = 0, Chick = 1, Cake = 2, Pumpkin = 3, Melon = 4, Popsicle = 5 }
    public const int ShapeCount = 6;
    public static readonly string[] ShapeNames = { "Cube", "Chick", "Cake", "Pumpkin", "Melon", "Popsicle" };

    public static readonly Color[] Palette =
    {
        new Color(0.36f, 0.86f, 0.30f), // green
        new Color(1.00f, 0.36f, 0.66f), // pink
        new Color(0.20f, 0.62f, 1.00f), // blue
        new Color(1.00f, 0.78f, 0.12f), // yellow
        new Color(1.00f, 0.50f, 0.15f), // orange
        new Color(0.62f, 0.42f, 1.00f), // purple
        new Color(1.00f, 0.27f, 0.30f), // red
        new Color(0.12f, 0.85f, 0.82f), // teal
    };
    public static readonly string[] ColorNames = { "Green", "Pink", "Blue", "Yellow", "Orange", "Purple", "Red", "Teal" };
    public static int ColorCount { get { return Palette.Length; } }

    public static readonly Color Sponge = new Color(0.98f, 0.78f, 0.52f);
    public static readonly Color Stem = new Color(0.3f, 0.62f, 0.2f);
    public static readonly Color Beak = new Color(1f, 0.55f, 0.12f);
    public static readonly Color Stick = new Color(0.96f, 0.86f, 0.62f);
    public static readonly Color Pupil = new Color(0.10f, 0.11f, 0.22f);

    static Sprite cube, tri, melon, pops, sprinkles, pumpkin, disc, pill;

    public static Sprite CubeSprite { get { return cube ?? (cube = ProcArt.CubeTop(160, 0.3f)); } }
    public static Sprite TriSprite { get { return tri ?? (tri = ProcArt.Triangle(160)); } }
    public static Sprite MelonSprite { get { return melon ?? (melon = ProcArt.MelonDecal(160)); } }
    public static Sprite PopsicleSprite { get { return pops ?? (pops = ProcArt.PopsicleDecal(160)); } }
    public static Sprite SprinkleSprite { get { return sprinkles ?? (sprinkles = ProcArt.Sprinkles(160)); } }
    public static Sprite PumpkinSprite { get { return pumpkin ?? (pumpkin = ProcArt.PumpkinFace(160)); } }
    public static Sprite DiscSprite { get { return disc ?? (disc = ProcArt.Disc(96, false)); } }
    public static Sprite PillSprite { get { return pill ?? (pill = ProcArt.RoundedRect(64, 28)); } }

    public static bool IsSlice(int shape) { return shape == (int)Shape.Melon || shape == (int)Shape.Popsicle; }
    public static bool HasEyes(int shape) { return shape != (int)Shape.Pumpkin && !IsSlice(shape); }

    public static Color SideColor(int shape, Color c)
    {
        if (shape == (int)Shape.Cake) return Color.Lerp(Sponge, Color.black, 0.12f);
        if (shape == (int)Shape.Melon) return new Color(0.2f, 0.52f, 0.16f);
        return Color.Lerp(c, Color.black, 0.42f);
    }

    public static Color TopColor(int shape, Color c) { return Color.Lerp(c, Color.white, shape == (int)Shape.Cake ? 0.28f : 0.2f); }

    public static Sprite BodySprite(int shape) { return IsSlice(shape) ? TriSprite : CubeSprite; }

    public static Sprite DecalSprite(int shape)
    {
        switch ((Shape)shape)
        {
            case Shape.Cake: return SprinkleSprite;
            case Shape.Pumpkin: return PumpkinSprite;
            case Shape.Melon: return MelonSprite;
            case Shape.Popsicle: return PopsicleSprite;
            default: return null;
        }
    }

    // ------------------------------------------------------------ a little picture of a look, for the menus

    static Image Img(RectTransform parent, Sprite s, Color c, Vector2 pos, Vector2 size, float rot = 0f)
    {
        var go = new GameObject("Part", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        rt.localRotation = Quaternion.Euler(0f, 0f, rot);
        var img = go.GetComponent<Image>();
        img.sprite = s;
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>Builds (or rebuilds) a small picture of a character inside `holder`, `size` pixels wide.</summary>
    public static void BuildIcon(RectTransform holder, int shape, int color, float size)
    {
        for (int i = holder.childCount - 1; i >= 0; i--) Object.Destroy(holder.GetChild(i).gameObject);
        Color c = Palette[Mathf.Clamp(color, 0, Palette.Length - 1)];
        float s = size;
        bool slice = IsSlice(shape);
        // slices point up-right in the picture, like a menu icon
        float rot = slice ? -30f : 0f;
        Img(holder, DiscSprite, new Color(0.05f, 0.05f, 0.2f, 0.25f), new Vector2(s * 0.06f, -s * 0.36f), new Vector2(s * 0.95f, s * 0.32f));
        if (shape == (int)Shape.Popsicle)
            Img(holder, PillSprite, Stick, new Vector2(-s * 0.24f, -s * 0.38f), new Vector2(s * 0.16f, s * 0.42f), rot);
        Img(holder, BodySprite(shape), SideColor(shape, c), new Vector2(0f, -s * 0.16f), new Vector2(s * 0.8f, s * 0.8f), rot);
        Img(holder, BodySprite(shape), TopColor(shape, c), Vector2.zero, new Vector2(s * 0.8f, s * 0.8f), rot);
        var decal = DecalSprite(shape);
        if (decal != null) Img(holder, decal, Color.white, Vector2.zero, new Vector2(s * 0.8f, s * 0.8f), rot);
        if (shape == (int)Shape.Pumpkin)
            Img(holder, PillSprite, Stem, new Vector2(s * 0.03f, s * 0.36f), new Vector2(s * 0.12f, s * 0.24f), -12f);
        if (shape == (int)Shape.Cube || shape == (int)Shape.Chick)
        {
            Color crest = shape == (int)Shape.Chick ? Color.Lerp(c, Color.white, 0.1f) : Color.Lerp(c, Color.black, 0.12f);
            Img(holder, PillSprite, crest, new Vector2(-s * 0.05f, s * 0.3f), new Vector2(s * 0.1f, s * 0.26f), 18f);
            Img(holder, PillSprite, crest, new Vector2(s * 0.06f, s * 0.31f), new Vector2(s * 0.1f, s * 0.26f), -18f);
        }
        if (HasEyes(shape))
        {
            Color rim = Color.Lerp(c, Color.black, 0.45f);
            for (int k = -1; k <= 1; k += 2)
            {
                var e = new Vector2(k * s * 0.15f, -s * 0.03f);
                Img(holder, DiscSprite, rim, e, new Vector2(s * 0.26f, s * 0.31f));
                Img(holder, DiscSprite, Color.white, e, new Vector2(s * 0.2f, s * 0.25f));
                Img(holder, DiscSprite, Pupil, e + new Vector2(0f, -s * 0.03f), new Vector2(s * 0.12f, s * 0.12f));
                Img(holder, DiscSprite, Color.white, e + new Vector2(-s * 0.025f, s * 0.01f), new Vector2(s * 0.045f, s * 0.045f));
            }
            if (shape == (int)Shape.Chick)
                Img(holder, PillSprite, Beak, new Vector2(0f, -s * 0.2f), new Vector2(s * 0.22f, s * 0.11f));
        }
    }
}
