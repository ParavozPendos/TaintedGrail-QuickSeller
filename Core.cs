using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Awaken.TG.Main.Heroes;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Locations.Shops;
using Awaken.TG.Main.UI.ButtonSystem;
using Awaken.TG.Main.Utility;
using Awaken.TG.MVC;
#if IL2CPP
using Il2CppInterop.Runtime.InteropTypes;
#endif
using UnityEngine;
using TGShared;

namespace QuickSeller;

// One of our prompts. Name/state are written straight into our view: the game's ChangeName/SetupState
// touch the removed footer view and throw.
internal sealed class PromptSlot
{
    public Prompt Prompt; public VGenericPresenterPrompt View; public string Text; public bool Active = true, Visible = true;

    public void Set(string text, bool active, bool visible)
    {
        if (Prompt == null) return;
        try
        {
            if (text != Text)
            {
                // our own view: write the field and the view; the game's view: let ChangeName do both
                // (setting the field first would make ChangeName think nothing changed)
                if (View != null) { Prompt._name = text; View.SetName(text); } else Prompt.ChangeName(text);
                Text = text;
            }
            if (active != Active || visible != Visible)
            {
                if (View != null) { Prompt._active = active; Prompt._visible = visible; View.SetActive(active); View.SetVisible(visible); }
                else Prompt.SetupState(visible, active);
                Active = active; Visible = visible;
            }
        }
        catch (Exception e) { Plugin.L.LogWarning("PromptSlot.Set: " + e.Message); }
    }
}

internal struct Quote
{
    public Item Item; public string Why; public int Price; public int Net; public int Qty; public string Buyer;
}

// Shared by the loot window, the inventory and the world price tag.
internal static class Core
{
    internal static Shop LastShop;
    private static List<Shop> _shops = new();
    private static float _shopsAt = -999f;

#if IL2CPP
    // game interfaces are separate wrapper classes under Il2CppInterop
    internal static IModel AsModel(Il2CppObjectBase o) => new IModel(o.Pointer);
    internal static IMerchant AsMerchant(Il2CppObjectBase o) => new IMerchant(o.Pointer);
    internal static Awaken.TG.Main.Character.IInventory AsInventory(Il2CppObjectBase o) => new Awaken.TG.Main.Character.IInventory(o.Pointer);
#else
    internal static IModel AsModel(object o) => (IModel)o;
    internal static IMerchant AsMerchant(object o) => (IMerchant)o;
    internal static Awaken.TG.Main.Character.IInventory AsInventory(object o) => (Awaken.TG.Main.Character.IInventory)o;
#endif

    internal static IMerchant HeroM(Hero h) => AsMerchant(h);
    internal static IMerchant ShopM(Shop s) => AsMerchant(s);

    internal static bool Alive(Model m)
    {
        try { return m != null && !m.HasBeenDiscarded; } catch { return false; }
    }

    internal static int Penalty(int price) => (int)Math.Round(price * Mathf.Clamp(Plugin.PenaltyPercent.Value, 0f, 95f) / 100f);

    internal static string SafeName(Item i) { try { return i.DisplayName; } catch { return "?"; } }

    // Why the item can't be sold (null = it can). inventory: also checks equipped/favourite.
    internal static string Blocked(Item item, bool inventory)
    {
        if (item == null) return Loc.T("why_noitem");
        if (!item.IsSellable) return Loc.T("why_notsellable");
        if (item.IsQuestItem) return Loc.T("why_quest");
        var tpl = item.Template;
        if (tpl != null)
        {
            if (Plugin.BlockKeys.Value && tpl.IsKey) return Loc.T("why_key");
            if (Plugin.BlockImportant.Value && tpl.IsImportantItem) return Loc.T("why_important");
        }
        if (IsCurrency(item)) return Loc.T("why_currency");
        if (Plugin.BlockStolen.Value && item.IsStolen) return Loc.T("why_stolen");
        if (inventory)
        {
            if (Plugin.BlockEquipped.Value && item.IsEquipped) return Loc.T("why_equipped");
            if (Plugin.BlockFavourite.Value && item.Favourite) return Loc.T("why_favourite");
        }
        return null;
    }

    // Money-like items (coin purses turn into coins on pickup, cobwebs are a currency, fake items):
    // selling them for a share of their value would just burn money.
    internal static bool IsCurrency(Item item)
    {
        try { if (item.TryGetElement(Compat.TypeOf<Awaken.TG.Main.Heroes.Items.Attachments.ItemToCurrency>()) != null) return true; } catch { }
        try { if (item.TryGetElement(Compat.TypeOf<Awaken.TG.Main.Heroes.Items.Attachments.FakeItem>()) != null) return true; } catch { }
        return IsCurrency(item.Template);
    }

    internal static bool IsCurrency(ItemTemplate tpl)
    {
        if (tpl == null) return false;
        try
        {
            return tpl.GetComponent(Compat.TypeOf<Awaken.TG.Main.Heroes.Items.Attachments.ItemToCurrencySpec>()) != null
                || tpl.GetComponent(Compat.TypeOf<Awaken.TG.Main.Heroes.Items.Attachments.CurrencyAsCraftingIngredientAttachment>()) != null
                || tpl.GetComponent(Compat.TypeOf<Awaken.TG.Main.Heroes.Items.Attachments.FakeItemAttachment>()) != null;
        }
        catch { return false; }
    }

    private static List<Shop> AllShops()
    {
        if (Time.unscaledTime - _shopsAt < 20f) return _shops;
        _shopsAt = Time.unscaledTime;
        var list = new List<Shop>();
        try
        {
            foreach (var m in World.ModelsById.Values)
            {
                var s = m?.TryCast<Shop>();
                if (s != null && Alive(s)) list.Add(s);
            }
        }
        catch (Exception e) { Plugin.L.LogWarning("Shop scan: " + e.Message); }
        _shops = list;
        return list;
    }

    internal static Shop ResolveShop(Item item, Hero hero)
    {
        {
            var last = LastShop;
            if (!Alive(last) && !string.IsNullOrEmpty(Plugin.LastMerchantId.Value))
            {
                try { if (World.ModelsById.TryGetValue(Plugin.LastMerchantId.Value, out var m)) last = m?.TryCast<Shop>(); } catch { }
                if (Alive(last)) LastShop = last;
            }
            if (Alive(last) && last.CanItemBeSoldToTheShop(item)) return last;
        }
        Shop best = null;
        float bestD = float.MaxValue;
        var hp = hero.Coords;
        foreach (var s in AllShops())
        {
            try
            {
                if (!Alive(s) || !s.CanItemBeSoldToTheShop(item)) continue;
                var loc = s.ParentModel;
                float d = loc != null ? (loc.Coords - hp).sqrMagnitude : float.MaxValue / 2;
                if (d < bestD) { bestD = d; best = s; }
            }
            catch { }
        }
        return best;
    }

    internal static string ShopName(Shop s)
    {
        try { var n = s.ParentModel?.DisplayName; if (!string.IsNullOrEmpty(n)) return n; } catch { }
        return "merchant";
    }

    // Buyer used only to quote the price (nothing on it is changed): last/nearest merchant, else the hero.
    internal static IMerchant QuoteBuyer(Item item, Hero hero)
    {
        var shop = ResolveShop(item, hero) ?? AnyShop(hero);
        return shop != null ? ShopM(shop) : HeroM(hero);
    }

    private static Shop AnyShop(Hero hero)
    {
        Shop best = null; float bestD = float.MaxValue; var hp = hero.Coords;
        foreach (var s in AllShops())
        {
            try { var loc = s.ParentModel; float d = loc != null ? (loc.Coords - hp).sqrMagnitude : float.MaxValue / 2; if (d < bestD) { bestD = d; best = s; } } catch { }
        }
        return best;
    }

    internal static Quote Evaluate(Item item, bool inventory)
    {
        var q = new Quote { Item = item };
        q.Why = Blocked(item, inventory);
        if (q.Why != null) return q;
        var hero = Hero.Current;
        if (hero == null) { q.Why = Loc.T("why_notnow"); return q; }
        q.Qty = Math.Max(1, item.Quantity);
        var buyer = QuoteBuyer(item, hero);
        q.Price = TradeUtils.Price(HeroM(hero), buyer, item, q.Qty);
        q.Buyer = Compat.Same(buyer, hero) ? "hero" : "merchant";
        q.Net = q.Price - Penalty(q.Price);
        QuoteNote = $"price {q.Price} (quoted by {q.Buyer})";
        if (q.Net < Plugin.MinPrice.Value || q.Net <= 0) q.Why = Loc.T("why_worthless");
        return q;
    }

    // Sale without a merchant: the item (already in the hero's bag) is removed, then coins are added.
    // Removal goes first and is verified — if it failed, no coins are given and the item stays in the bag.
    internal static string LastQuoteInfo = "";
    internal static string QuoteNote = "";

    internal static bool SellFromHero(Item item, int qty, int net, out string result)
    {
        try { LastQuoteInfo = $"game price {item.Price}x{qty}"; } catch { LastQuoteInfo = ""; }
        var hero = Hero.Current;
        string name = SafeName(item);
        int have = Math.Max(1, item.Quantity);
        qty = Math.Min(qty, have);
        // snapshot for buyback — before removal
        Awaken.TG.Main.Heroes.Items.LootTables.ItemSpawningDataRuntime snap = null;
        try { snap = new Awaken.TG.Main.Heroes.Items.LootTables.ItemSpawningDataRuntime(item); snap.quantity = qty; } catch (Exception e) { Plugin.L.LogWarning("snapshot: " + e.Message); }

        bool removed;
        if (qty < have)
        {
            try { item.ChangeQuantity(-qty); } catch (Exception e) { Plugin.L.LogWarning("ChangeQuantity: " + e.Message); }
            int now;
            try { now = item.HasBeenDiscarded ? 0 : item.Quantity; } catch { now = 0; }
            removed = now <= have - qty;           // verify by the actual quantity, not by the return value
        }
        else
        {
            var inv = hero.Inventory;
            if (inv == null) { result = "no inventory"; return false; }
            AsInventory(inv).Remove(item, true);
            bool gone;
            try { gone = item.HasBeenDiscarded || item.Quantity <= 0; } catch { gone = true; }
            removed = gone || !OwnedByHero(item);
        }
        if (!removed) { result = "removal not confirmed"; return false; }
        hero.Wealth.IncreaseBy(net);
        if (snap != null) Buyback.Record(name, qty, net, snap);
        result = $"+{net}";
        if (Plugin.LogSales.Value) Plugin.L.LogInfo($"[sold] {name} x{qty}: net {net} (penalty {Plugin.PenaltyPercent.Value}%), {LastQuoteInfo}, {QuoteNote}");
        return true;
    }

    private static bool OwnedByHero(Item item)
    {
        try
        {
            foreach (var it in HeroItemsOf(item.Template)) if (it.Id() == item.Id()) return true;
        }
        catch { }
        return false;
    }

    internal static KeyBindings ResolveBinding() => ResolveBinding(Plugin.KeyBinding.Value);

    internal static KeyBindings ResolveBinding(string name)
    {
        name = name?.Trim() ?? "";
        foreach (var t in new[] { typeof(KeyBindings.UI.Items), typeof(KeyBindings.UI.Generic) })
        {
            // Il2CppInterop exposes static fields as properties; the Mono build has the original fields
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (p != null && p.GetValue(null) is KeyBindings kb) return kb;
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (f != null && f.GetValue(null) is KeyBindings kf) return kf;
        }
        return KeyBindings.UI.Items.DropItem;
    }

    // Virtual key code currently bound to the action (from Rewired settings).
    private static int _vk; private static float _vkAt = -999f;
    internal static int BindingVk()
    {
        if (Time.unscaledTime - _vkAt < 5f && _vk != 0) return _vk;
        _vkAt = Time.unscaledTime;
        try
        {
            string action = ResolveBinding();
            string key = Awaken.TG.Main.Utility.UI.RewiredHelper.KeyIdentifierFor(action)?.Trim().ToUpperInvariant() ?? "";
            if (key.Length == 1 && char.IsLetterOrDigit(key[0])) _vk = key[0];
            else if (key.Length >= 2 && key[0] == 'F' && int.TryParse(key.Substring(1), out var n)) _vk = 0x6F + n;
            else _vk = 'R';
            KeyName = key.Length > 0 ? key : "R";
        }
        catch { _vk = 'R'; KeyName = "R"; }
        return _vk;
    }
    internal static string KeyName = "R";

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    private static readonly uint MyPid = (uint)Process.GetCurrentProcess().Id;

    internal static bool KeyDown(int vk)
    {
        if (vk == 0) return false;
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        return pid == MyPid && (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    // ---- add a prompt rendered with the same view type as a neighbouring game prompt ----
    private static readonly Dictionary<string, Type> TypeCache = new();

    private static Type ManagedType(string fullName)
    {
        if (string.IsNullOrEmpty(fullName)) return null;
        if (TypeCache.TryGetValue(fullName, out var t)) return t;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            t = a.GetType(fullName, false);
            if (t == null) t = a.GetType("Il2Cpp" + fullName, false);
            if (t != null) break;
        }
        TypeCache[fullName] = t;
        return t;
    }

    internal static string ViewTypeOf(Prompt p)
    {
        try
        {
            if (p == null) return null;
            if (World.MainViewsByModel.TryGetValue(AsModel(p), out var v) && v != null)
                return v.GameType().FullName;
        }
        catch (Exception e) { Plugin.L.LogWarning("ViewTypeOf: " + e.Message); }
        return null;
    }

    // Add a prompt and render it with the same view type as a neighbouring game prompt.
    // Generic AddPrompt<TPromptView> does not initialize under Il2CppInterop (type initializer throws), so:
    // plain AddPrompt (spawns a view in the screen-wide footer) -> remove that view ->
    // World.SpawnView(prompt, neighbour's view type) — a non-generic call -> mount it into the host.
    // If that is impossible the prompt is removed rather than left in the footer.
    internal static Prompt AddLike(Prompts prompts, Prompt prompt, IModel owner, Prompt sibling, IVisualElementPromptHost host, out VGenericPresenterPrompt view)
    {
        view = null;
        var added = prompts.AddPrompt(prompt, owner, true, true);
        try
        {
            View sib = null, mine = null;
            if (sibling != null) World.MainViewsByModel.TryGetValue(AsModel(sibling), out sib);
            World.MainViewsByModel.TryGetValue(AsModel(added), out mine);
            var sibType = sib?.GameType();
            string mineName = mine?.GameType().FullName, sibName = sibType?.FullName;
            if (mineName == sibName && sibName != null) { Plugin.L.LogInfo($"[ui] prompt view '{mineName}' (same as neighbour)"); return added; }
            if (sibType == null)
            {
                Plugin.L.LogWarning($"[ui] no neighbour prompt view — prompt removed (view '{mineName}')");
                var tmp = added;
                try { prompts.RemovePrompt(ref tmp); } catch (Exception e) { Plugin.L.LogWarning("RemovePrompt: " + e.Message); }
                return null;
            }

            if (mine != null)
            {
                var go = mine.gameObject;
                World.RemoveView(mine);
                if (go != null) UnityEngine.Object.Destroy(go);     // RemoveView does not destroy the object — otherwise a label stays in the footer
            }
            var v = World.SpawnView(AsModel(added), sibType, true, true, null, false);
            var vp = v?.TryCast<VGenericPresenterPrompt>();
            view = vp;
            string placed = "?";
            if (vp != null && vp.Prompt != null)
            {
                if (vp.Prompt.parent == null && host != null) { host.Add(vp.Prompt); placed = "added to host"; }
                else if (vp.Prompt.parent == null)
                {
                    // no explicit host: put it right after the neighbouring prompt, in the same container
                    var sibEl = sib?.TryCast<VGenericPresenterPrompt>()?.Prompt;
                    var parent = sibEl?.parent;
                    if (parent != null) { parent.Insert(parent.IndexOf(sibEl) + 1, vp.Prompt); placed = "next to neighbour"; }
                    else placed = "no host";
                }
                else placed = "already mounted";
            }
            Plugin.L.LogInfo($"[ui] prompt view '{mineName}' -> '{v?.GameType().FullName}' ({placed})");
        }
        catch (Exception e) { Plugin.L.LogWarning("AddLike view swap: " + e.Message); }
        return added;
    }

    // ---- messages ----
    // Errors / "can't sell": under the crosshair and/or the game's own banner. Successful sales only show gold popups.
    internal static void Error(string text)
    {
        if (Plugin.LogSales.Value) Plugin.L.LogInfo("[notify] " + text);
        string style = Plugin.ErrorStyle.Value;
        if (style != "Overlay")
        {
            try { Awaken.TG.Main.Saving.Models.SaveLoadUnavailableInfo.ShowInfo(text); if (style == "Game") return; }
            catch (Exception e) { Plugin.L.LogWarning("Error(game): " + e.Message); }
        }
        Popups.Message(text, true);
    }

    internal static void Info(string text) => Popups.Message(text, false);

    // ---- hero inventory ----
    internal static List<Item> HeroItemsOf(ItemTemplate tpl)
    {
        var res = new List<Item>();
        var hero = Hero.Current;
        if (hero == null || tpl == null) return res;
        var inv = hero.Inventory;
        if (inv == null) return res;
        var items = AsInventory(inv).Items;
        if (items == null) return res;
#if IL2CPP
        var e = items.GetEnumerator();
        var ne = new Il2CppSystem.Collections.IEnumerator(e.Pointer);
        while (ne.MoveNext())
        {
            var it = e.Current;
            try { if (it != null && it.Template != null && it.Template.Id() == tpl.Id()) res.Add(it); } catch { }
        }
#else
        foreach (var it in items)
        {
            try { if (it != null && it.Template != null && Compat.Same(it.Template, tpl)) res.Add(it); } catch { }
        }
#endif
        return res;
    }

    internal static GAction Action(Action a) => Compat.Action(a);
}
