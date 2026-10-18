using System;
using System.Reflection;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Combat;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Locations.Actions;
using Awaken.TG.Main.Locations.Shops;
using Awaken.TG.Main.Utility;
using Awaken.TG.MVC;

using UnityEngine;
using TGShared;

namespace QuickSeller;

public class SellerTicker : MonoBehaviour
{
#if IL2CPP
    public SellerTicker(IntPtr ptr) : base(ptr) { }
#endif
    private float _nextUi, _nextWorld;
    private bool _dumped;

    public void Update()
    {
        try
        {
            Loc.Tick(Time.unscaledTime);
            if (!_dumped && Plugin.DumpKeys.Value && Time.unscaledTime > 25f) { _dumped = true; DumpKeys(); }
            if (!Plugin.Enabled.Value) { WorldPrice.Text = null; return; }
            InventorySell.Tick();                         // key hold — every frame
            if (Time.unscaledTime >= _nextUi) { _nextUi = Time.unscaledTime + 0.1f; ContainerSell.Refresh(); }
            if ((Plugin.ShowWorldPrice.Value || Plugin.WorldSell.Value) && Time.unscaledTime >= _nextWorld) { _nextWorld = Time.unscaledTime + 0.12f; WorldPrice.Update(); }
            WorldPrice.TickSell();
            Buyback.Tick();
        }
        catch (Exception e) { Plugin.L.LogError("Ticker: " + e.Message); }
    }

    public void OnGUI()
    {
        try
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            Buyback.Draw();
            if (Plugin.Enabled.Value && Plugin.ShowWorldPrice.Value && WorldPrice.Text != null) WorldPrice.Draw();
            Popups.Draw();
        }
        catch (Exception e) { if (_guiErrors++ < 3) Plugin.L.LogWarning("Ticker.OnGUI: " + e.Message); }
    }
    private static int _guiErrors;

    private static void DumpKeys()
    {
        int n = 0;
        void Walk(Type t, string path)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
            // static properties under Il2CppInterop, static fields in the Mono build
            var members = new System.Collections.Generic.List<(string name, Type type, Func<object> get)>();
            foreach (var p in t.GetProperties(flags)) { var pp = p; members.Add((pp.Name, pp.PropertyType, () => pp.GetValue(null))); }
            foreach (var f in t.GetFields(flags)) { var ff = f; members.Add((ff.Name, ff.FieldType, () => ff.GetValue(null))); }
            foreach (var (name, type, get) in members)
            {
                if (!typeof(KeyBindings).IsAssignableFrom(type)) continue;
                try
                {
                    if (get() is not KeyBindings kb) continue;
                    string action = kb;
                    Plugin.L.LogInfo($"[keys] {path}.{name} action='{action}' key='{Awaken.TG.Main.Utility.UI.RewiredHelper.KeyIdentifierFor(action)}'");
                    n++;
                }
                catch { }
            }
            foreach (var nt in t.GetNestedTypes(BindingFlags.Public)) if (typeof(KeyBindings).IsAssignableFrom(nt)) Walk(nt, path + "." + nt.Name);
        }
        Walk(typeof(KeyBindings), "KeyBindings");
        Plugin.L.LogInfo($"[keys] dumped {n}");
    }
}

// Price of the item you look at (lying in the world: PickItemAction), and selling it with one key.
internal static class WorldPrice
{
    internal static string Text;
    private static string _price, _action;
    private static bool _actionBad;
    private static VCHeroRaycaster _ray;
    private static float _rayAt = -999f;
    private static ObjId _last;
    private static int _errors;
    private static PickItemAction _pick;
    private static Awaken.TG.Main.Heroes.Interactions.IInteractableWithHero _interactable;
    private static int _qty, _lvl;
    private static ItemTemplate _tpl;
    private static Quote _quote;
    private static bool _quoteOk;
    private static float _holdStart = -1f;
    private static bool _done;
    private static bool _checkedTemp;

    internal static void Update()
    {
        if (_errors > 10) { Text = null; return; }
        try
        {
            if (Cursor.lockState != CursorLockMode.Locked || Hero.Current == null || ContainerSell.Active) { Clear(); return; }
            if (Compat.Dead(_ray) ? Time.unscaledTime - _rayAt > 2f : Time.unscaledTime - _rayAt > 10f)
            {
                _rayAt = Time.unscaledTime;
#pragma warning disable CS0618 // the replacement is not available under Il2CppInterop; keep one call for both builds
                var o = UnityEngine.Object.FindObjectOfType(Compat.TypeOf<VCHeroRaycaster>());
#pragma warning restore CS0618
                _ray = o?.TryCast<VCHeroRaycaster>();
            }
            if (_ray == null) { Clear(); return; }
            var act = _ray._action;
            if (act == null) { Clear(); return; }
            if (act.Id() == _last) return;
            Clear();
            _last = act.Id();
            var pick = Compat.As<PickItemAction>(act);
            if (pick == null) return;
            var data = pick._itemSpawningData;
            var tpl = data?.ItemTemplate;
            if (tpl == null) return;
            _pick = pick; _interactable = _ray._interactable; _tpl = tpl;
            _qty = Math.Max(1, data.quantity); _lvl = data.itemLvl;
            Describe(pick);
        }
        catch (Exception e) { if (++_errors <= 3) Plugin.L.LogWarning("WorldPrice: " + e.Message); Clear(); }
    }

    private static void Clear() { Text = null; _price = null; _action = null; _pick = null; _last = default(ObjId); _quoteOk = false; }

    private static int KeyVk()
    {
        var k = (Plugin.WorldSellKey.Value ?? "G").Trim().ToUpperInvariant();
        if (k.Length == 1 && char.IsLetterOrDigit(k[0])) return k[0];
        if (k.Length >= 2 && k[0] == 'F' && int.TryParse(k.Substring(1), out var n)) return 0x6F + n;
        return 'G';
    }

    internal static void TickSell()
    {
        if (!Plugin.WorldSell.Value) return;
        bool down = Cursor.lockState == CursorLockMode.Locked && !ContainerSell.Active && Core.KeyDown(KeyVk());
        if (!down)
        {
            if (_holdStart >= 0 && !_done && _pick != null) Describe(_pick); // released mid-hold — restore the tag
            _holdStart = -1f; _done = false; return;
        }
        if (_done || _pick == null) return;
        if (_holdStart < 0) _holdStart = Time.unscaledTime;
        float hold = Plugin.WorldHoldTime.Value;
        float t = Time.unscaledTime - _holdStart;
        if (hold > 0.01f && t < hold) { _action = Loc.F("progress", Mathf.RoundToInt(t / hold * 100f)); return; }
        _done = true;
        SellPicked();
    }

    // Temporary item (never added to the world) — so the price uses the same formulas as trading.
    private static Item TempItem()
    {
        int before = 0;
        if (!_checkedTemp) try { before = World.ModelsById.Count; } catch { }
        var tmp = new Item(_tpl, _qty, _lvl);
        if (!_checkedTemp)
        {
            _checkedTemp = true;
            try
            {
                int after = World.ModelsById.Count;
                if (after != before) Plugin.L.LogWarning($"[world] temporary price item changed the world model count ({before} -> {after})");
                else if (Plugin.LogSales.Value) Plugin.L.LogInfo("[world] temporary price item is not registered in the world (ok)");
            }
            catch { }
        }
        return tmp;
    }

    private static void Describe(PickItemAction pick)
    {
        try
        {
            _quote = Core.Evaluate(TempItem(), false);
            _quoteOk = _quote.Why == null && _quote.Net > 0;
            bool owned = false;
            try { owned = Plugin.BlockStolen.Value && pick.IsIllegal; } catch { }
            if (owned) { _quoteOk = false; _quote.Why = Loc.T("why_owned"); }
            _price = _quote.Price > 0 ? Loc.F("world_line", _quote.Price) : null;
            if (!Plugin.WorldSell.Value) _action = null;
            else if (_quoteOk) { _action = Loc.F("world_here", _quote.Net); _actionBad = false; }
            else if (_quote.Price > 0) { _action = Loc.F("world_cant", _quote.Why); _actionBad = true; }
            Text = _price ?? _action;
        }
        catch (Exception e)
        {
            if (++_errors <= 3) Plugin.L.LogWarning("WorldPrice.Describe: " + e.Message);
            try { int b = _tpl.BasePrice * _qty; _price = b > 0 ? Loc.F("world_line", "~" + b) : null; Text = _price; } catch { Text = null; }
        }
    }

    private static void SellPicked()
    {
        var pick = _pick; var tpl = _tpl; int qty = _qty;
        try
        {
            var hero = Hero.Current;
            if (hero == null || pick == null || tpl == null) return;
            try { if (Plugin.BlockStolen.Value && pick.IsIllegal) { Core.Error(Loc.F("err_cant", Loc.T("why_owned"))); return; } } catch { return; }
            var q = Core.Evaluate(TempItem(), false);
            if (q.Why != null) { Core.Error(Loc.F("err_cant", q.Why)); return; }
            // what the hero already had of this template
            var before = new System.Collections.Generic.Dictionary<ObjId, int>();
            foreach (var it in Core.HeroItemsOf(tpl)) before[it.Id()] = it.Quantity;
            bool started;
            try { started = pick.StartInteraction(hero, _interactable); }
            catch (Exception e) { Plugin.L.LogWarning("StartInteraction: " + e.Message + " — trying OnStart"); pick.OnStart(hero, _interactable); started = true; }
            if (!started) { Core.Error(Loc.T("err_pickup")); return; }
            // what was added
            Item got = null; int gotQty = 0;
            foreach (var it in Core.HeroItemsOf(tpl))
            {
                int was = before.TryGetValue(it.Id(), out var b) ? b : 0;
                int d = it.Quantity - was;
                if (d > gotQty) { gotQty = d; got = it; }
            }
            if (got == null) { Core.Error(Loc.T("err_notfound")); return; }
            // the picked stack itself must pass the same checks (e.g. it became a quest item on pickup)
            var why = Core.Blocked(got, false);
            if (why != null) { Core.Error(Loc.F("err_cant", why)); return; }
            int sellQty = Math.Min(gotQty, qty);
            int net = (int)Math.Round((double)q.Net * sellQty / Math.Max(1, qty));
            if (net <= 0) { Core.Error(Loc.F("err_cant", Loc.T("why_worthless"))); return; }
            if (Core.SellFromHero(got, sellQty, net, out var res)) Popups.Gold(net, Popups.AboveCrosshair());
            else { Plugin.L.LogWarning("world sell failed: " + res); Core.Error(Loc.T("err_notsold")); }
            Clear();
        }
        catch (Exception e) { Plugin.L.LogError("WorldSell: " + e); Core.Error(Loc.T("err_generic")); }
    }

    // Small tag under the crosshair: price, and "[G] sell here: N".
    internal static void Draw()
    {
        if (_price == null && _action == null) return;
        var vs = Ui.Begin(Plugin.UiScale.Value);
        try
        {
            var st = Ui.Text(15);
            string key = (Plugin.WorldSellKey.Value ?? "G").Trim().ToUpperInvariant();
            bool chip = Plugin.WorldSell.Value && _action != null && !_actionBad && _holdStart < 0;
            float w = 24;
            if (_price != null) w += Ui.Width(_price, st);
            if (_price != null && _action != null) w += 26;
            float chipW = chip ? Mathf.Max(22, Ui.Width(key, Ui.Text(14, true)) + 12) + 8 : 0;
            if (_action != null) w += chipW + Ui.Width(_action, st);
            var r = new Rect(vs.x / 2 - w / 2, vs.y * 0.565f, w, 30);
            Ui.Panel(r, 0.70f);
            float x = r.x + 12;
            if (_price != null) { Ui.Label(new Rect(x, r.y, 600, r.height), _price, st, Ui.Gold); x += Ui.Width(_price, st) + 10; }
            if (_price != null && _action != null) { Ui.Label(new Rect(x - 2, r.y, 20, r.height), "·", st, Ui.Muted); x += 16; }
            if (_action != null)
            {
                if (chip) x += Ui.Chip(x, r.y + 4, 22, key, 14) + 8;
                Ui.Label(new Rect(x, r.y, 600, r.height), _action, st, _actionBad ? Ui.Muted : Ui.TextC);
            }
        }
        finally { Ui.End(); }
    }
}
