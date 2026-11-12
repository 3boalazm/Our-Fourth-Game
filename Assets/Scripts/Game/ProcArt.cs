using UnityEngine;

/// <summary>All the game's pictures are drawn in code (no image files needed).</summary>
public static class ProcArt
{
    static Texture2D NewTex(int w, int h)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        return t;
    }

    static Sprite ToSprite(Texture2D t, float ppu, Vector4 border = default(Vector4))
    {
        t.Apply(false, false);
        return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), ppu, 0,
            SpriteMeshType.FullRect, border);
    }

    static float Smooth(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    /// <summary>Soft round glow (white, fading out), for glows and particles. 1 unit wide.</summary>
    public static Sprite SoftCircle(int size)
    {
        var t = NewTex(size, size);
        var px = new Color32[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        t.SetPixels32(px);
        return ToSprite(t, size);
    }

    /// <summary>A solid disc with an anti-aliased edge. Grey shading so it can be tinted. 1 unit wide.</summary>
    public static Sprite Disc(int size, bool shaded)
    {
        var t = NewTex(size, size);
        var px = new Color32[size * size];
        float r = size * 0.5f - 1f;
        var c = new Vector2(size * 0.5f, size * 0.5f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Vector2.Distance(p, c);
                float a = Mathf.Clamp01(r - d + 0.5f);
                float v = 1f;
                if (shaded)
                {
                    // lighter towards the top-left, darker rim
                    float lit = Vector2.Dot((p - c) / r, new Vector2(-0.5f, 0.6f));
                    v = Mathf.Lerp(0.78f, 1f, 0.5f + 0.5f * lit);
                    v *= Mathf.Lerp(1f, 0.82f, Smooth(0.72f, 1f, d / r));
                }
                byte g = (byte)(Mathf.Clamp01(v) * 255);
                px[y * size + x] = new Color32(g, g, g, (byte)(a * 255));
            }
        t.SetPixels32(px);
        return ToSprite(t, size);
    }

    /// <summary>A ring (outline of a disc). 1 unit wide.</summary>
    public static Sprite Ring(int size, float thickness)
    {
        var t = NewTex(size, size);
        var px = new Color32[size * size];
        float r = size * 0.5f - 1f;
        float inner = r - thickness * size;
        var c = new Vector2(size * 0.5f, size * 0.5f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                float a = Mathf.Clamp01(r - d + 0.5f) * Mathf.Clamp01(d - inner + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        t.SetPixels32(px);
        return ToSprite(t, size);
    }

    /// <summary>A white rounded rectangle for 9-slicing (UI panels and buttons).</summary>
    public static Sprite RoundedRect(int size, int radius, bool outlineOnly = false, int stroke = 4)
    {
        var t = NewTex(size, size);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - size * 0.5f) - (size * 0.5f - radius);
                float qy = Mathf.Abs(y + 0.5f - size * 0.5f) - (size * 0.5f - radius);
                float d = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                          + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                float a = Mathf.Clamp01(0.5f - d);
                if (outlineOnly) a *= Mathf.Clamp01(d + stroke + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        t.SetPixels32(px);
        return ToSprite(t, 100f, new Vector4(radius + 2, radius + 2, radius + 2, radius + 2));
    }

    /// <summary>A little golden crown for whoever is in the lead.</summary>
    public static Sprite Crown(int size)
    {
        var t = NewTex(size, size);
        var px = new Color32[size * size];
        var gold = new Color(1f, 0.82f, 0.25f);
        var dark = new Color(0.72f, 0.48f, 0.08f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                // band
                bool band = v > 0.18f && v < 0.42f && u > 0.12f && u < 0.88f;
                // three spikes
                float spikes = 0f;
                float[] cx = { 0.2f, 0.5f, 0.8f };
                foreach (float s in cx)
                {
                    float w = 0.17f * (1f - (v - 0.4f) / 0.5f);
                    if (v >= 0.4f && v < 0.9f && Mathf.Abs(u - s) < w) spikes = 1f;
                }
                // jewels on the tips
                float jewel = 0f;
                foreach (float s in cx)
                    if (Vector2.Distance(new Vector2(u, v), new Vector2(s, 0.88f)) < 0.07f) jewel = 1f;
                float a = (band || spikes > 0f || jewel > 0f) ? 1f : 0f;
                Color col = Color.Lerp(dark, gold, Mathf.Clamp01((v - 0.15f) * 2f));
                if (jewel > 0f) col = new Color(1f, 0.95f, 0.7f);
                px[y * size + x] = new Color32((byte)(col.r * 255), (byte)(col.g * 255), (byte)(col.b * 255), (byte)(a * 255));
            }
        t.SetPixels32(px);
        return ToSprite(t, size);
    }

    /// <summary>
    /// The top of a soft rounded cube, seen from above (grey shading so it can be tinted):
    /// lit from the top left, a darker rim and a soft bevel. 1 unit wide.
    /// </summary>
    public static Sprite CubeTop(int size, float radius)
    {
        var t = NewTex(size, size);
        var px = new Color32[size * size];
        float half = size * 0.5f;
        float rad = radius * size;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Abs(x + 0.5f - half) - (half - 1f - rad);
                float qy = Mathf.Abs(y + 0.5f - half) - (half - 1f - rad);
                float d = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                          + Mathf.Min(Mathf.Max(qx, qy), 0f) - rad;
                float a = Mathf.Clamp01(0.5f - d);
                float inside = Mathf.Clamp01(-d / (size * 0.16f)); // 0 at the edge, 1 well inside
                float u = (x + 0.5f) / size - 0.5f, v = (y + 0.5f) / size - 0.5f;
                float lit = Mathf.Clamp01(0.5f + (-u * 0.55f + v * 0.75f));
                float shade = Mathf.Lerp(0.84f, 1f, lit);
                // bevel: darker towards the outline, a little lighter just inside it
                shade *= Mathf.Lerp(0.8f, 1f, Smooth(0f, 0.5f, inside));
                shade += 0.06f * Smooth(0.25f, 0.6f, inside) * (1f - Smooth(0.6f, 1f, inside));
                byte g = (byte)(Mathf.Clamp01(shade) * 255);
                px[y * size + x] = new Color32(g, g, g, (byte)(a * 255));
            }
        t.SetPixels32(px);
        return ToSprite(t, size);
    }

    // Signed distance to a 5 pointed star (negative inside); r outer radius, rf inner ratio.
    static float StarDist(Vector2 p, float r, float rf)
    {
        var k1 = new Vector2(0.809016994f, -0.587785252f);
        var k2 = new Vector2(-k1.x, k1.y);
        p.x = Mathf.Abs(p.x);
        p -= 2f * Mathf.Max(Vector2.Dot(k1, p), 0f) * k1;
        p -= 2f * Mathf.Max(Vector2.Dot(k2, p), 0f) * k2;
        p.x = Mathf.Abs(p.x);
        p.y -= r;
        var ba = rf * new Vector2(-k1.y, k1.x) - new Vector2(0f, 1f);
        float h = Mathf.Clamp(Vector2.Dot(p, ba) / Vector2.Dot(ba, ba), 0f, r);
        return (p - ba * h).magnitude * Mathf.Sign(p.y * ba.x - p.x * ba.y);
    }

    /// <summary>A chunky golden star with a darker outline and a shine. 1 unit wide.</summary>
    public static Sprite Star(int size)
    {
        var t = NewTex(size, size);
        var px = new Color32[size * size];
        var gold = new Color(1f, 0.84f, 0.2f);
        var deep = new Color(1f, 0.6f, 0.05f);
        var edge = new Color(0.85f, 0.42f, 0.02f);
        float s = size;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2((x + 0.5f) / s - 0.5f, (y + 0.5f) / s - 0.52f);
                // rounded star: shrink then grow by a radius
                float d = (StarDist(p, 0.4f, 0.5f) - 0.04f) * s;
                float a = Mathf.Clamp01(0.5f - d);
                float inside = Mathf.Clamp01(-d / (s * 0.06f));
                Color c = Color.Lerp(deep, gold, Mathf.Clamp01(0.55f + p.y * 1.6f - p.x * 0.4f));
                c = Color.Lerp(edge, c, Smooth(0.15f, 0.6f, inside));
                // shine on the upper left
                float sh = 1f - Smooth(0.03f, 0.11f, (p - new Vector2(-0.1f, 0.12f)).magnitude);
                c = Color.Lerp(c, Color.white, sh * 0.65f);
                px[y * size + x] = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(a * 255));
            }
        t.SetPixels32(px);
        return ToSprite(t, size);
    }

    /// <summary>Soft vertical gradient, used behind the menus.</summary>
    public static Sprite Gradient(Color top, Color bottom)
    {
        var t = NewTex(4, 64);
        var px = new Color32[4 * 64];
        for (int y = 0; y < 64; y++)
        {
            Color c = Color.Lerp(bottom, top, y / 63f);
            for (int x = 0; x < 4; x++) px[y * 4 + x] = c;
        }
        t.SetPixels32(px);
        return ToSprite(t, 64);
    }
}
