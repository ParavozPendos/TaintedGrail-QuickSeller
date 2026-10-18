using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UIElements;
using TGShared;

namespace QuickSeller;

// Floating "+N coins" numbers (like damage numbers): appear at the sold item, drift up and fade out.
// Error messages: a short line under the crosshair.
internal static class Popups
{
    private sealed class P { public string Text, Sub; public Vector2 Pos; public float T0; public Color C; public float Jitter; }
    private static readonly List<P> Items = new();
    private const float Life = 1.7f, Rise = 70f;

    private static string _msg;
    private static float _msgUntil;
    private static bool _msgBad;

    internal static string Coins(long n) => $"{(n >= 0 ? "+" : "−")}{Math.Abs(n)} {Loc.Plural("coins", Math.Abs(n))}";

    // pos: screen pixels (top-left origin)
    internal static void Gold(long amount, Vector2 pos, string sub = null)
    {
        if (!Plugin.GoldPopups.Value) return;
        if (Items.Count > 12) Items.RemoveAt(0);
        var c = amount >= 0 ? new Color(1f, 0.83f, 0.35f, 1f) : new Color(1f, 0.55f, 0.45f, 1f);
        Items.Add(new P { Text = Coins(amount), Sub = sub, Pos = pos, T0 = Time.unscaledTime, C = c, Jitter = UnityEngine.Random.Range(-14f, 14f) });
    }

    internal static void Message(string text, bool bad = true)
    {
        _msg = text; _msgBad = bad; _msgUntil = Time.unscaledTime + 3.2f;
    }

    // ---- where to spawn ----
    internal static Vector2 AboveCrosshair() => new(Screen.width * 0.5f, Screen.height * 0.44f);

    // Centre-top of a UI Toolkit element, in screen pixels.
    internal static bool ScreenPos(VisualElement ve, out Vector2 pos)
    {
        pos = default;
        try
        {
            if (ve?.panel == null) return false;
            var root = ve.panel.visualTree;
            var pr = root.worldBound;
            var wb = ve.worldBound;
            if (pr.width < 1 || pr.height < 1 || wb.width <= 0 && wb.height <= 0) return false;
            float sx = Screen.width / pr.width, sy = Screen.height / pr.height;
            pos = new Vector2((wb.x + wb.width * 0.5f - pr.x) * sx, (wb.y - pr.y) * sy);
            return pos.x > 0 && pos.y > 0 && pos.x < Screen.width && pos.y < Screen.height;
        }
        catch { return false; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    // Mouse cursor in screen pixels (inventory: the hovered item is under it).
    internal static Vector2 Cursor()
    {
        try
        {
            if (UnityEngine.Cursor.lockState == CursorLockMode.Locked) return AboveCrosshair();
            var h = GetForegroundWindow();
            if (h != IntPtr.Zero && GetCursorPos(out var p) && ScreenToClient(h, ref p) && GetClientRect(h, out var r) && r.R > 0 && r.B > 0)
            {
                float x = p.X * (Screen.width / (float)r.R), y = p.Y * (Screen.height / (float)r.B);
                if (x >= 0 && y >= 0 && x <= Screen.width && y <= Screen.height) return new Vector2(x, y - 24f);
            }
        }
        catch { }
        return AboveCrosshair();
    }

    // ---- drawing (OnGUI) ----
    internal static void Draw()
    {
        float now = Time.unscaledTime;
        Items.RemoveAll(p => now - p.T0 > Life);
        bool msg = _msg != null && now < _msgUntil;
        if (Items.Count == 0 && !msg) return;
        var vs = Ui.Begin(Plugin.UiScale.Value);
        try
        {
            float s = Ui.Scale;
            foreach (var p in Items)
            {
                float t = (now - p.T0) / Life;
                float ease = 1f - (1f - t) * (1f - t);
                float a = t < 0.65f ? 1f : Mathf.Clamp01(1f - (t - 0.65f) / 0.35f);
                float pop = t < 0.12f ? Mathf.Lerp(0.7f, 1.12f, t / 0.12f) : Mathf.Lerp(1.12f, 1f, Mathf.Clamp01((t - 0.12f) / 0.15f));
                int size = Mathf.RoundToInt(26 * pop);
                var st = Ui.Text(size, true, TextAnchor.MiddleCenter);
                float x = p.Pos.x / s + p.Jitter * ease, y = p.Pos.y / s - 18 - Rise * ease;
                var r = new Rect(x - 200, y - 20, 400, 40);
                Outline(r, p.Text, st, new Color(p.C.r, p.C.g, p.C.b, a));
                if (!string.IsNullOrEmpty(p.Sub))
                    Outline(new Rect(r.x, r.y + 26, r.width, 28), p.Sub, Ui.Text(15, false, TextAnchor.MiddleCenter), new Color(0.92f, 0.90f, 0.86f, a));
            }
            if (msg)
            {
                float a = Mathf.Clamp01((_msgUntil - now) / 0.5f);
                var st = Ui.Text(16, false, TextAnchor.MiddleCenter);
                float w = Ui.Width(_msg, st) + 36;
                var r = new Rect(vs.x / 2 - w / 2, vs.y * 0.60f, w, 32);
                var old = GUI.color;
                Ui.Panel(r, 0.78f * a);
                GUI.color = old;
                Ui.Label(r, _msg, st, _msgBad ? new Color(1f, 0.72f, 0.62f, a) : new Color(Ui.TextC.r, Ui.TextC.g, Ui.TextC.b, a));
            }
        }
        finally { Ui.End(); }
    }

    private static void Outline(Rect r, string s, GUIStyle st, Color c)
    {
        var old = GUI.color;
        GUI.color = new Color(0.05f, 0.03f, 0.0f, c.a * 0.9f);
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (dx != 0 || dy != 0) GUI.Label(new Rect(r.x + dx * 1.5f, r.y + dy * 1.5f, r.width, r.height), s, st);
        GUI.color = c;
        GUI.Label(r, s, st);
        GUI.color = old;
    }
}
