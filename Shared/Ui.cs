using System;
using UnityEngine;

namespace TGShared;

// Small IMGUI kit: dark rounded panels, gold accents, shadowed text, key "chips".
// Everything is laid out in a virtual 1080p space and scaled to the real screen.
internal static class Ui
{
    internal static readonly Color Gold = new(0.93f, 0.78f, 0.42f, 1f);
    internal static readonly Color GoldDim = new(0.80f, 0.66f, 0.38f, 0.85f);
    internal static readonly Color TextC = new(0.90f, 0.89f, 0.86f, 1f);
    internal static readonly Color Muted = new(0.62f, 0.61f, 0.58f, 1f);
    internal static readonly Color On = new(0.55f, 0.90f, 0.55f, 1f);
    internal static readonly Color Off = new(0.55f, 0.53f, 0.50f, 1f);
    internal static readonly Color Bad = new(1f, 0.45f, 0.40f, 1f);

    private static Texture2D _round, _roundSmall;
    private static GUIStyle _panel, _chip;
    private static readonly System.Collections.Generic.Dictionary<int, GUIStyle> _text = new();
    private static Font _cjkFont;
    private static bool _cjkTried;
    private static bool _lastCjk;
    internal static float Scale = 1f;

    internal static float AutoScale(float userScale)
    {
        float s = Mathf.Max(0.5f, Screen.height / 1080f);
        return s * Mathf.Clamp(userScale <= 0 ? 1f : userScale, 0.5f, 3f);
    }

    // Begin drawing in virtual space; returns virtual screen size.
    internal static Vector2 Begin(float userScale)
    {
        Scale = AutoScale(userScale);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Scale, Scale, 1f));
        if (_lastCjk != Loc.IsCjk) { _lastCjk = Loc.IsCjk; _text.Clear(); _panel = null; _chip = null; }
        return new Vector2(Screen.width / Scale, Screen.height / Scale);
    }

    internal static void End() => GUI.matrix = Matrix4x4.identity;

    private static Texture2D RoundTex(int size, int radius, bool border)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.hideFlags = HideFlags.HideAndDontSave;
        t.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                float a = Mathf.Clamp01(radius - d + 0.5f);
                // 1px light rim, darker body
                bool rim = border && (d > radius - 1.5f || x == 0 || y == 0 || x == size - 1 || y == size - 1) && a > 0;
                t.SetPixel(x, y, rim ? new Color(1f, 1f, 1f, a) : new Color(0.62f, 0.62f, 0.62f, a));
            }
        t.Apply();
        return t;
    }

    private static Font CjkFont()
    {
        if (_cjkTried) return _cjkFont;
        _cjkTried = true;
        try
        {
            _cjkFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Microsoft JhengHei UI", "Yu Gothic UI", "Meiryo UI", "Malgun Gothic", "MS Gothic", "SimSun", "Arial Unicode MS" }, 16);
            if (_cjkFont != null) _cjkFont.hideFlags = HideFlags.HideAndDontSave;
        }
        catch { _cjkFont = null; }
        return _cjkFont;
    }

    internal static GUIStyle Text(int size, bool bold = false, TextAnchor anchor = TextAnchor.MiddleLeft)
    {
        size = Mathf.Clamp(size, 8, 60);
        int key = size * 100 + (bold ? 50 : 0) + (int)anchor;
        if (_text.TryGetValue(key, out var st) && st != null) return st;
        st = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = anchor, richText = false, wordWrap = false, clipping = TextClipping.Overflow };
        st.padding = new RectOffset(0, 0, 0, 0);
        st.margin = new RectOffset(0, 0, 0, 0);
        if (bold) st.fontStyle = FontStyle.Bold;
        if (Loc.IsCjk) { var f = CjkFont(); if (f != null) st.font = f; }
        st.normal.textColor = Color.white;
        _text[key] = st;
        return st;
    }

    internal static float Width(string s, GUIStyle st)
    {
        try { return st.CalcSize(new GUIContent(s)).x; } catch { return s.Length * st.fontSize * 0.55f; }
    }

    // Text with a soft 1px shadow.
    internal static void Label(Rect r, string s, GUIStyle st, Color c)
    {
        var old = GUI.color;
        GUI.color = new Color(0, 0, 0, c.a * 0.85f);
        GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), s, st);
        GUI.color = c;
        GUI.Label(r, s, st);
        GUI.color = old;
    }

    internal static void Panel(Rect r, float alpha = 0.90f)
    {
        if (_round == null) _round = RoundTex(32, 9, true);
        if (_panel == null)
        {
            _panel = new GUIStyle();
            _panel.normal.background = _round;
            _panel.border = new RectOffset(10, 10, 10, 10);
        }
        var old = GUI.color;
        GUI.color = new Color(0.10f, 0.095f, 0.09f, alpha);
        GUI.Box(r, GUIContent.none, _panel);
        GUI.color = old;
    }

    internal static void Rect(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    // Key chip: [F10]
    internal static float Chip(float x, float y, float h, string key, int size)
    {
        if (_roundSmall == null) _roundSmall = RoundTex(16, 4, true);
        if (_chip == null) { _chip = new GUIStyle(); _chip.normal.background = _roundSmall; _chip.border = new RectOffset(5, 5, 5, 5); }
        var st = Text(size, true, TextAnchor.MiddleCenter);
        float w = Mathf.Max(h, Width(key, st) + 12);
        var old = GUI.color;
        GUI.color = new Color(0.30f, 0.27f, 0.22f, 0.95f);
        GUI.Box(new Rect(x, y, w, h), GUIContent.none, _chip);
        GUI.color = old;
        Label(new Rect(x, y, w, h), key, st, Gold);
        return w;
    }

    // "[key] text   [key] text" footer; returns total width.
    internal static float Hints(float x, float y, float h, int size, params (string key, string text)[] items)
    {
        float cx = x;
        var st = Text(size);
        foreach (var (k, t) in items)
        {
            cx += Chip(cx, y, h, k, size - 1) + 6;
            Label(new Rect(cx, y, 400, h), t, st, Muted);
            cx += Width(t, st) + 16;
        }
        return cx - x;
    }

    internal static float HintsWidth(int size, params (string key, string text)[] items)
    {
        float w = 0;
        var st = Text(size);
        var sb = Text(size - 1, true);
        foreach (var (k, t) in items) w += Mathf.Max(0, Width(k, sb) + 12) + 6 + Width(t, st) + 16;
        return w;
    }
}
