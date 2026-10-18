using System;
using Awaken.TG.Main.Heroes.CharacterSheet.Items.Bag;
using Awaken.TG.Main.Heroes.CharacterSheet.Items.Equipment;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.UI.ButtonSystem;
using Awaken.TG.MVC;
using UnityEngine;
using TGShared;

namespace QuickSeller;

// Inventory (the Bag tab, and the item list opened from an equipment slot):
//  • a separate "Sell: N" prompt on the same key with the game's hold icon — hold it to sell the hovered item;
//  • the game's own "Drop" prompt stays as it was: a quick tap still drops.
// The Drop prompt fires on key-down, so its action is wrapped: the drop happens only if the key is released
// quickly (a tap). Holding longer means "sell" — releasing early cancels, nothing is dropped.
internal static class InventorySell
{
    private const float TapTime = 0.3f;

    private static Model _owner;                 // BagUI or EquipmentChooseUI
    internal static bool Active => _owner != null;
    private static Func<Item> _hovered;
    private static Prompt _drop;
    private static GAction _origDrop;
    private static PromptSlot _sell;
    private static Item _chooseHovered;
    private static ObjId _shownItem;
    private static float _refreshAt;

    private static bool _pendingDrop;            // the game asked to drop on key-down — decide on release
    private static float _downSince = -1f;
    private static bool _sold;                   // this press already sold something

    internal static void AttachBag(BagUI bag)
    {
        if (bag == null) return;
        var b = bag;
        Attach(bag, bag._promptDrop, bag.Prompts, () => b._hoveredItem);
    }

    internal static void AttachChoose(EquipmentChooseUI ui)
    {
        if (ui == null) return;
        var drop = ui._promptDrop;
        Prompts prompts = null;
        try { var pm = drop?.GenericParentModel; if (pm != null) prompts = Compat.As<Prompts>(pm); } catch { }
        _chooseHovered = null;
        Attach(ui, drop, prompts, () => _chooseHovered);
    }

    internal static void ChooseHovered(EquipmentChooseUI ui, Item item)
    {
        if (_owner != null && ui != null && _owner.Id() == ui.Id()) _chooseHovered = item;
    }

    private static void Attach(Model owner, Prompt drop, Prompts prompts, Func<Item> hovered)
    {
        Detach();
        if (!Plugin.Enabled.Value || !Plugin.InventorySell.Value) return;
        if (drop == null || prompts == null) { Plugin.L.LogWarning($"[ui] {owner?.GameType().Name}: no drop prompt / prompts"); return; }
        _owner = owner; _hovered = hovered; _drop = drop;
        _origDrop = drop._action;
#if IL2CPP
        drop._action = Core.Action(OnDropRequested);
#else
        // readonly in the game's code — set through reflection
        HarmonyLib.AccessTools.Field(typeof(Prompt), nameof(Prompt._action)).SetValue(drop, Core.Action(OnDropRequested));
#endif
        var p = Prompt.Hold(Core.ResolveBinding(), Loc.T("btn_sell"), Core.Action(OnHoldComplete), holdTime: Plugin.InventoryHoldTime.Value);
        var added = Core.AddLike(prompts, p, Core.AsModel(owner), drop, null, out var view);
        if (added != null) _sell = new PromptSlot { Prompt = added, View = view };
        _shownItem = Compat.NoId;
        Core.BindingVk();
        if (Plugin.LogSales.Value) Plugin.L.LogInfo($"[ui] inventory sell attached to {owner.GameType().Name} (drop '{drop._name}', hold {Plugin.InventoryHoldTime.Value}s)");
    }

    private static void Detach()
    {
        _owner = null; _drop = null; _sell = null; _origDrop = null; _hovered = null;
        _pendingDrop = false; _downSince = -1f; _sold = false;
    }

    private static bool Valid()
    {
        try { return _owner != null && _drop != null && !_owner.HasBeenDiscarded && !_drop.HasBeenDiscarded; } catch { return false; }
    }

    private static void RunOrigDrop()
    {
        try { _origDrop?.Invoke(); } catch (Exception e) { Plugin.L.LogError("drop: " + e.Message); }
    }

    private static Item Hovered()
    {
        try { return _hovered?.Invoke(); } catch { return null; }
    }

    // Called by the game instead of "Drop".
    private static void OnDropRequested()
    {
        if (!Valid() || !Plugin.InventorySell.Value) { RunOrigDrop(); return; }
        bool down = Core.KeyDown(Core.BindingVk());
        if (down) { _pendingDrop = true; if (_downSince < 0) _downSince = Time.unscaledTime; return; }
        // fired on release (or by a gamepad): drop only after a quick tap that sold nothing
        float held = _downSince >= 0 ? Time.unscaledTime - _downSince : 0f;
        if (!_sold && held < TapTime) RunOrigDrop();
    }

    // The game's hold prompt finished.
    private static void OnHoldComplete()
    {
        if (!Valid() || _sold) return;
        SellHovered();
    }

    private static void SellHovered()
    {
        _sold = true;
        _pendingDrop = false;
        var item = Hovered();
        if (item == null) return;
        var pos = Popups.Cursor();
        var q = Core.Evaluate(item, true);
        if (q.Why != null) Core.Error(Loc.F("err_cant", q.Why));
        else if (Core.SellFromHero(item, q.Qty, q.Net, out var res)) Popups.Gold(q.Net, pos);
        else { Plugin.L.LogWarning("inventory sell failed: " + res); Core.Error(Loc.T("err_notsold")); }
        _shownItem = Compat.NoId;
    }

    internal static void Tick()
    {
        if (_owner == null) return;
        if (!Valid()) { Detach(); return; }
        try
        {
            bool down = Core.KeyDown(Core.BindingVk());
            float now = Time.unscaledTime;
            if (down)
            {
                if (_downSince < 0) _downSince = now;
                // fallback if the game's hold prompt does not fire on this screen
                if (!_sold && now - _downSince >= Plugin.InventoryHoldTime.Value + 0.15f) SellHovered();
            }
            else if (_downSince >= 0)
            {
                float held = now - _downSince;
                if (_pendingDrop && !_sold && held < TapTime) RunOrigDrop();   // quick tap — the usual drop
                _pendingDrop = false; _downSince = -1f; _sold = false;          // longer and unsold — cancelled
            }
            RefreshLabel(now);
        }
        catch (Exception e) { Plugin.L.LogError("InventorySell.Tick: " + e.Message); }
    }

    private static void RefreshLabel(float now)
    {
        if (_sell == null) return;
        var item = Hovered();
        var ptr = item.Id();
        if (ptr == _shownItem && now < _refreshAt) return;
        _shownItem = ptr; _refreshAt = now + 1f;
        if (item == null) { _sell.Set(Loc.T("btn_sell"), false, true); return; }
        var q = Core.Evaluate(item, true);
        if (q.Why != null) _sell.Set(Loc.T("btn_sell"), false, true);
        else _sell.Set(Loc.F("btn_sell_price", q.Net), true, true);
    }
}
