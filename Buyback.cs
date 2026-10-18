using System;
using System.Collections.Generic;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Heroes.Items.LootTables;
using UnityEngine;
using TGShared;

namespace QuickSeller;

// Recent sales: items sold on the spot can be bought back for the same price.
// A snapshot of the item is kept (ItemSpawningDataRuntime: template, level, upgrades, quantity);
// buying back recreates it (ItemUtils.AddSingleItem). Lives until the game is closed or a save is loaded.
// The list stays open until closed: U (tap) or Esc. Up/Down select, Right/Enter buy back.
internal static class Buyback
{
    private sealed class Entry { public string Name; public int Qty; public int Paid; public ItemSpawningDataRuntime Snap; }
    private static readonly List<Entry> Entries = new();
    internal static bool Open;
    private static int _sel;
    private static float _scroll;
    private static float _downAt;
    private static bool _fired, _prevKey;
    private static ObjId _heroPtr;
    private static Awaken.TG.Main.UI.Menu.MenuUI _closeMenu;
    private static float _escClosedAt = -9f;
    private const int Visible = 10;

    internal static void Record(string name, int qty, int paid, ItemSpawningDataRuntime snap)
    {
        if (!Plugin.BuybackEnabled.Value) return;
        Entries.Insert(0, new Entry { Name = name, Qty = qty, Paid = paid, Snap = snap });
        while (Entries.Count > Plugin.BuybackSize.Value) Entries.RemoveAt(Entries.Count - 1);
        _sel = 0;
    }

    private static int Vk(string k)
    {
        k = (k ?? "U").Trim().ToUpperInvariant();
        if (k.Length == 1 && char.IsLetterOrDigit(k[0])) return k[0];
        if (k.Length >= 2 && k[0] == 'F' && int.TryParse(k.Substring(1), out var n)) return 0x6F + n;
        return 'U';
    }

    // Pause menu opened while our list was open → that Esc was meant for us: close the menu next frame.
    internal static void OnPauseMenu(Awaken.TG.Main.UI.Menu.MenuUI menu)
    {
        if (!Open && Time.unscaledTime - _escClosedAt > 0.5f) return;
        Open = false;
        _escClosedAt = -9f;
        _closeMenu = menu;
    }

    private static readonly Dictionary<int, bool> Prev = new();
    private static readonly Dictionary<int, float> RepeatAt = new();
    private static bool Pressed(int vk, bool repeat = false)
    {
        bool down = Core.KeyDown(vk);
        Prev.TryGetValue(vk, out var was);
        Prev[vk] = down;
        if (down && !was) { RepeatAt[vk] = Time.unscaledTime + 0.4f; return true; }
        if (repeat && down && RepeatAt.TryGetValue(vk, out var t) && Time.unscaledTime >= t) { RepeatAt[vk] = Time.unscaledTime + 0.08f; return true; }
        return false;
    }

    internal static void Tick()
    {
        if (_closeMenu != null)
        {
            var m = _closeMenu; _closeMenu = null;
            try { if (!m.HasBeenDiscarded) m.Close(); } catch (Exception e) { Plugin.L.LogWarning("close pause menu: " + e.Message); }
        }
        if (!Plugin.BuybackEnabled.Value) { Open = false; return; }
        var hero = Hero.Current;
        // new hero / loaded save — the list is cleared (snapshots belong to the previous world session)
        var hp = hero.Id();
        if (hp != _heroPtr) { _heroPtr = hp; Entries.Clear(); Open = false; }
        if (hero == null) return;
        bool gameplay = Cursor.lockState == CursorLockMode.Locked;

        // U: tap — open/close; hold 0.6 s — buy back the most recent sale
        bool k = gameplay && Core.KeyDown(Vk(Plugin.BuybackKey.Value));
        if (k)
        {
            if (!_prevKey) { _downAt = Time.unscaledTime; _fired = false; }
            if (!_fired && Time.unscaledTime - _downAt >= 0.6f)
            {
                _fired = true;
                if (Entries.Count > 0) BuyBack(0); else Core.Error(Loc.T("bb_empty"));
            }
        }
        else if (_prevKey && !_fired) { Open = !Open; _sel = 0; _scroll = 0; }
        _prevKey = k;

        if (!gameplay) { if (Open) Open = false; return; }   // a game menu took over
        if (!Open) return;
        if (Pressed(0x1B)) { Open = false; _escClosedAt = Time.unscaledTime; return; }         // Esc (the pause menu, if it opens, is closed in OnPauseMenu)
        if (Entries.Count == 0) return;
        if (Pressed(0x26, true)) _sel = Math.Max(0, _sel - 1);                        // Up
        if (Pressed(0x28, true)) _sel = Math.Min(Entries.Count - 1, _sel + 1);         // Down
        if (Pressed(0x27) | Pressed(0x0D)) BuyBack(Math.Clamp(_sel, 0, Entries.Count - 1)); // Right / Enter
        _sel = Math.Clamp(_sel, 0, Math.Max(0, Entries.Count - 1));
    }

    private static void BuyBack(int idx)
    {
        if (idx < 0 || idx >= Entries.Count) return;
        var e = Entries[idx];
        try
        {
            var hero = Hero.Current;
            var w = hero.Wealth;
            if (w.ModifiedValue < e.Paid) { Core.Error(Loc.F("bb_nomoney", e.Paid)); return; }
            var inv = Core.AsInventory(hero.Inventory);
            var item = ItemUtils.AddSingleItem(inv, e.Snap);
            if (item == null) { Core.Error(Loc.T("bb_fail")); return; }
            w.DecreaseBy(e.Paid);
            Entries.RemoveAt(idx);
            _sel = Math.Clamp(_sel, 0, Math.Max(0, Entries.Count - 1));
            Popups.Gold(-e.Paid, Popups.AboveCrosshair(), Loc.F("bb_done", e.Qty > 1 ? $"{e.Name} ×{e.Qty}" : e.Name));
            if (Plugin.LogSales.Value) Plugin.L.LogInfo($"[buyback] {e.Name} x{e.Qty} for {e.Paid}");
        }
        catch (Exception ex) { Plugin.L.LogError("Buyback: " + ex); Core.Error(Loc.T("bb_err")); }
    }

    internal static void Draw()
    {
        if (!Open) return;
        var vs = Ui.Begin(Plugin.UiScale.Value);
        try
        {
            const float w = 500, lh = 27, pad = 16;
            int rows = Math.Max(1, Math.Min(Visible, Entries.Count));
            float listH = rows * lh;
            float h = 48 + listH + 12 + 34;
            var r = new Rect(36, Mathf.Max(80, vs.y * 0.24f), w, h);
            Ui.Panel(r, 0.95f);
            Ui.Label(new Rect(r.x + pad, r.y + 8, w, 28), Loc.T("bb_title"), Ui.Text(18, true), Ui.Gold);
            if (Entries.Count > 0) Ui.Label(new Rect(r.x + w - pad - 120, r.y + 8, 120, 28), $"{Math.Min(_sel + 1, Entries.Count)} / {Entries.Count}", Ui.Text(13, false, TextAnchor.MiddleRight), Ui.Muted);
            float y = r.y + 40;
            Ui.Rect(new Rect(r.x + pad, y, w - pad * 2, 1), Ui.GoldDim);
            y += 4;
            var st = Ui.Text(15);
            var stR = Ui.Text(15, true, TextAnchor.MiddleRight);
            if (Entries.Count == 0) { Ui.Label(new Rect(r.x + pad, y, w, lh), Loc.T("bb_empty"), st, Ui.Muted); y += lh; }
            else
            {
                if (_sel < _scroll) _scroll = _sel;
                if (_sel >= _scroll + Visible) _scroll = _sel - Visible + 1;
                int first = (int)_scroll;
                long money = 0;
                try { money = (long)Hero.Current.Wealth.ModifiedValue; } catch { }
                for (int i = first; i < Math.Min(Entries.Count, first + Visible); i++)
                {
                    var e = Entries[i];
                    bool sel = i == _sel;
                    if (sel) { Ui.Rect(new Rect(r.x + 8, y + 1, w - 16, lh - 2), new Color(Ui.Gold.r, Ui.Gold.g, Ui.Gold.b, 0.13f)); Ui.Rect(new Rect(r.x + 8, y + 1, 3, lh - 2), Ui.Gold); }
                    string name = e.Qty > 1 ? $"{e.Name} ×{e.Qty}" : e.Name;
                    Ui.Label(new Rect(r.x + pad + 6, y, w - 150, lh), name, st, sel ? Color.white : Ui.TextC);
                    Ui.Label(new Rect(r.x + w - pad - 120, y, 120, lh), e.Paid.ToString(), stR, e.Paid > money ? Ui.Bad : Ui.Gold);
                    y += lh;
                }
                // scrollbar
                if (Entries.Count > Visible)
                {
                    float top = r.y + 44, trackH = listH;
                    Ui.Rect(new Rect(r.x + w - 7, top, 3, trackH), new Color(1, 1, 1, 0.06f));
                    float th = Mathf.Max(20, trackH * Visible / Entries.Count);
                    Ui.Rect(new Rect(r.x + w - 7, top + (trackH - th) * (_scroll / Mathf.Max(1, Entries.Count - Visible)), 3, th), Ui.GoldDim);
                }
                y = r.y + 44 + listH;
            }
            y += 10;
            Ui.Hints(r.x + pad, y, 24, 13,
                ("↑↓", Loc.T("bb_h_select")),
                ("→ / Enter", Loc.T("bb_h_buy")),
                ($"Esc / {(Plugin.BuybackKey.Value ?? "U").Trim().ToUpperInvariant()}", Loc.T("bb_h_close")));
        }
        finally { Ui.End(); }
    }
}
