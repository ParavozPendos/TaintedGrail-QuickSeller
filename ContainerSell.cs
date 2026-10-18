using System;
using System.Collections.Generic;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Locations.Containers;
using Awaken.TG.Main.UI.ButtonSystem;
using Awaken.TG.MVC;
using UnityEngine;
using TGShared;

namespace QuickSeller;

// Loot window: "Sell" (tap R by default) and "Sell all" (hold G 1 s) next to "Take / Take all".
internal static class ContainerSell
{
    private static PContainerUI _presenter;
    private static ContainerUI _container;
    private static PromptSlot _sell, _sellAll;
    private static ObjId _item;
    private static float _confirmUntil;
    private static ObjId _confirmItem;
    private static bool _busy;

    internal static bool Active => _container != null;

    internal static void Attach(PContainerUI presenter)
    {
        _presenter = presenter;
        _container = presenter.TargetModel;
        _sell = null; _sellAll = null; _item = default(ObjId);
        if (!Plugin.Enabled.Value || _container == null || presenter._prompts == null) return;
        var owner = Core.AsModel(_container);

        float hold = Plugin.ContainerHoldTime.Value;
        var p = hold <= 0.01f
            ? Prompt.Tap(Core.ResolveBinding(), Loc.T("btn_sell"), Core.Action(OnSell))
            : Prompt.Hold(Core.ResolveBinding(), Loc.T("btn_sell"), Core.Action(OnSell), holdTime: hold);
        var added = Core.AddLike(presenter._prompts, p, owner, presenter._takePrompt, presenter.ViewPromptHost, out var view);
        if (added != null) _sell = new PromptSlot { Prompt = added, View = view };

        if (Plugin.SellAll.Value)
        {
            var pa = Prompt.Hold(Core.ResolveBinding(Plugin.SellAllBinding.Value), Loc.T("btn_sellall"), Core.Action(OnSellAll), holdTime: Plugin.SellAllHoldTime.Value);
            var addedAll = Core.AddLike(presenter._prompts, pa, owner, presenter._takePrompt, presenter.ViewPromptHost, out var viewAll);
            if (addedAll != null) _sellAll = new PromptSlot { Prompt = addedAll, View = viewAll };
        }
        Refresh(true);
    }

    internal static void OnDiscard(ContainerUI c)
    {
        if (_container != null && _container.Id() == c.Id()) { _container = null; _sell = null; _sellAll = null; _presenter = null; }
    }

    private static bool Illegal()
    {
        try { return Plugin.BlockStolen.Value && _container.IsIllegal; } catch { return true; }
    }

    internal static void Refresh(bool force = false)
    {
        if (_container == null || (_sell == null && _sellAll == null)) return;
        try
        {
            if (!Core.Alive(_container)) { _container = null; _sell = null; _sellAll = null; return; }
            bool illegal = Illegal();
            bool empty;
            try { empty = _container.IsEmpty; } catch { empty = false; }
            _sellAll?.Set(Loc.T("btn_sellall"), true, !illegal && !empty);
            if (_sell == null) return;
            if (illegal) { _sell.Set(Loc.T("btn_sell"), false, false); return; }
            var cur = _container.CurrentItem?.Item;
            var ptr = cur.Id();
            if (ptr != _confirmItem) _confirmUntil = 0;
            if (!force && ptr == _item && _confirmUntil == 0 && _sell.Text != null) return;
            _item = ptr;
            if (cur == null) { _sell.Set(Loc.T("btn_sell"), true, true); return; }
            var q = Core.Evaluate(cur, false);
            if (q.Why != null) { _sell.Set(Loc.T("btn_sell"), true, true); return; }
            if (_confirmUntil > Time.unscaledTime) { _sell.Set(Loc.T("btn_confirm"), true, true); return; }
            _confirmUntil = 0;
            _sell.Set(Plugin.ShowPriceOnButton.Value ? Loc.F("btn_sell_price", q.Net) : Loc.T("btn_sell"), true, true);
        }
        catch (Exception e) { Plugin.L.LogError("ContainerSell.Refresh: " + e.Message); }
    }

    // Take one list entry and sell it. Returns coins earned (0 = nothing sold); error = message for the player.
    private static int SellElement(ContainerElement el, out string error)
    {
        error = null;
        var item = el?.Item;
        if (item == null) { error = Loc.F("err_cant", Loc.T("why_noitem")); return 0; }
        var q = Core.Evaluate(item, false);
        if (q.Why != null) { error = Loc.F("err_cant", q.Why); return 0; }
        int qty = q.Qty; // read BEFORE taking: the taken stack may merge with the hero's stack
        var taken = _container.TakeItemFromContainer(el, true, false);
        if (taken == null) { error = Loc.T("err_take"); return -1; }
        int sellQty = Math.Min(qty, Math.Max(1, taken.Quantity));
        if (Core.SellFromHero(taken, sellQty, q.Net, out var res)) return q.Net;
        Plugin.L.LogWarning("Sell failed after take: " + res);
        error = Loc.T("err_notsold");
        return -1;
    }

    private static void OnSell()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (_container == null || !Core.Alive(_container) || Illegal()) return;
            var el = _container.CurrentItem;
            var item = el?.Item;
            if (item == null) return;
            if (Plugin.RequireDoublePress.Value && !(_confirmUntil > Time.unscaledTime && _confirmItem == item.Id()))
            {
                var q0 = Core.Evaluate(item, false);
                if (q0.Why != null) { Core.Error(Loc.F("err_cant", q0.Why)); return; }
                _confirmItem = item.Id();
                _confirmUntil = Time.unscaledTime + Mathf.Max(0.5f, Plugin.ConfirmTimeout.Value);
                Refresh(true);
                return;
            }
            _confirmUntil = 0;
            var pos = PriceColumn.PopupPos(item);
            int got = SellElement(el, out var err);
            if (got > 0) Popups.Gold(got, pos);
            else if (err != null) Core.Error(err);
            _item = default(ObjId);
        }
        catch (Exception e) { Plugin.L.LogError("ContainerSell: " + e); Core.Error(Loc.T("err_generic")); }
        finally { _busy = false; }
    }

    private static void OnSellAll()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (_container == null || !Core.Alive(_container) || Illegal()) return;
            var pos = PriceColumn.PopupPosTop();
            // snapshot of the list: taking items changes it
            var elements = new List<ContainerElement>();
            var all = _container.AllElements();
            if (all != null)
                for (int i = 0; i < all.Count; i++)
                {
                    var ce = all[i]?.TryCast<ContainerElement>();
                    if (ce != null) elements.Add(ce);
                }
            int total = 0, sold = 0, kept = 0;
            string fail = null;
            foreach (var el in elements)
            {
                if (_container == null || !Core.Alive(_container)) break; // emptied container may close itself
                if (!Core.Alive(el)) continue;
                Item item;
                try { item = el.Item; } catch { continue; }
                if (item == null) continue;
                int got = SellElement(el, out var err);
                if (got > 0) { total += got; sold++; }
                else if (got < 0) { fail = err; break; }   // something went wrong — stop, nothing else is touched
                else kept++;
            }
            if (Plugin.LogSales.Value) Plugin.L.LogInfo($"[sell all] sold {sold}, kept {kept}, total {total}{(fail != null ? ", stopped: " + fail : "")}");
            if (total > 0) Popups.Gold(total, pos, kept > 0 ? Loc.F("sellall_kept", kept) : null);
            if (fail != null) Core.Error(fail);
            else if (sold == 0) Core.Error(Loc.T("sellall_none"));
            _item = default(ObjId);
        }
        catch (Exception e) { Plugin.L.LogError("ContainerSell.SellAll: " + e); Core.Error(Loc.T("err_generic")); }
        finally { _busy = false; }
    }
}
