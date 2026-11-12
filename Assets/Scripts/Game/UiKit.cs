using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>Small helpers to build the uGUI screens from code (TextMeshPro text).</summary>
public static class UiKit
{
    public static Sprite Round;
    public static readonly Color Ink = new Color(0.07f, 0.08f, 0.18f, 1f);
    public static readonly Color Panel = new Color(0.12f, 0.14f, 0.28f, 0.94f);
    public static readonly Color Soft = new Color(0.75f, 0.79f, 0.95f, 1f);

    static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

    public static RectTransform Fill(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    public static RectTransform Place(GameObject go, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = Center;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    public static TextMeshProUGUI Text(Transform parent, string text, float size, Color color, Vector2 pos, Vector2 box,
        TextAlignmentOptions align = TextAlignmentOptions.Center, Vector2? anchor = null, bool bold = false, bool outline = false)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        Place(go, parent, anchor ?? Center, pos, box);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.enableAutoSizing = true;
        t.fontSizeMax = size;
        t.fontSizeMin = Mathf.Max(10f, size * 0.4f);
        t.raycastTarget = false;
        t.richText = true;
        if (bold) t.fontStyle = FontStyles.Bold;
        if (outline)
        {
            t.outlineWidth = 0.22f;
            t.outlineColor = new Color32(14, 16, 40, 255);
        }
        return t;
    }

    public static Image Box(Transform parent, Color color, Vector2 pos, Vector2 size, Vector2? anchor = null, bool shadow = false)
    {
        var go = new GameObject("Box", typeof(RectTransform), typeof(Image));
        Place(go, parent, anchor ?? Center, pos, size);
        var img = go.GetComponent<Image>();
        img.sprite = Round;
        img.type = Image.Type.Sliced;
        img.color = color;
        img.raycastTarget = false;
        if (shadow)
        {
            var s = go.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0.05f, 0.45f);
            s.effectDistance = new Vector2(0f, -8f);
        }
        return img;
    }

    public static Image Pic(Transform parent, Sprite sprite, Color color, Vector2 pos, Vector2 size, Vector2? anchor = null)
    {
        var go = new GameObject("Pic", typeof(RectTransform), typeof(Image));
        Place(go, parent, anchor ?? Center, pos, size);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        img.preserveAspect = true;
        return img;
    }

    public static Button Button(Transform parent, string label, Color color, Vector2 pos, Vector2 size, UnityAction onClick,
        float fontSize = 40f, Vector2? anchor = null)
    {
        var go = new GameObject("Button " + label, typeof(RectTransform), typeof(Image), typeof(Button));
        Place(go, parent, anchor ?? Center, pos, size);
        var img = go.GetComponent<Image>();
        img.sprite = Round;
        img.type = Image.Type.Sliced;
        img.color = color;
        var sh = go.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0.05f, 0.5f);
        sh.effectDistance = new Vector2(0f, -7f);
        var b = go.GetComponent<Button>();
        b.targetGraphic = img;
        var cb = b.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        cb.pressedColor = new Color(0.8f, 0.8f, 0.85f, 1f);
        cb.selectedColor = Color.white;
        cb.disabledColor = new Color(0.55f, 0.55f, 0.6f, 0.6f);
        cb.fadeDuration = 0.06f;
        b.colors = cb;
        if (onClick != null) b.onClick.AddListener(onClick);
        var t = Text(go.transform, label, fontSize, Color.white, Vector2.zero, size - new Vector2(28f, 12f), TextAlignmentOptions.Center, null, true);
        t.name = "Label";
        return b;
    }

    public static void SetLabel(Button b, string text)
    {
        var t = b.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.text = text;
    }

    public static string Hex(Color c)
    {
        return "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
