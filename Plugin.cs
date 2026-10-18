using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
#if IL2CPP
using BepInEx.Unity.IL2CPP;
#endif
using HarmonyLib;
using Awaken.TG.Main.Heroes.CharacterSheet.Items.Bag;
using Awaken.TG.Main.Locations.Containers;
using Awaken.TG.Main.Locations.Shops;
using TGShared;

namespace QuickSeller;

// QuickSeller: sell items on the spot, for a share of the merchant price.
//  • loot window (chests, corpses): tap the Drop key (R) — sell the selected item; hold G — sell everything;
//  • inventory: short R — drop as usual, hold R — sell;
//  • world: look at an item lying around to see its price, press G — pick up and sell it.
// No merchant is involved: the price is quoted from the last/nearest merchant (read-only), minus the penalty.
[BepInPlugin(Guid, "QuickSeller", Version)]
public class Plugin :
#if IL2CPP
    BasePlugin
#else
    BaseUnityPlugin
#endif
{
    public const string Guid = "leon.tg.quickseller";
    public const string Version = "1.0.0";

    internal static ManualLogSource L;
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<string> Language;
    internal static ConfigEntry<float> PenaltyPercent;
    internal static ConfigEntry<float> ContainerHoldTime;
    internal static ConfigEntry<string> KeyBinding;
    internal static ConfigEntry<bool> SellAll;
    internal static ConfigEntry<string> SellAllBinding;
    internal static ConfigEntry<float> SellAllHoldTime;
    internal static ConfigEntry<bool> RequireDoublePress;
    internal static ConfigEntry<float> ConfirmTimeout;
    internal static ConfigEntry<bool> ShowPriceOnButton;
    internal static ConfigEntry<bool> ShowPriceColumn;
    internal static ConfigEntry<float> PriceColumnOffset;
    internal static ConfigEntry<bool> InventorySell;
    internal static ConfigEntry<float> InventoryHoldTime;
    internal static ConfigEntry<bool> TooltipPrice;
    internal static ConfigEntry<bool> ShowConsole;
    internal static ConfigEntry<bool> ShowWorldPrice;
    internal static ConfigEntry<bool> WorldSell;
    internal static ConfigEntry<string> WorldSellKey;
    internal static ConfigEntry<float> WorldHoldTime;
    internal static ConfigEntry<bool> GoldPopups;
    internal static ConfigEntry<string> ErrorStyle;
    internal static ConfigEntry<float> UiScale;
    internal static ConfigEntry<bool> BuybackEnabled;
    internal static ConfigEntry<string> BuybackKey;
    internal static ConfigEntry<int> BuybackSize;
    internal static ConfigEntry<bool> BlockKeys;
    internal static ConfigEntry<bool> BlockImportant;
    internal static ConfigEntry<bool> BlockStolen;
    internal static ConfigEntry<bool> BlockEquipped;
    internal static ConfigEntry<bool> BlockFavourite;
    internal static ConfigEntry<int> MinPrice;
    internal static ConfigEntry<string> LastMerchantId;
    internal static ConfigEntry<bool> LogSales;
    internal static ConfigEntry<bool> DumpKeys;

#if IL2CPP
    public override void Load() => Init();
#else
    private void Awake() => Init();
    private ManualLogSource Log => Logger;
    private static T AddComponent<T>() where T : UnityEngine.Component => Compat.Spawn<T>("QuickSeller");
#endif

    private void Init()
    {
        L = Log;
        Enabled = Config.Bind("General", "Enabled", true, "Master switch | Главный выключатель");
        Language = Config.Bind("General", "Language", "auto",
            "UI language: auto = same as the game, or en, ru, de, fr, es, it, pl, cs, pt, ja, ko, zh-Hans, zh-Hant, uk | Язык: auto = как в игре");
        ShowConsole = Config.Bind("General", "ShowConsole", false,
            "Show the BepInEx console window (logs still go to BepInEx\\LogOutput.log) | Показывать окно консоли BepInEx (лог всё равно пишется в файл)");
        PenaltyPercent = Config.Bind("General", "SellPenaltyPercent", 50f,
            new ConfigDescription("Penalty to the normal sell price, %. 50 = you get half of what a merchant would pay | Штраф к обычной цене продажи, %", new AcceptableValueRange<float>(0f, 95f)));

        KeyBinding = Config.Bind("Loot", "SellAction", "DropItem",
            "Game action used to sell (key and icon follow the game's key bindings; DropItem = R): DropItem, FavouriteItem, SortItems, FilterItems, SecondaryAction | Игровое действие для продажи");
        ContainerHoldTime = Config.Bind("Loot", "SellHoldTime", 0f,
            new ConfigDescription("Seconds to hold the sell key in the loot window; 0 = a simple tap | Сколько секунд держать клавишу в окне лута; 0 = просто нажатие", new AcceptableValueRange<float>(0f, 5f)));
        SellAll = Config.Bind("Loot", "SellAll", true, "\"Sell all\" button in the loot window (protected items are skipped) | Кнопка «Продать всё» в окне лута");
        SellAllBinding = Config.Bind("Loot", "SellAllAction", "FavouriteItem", "Game action for \"Sell all\" (FavouriteItem = G) | Игровое действие для «Продать всё»");
        SellAllHoldTime = Config.Bind("Loot", "SellAllHoldTime", 1.0f, new ConfigDescription("Seconds to hold \"Sell all\" | Сколько секунд держать «Продать всё»", new AcceptableValueRange<float>(0.2f, 5f)));
        RequireDoublePress = Config.Bind("Loot", "RequireDoublePress", false, "Extra confirmation: sell only on the second press within ConfirmTimeout seconds | Подтверждение вторым нажатием");
        ConfirmTimeout = Config.Bind("Loot", "ConfirmTimeout", 3f, "Seconds to wait for the confirmation | Сколько секунд ждать подтверждения");
        ShowPriceOnButton = Config.Bind("Loot", "ShowPriceOnButton", false, "Show the price on the button (\"Sell for N\") | Писать цену на кнопке");
        ShowPriceColumn = Config.Bind("Loot", "ShowPriceColumn", true, "On-the-spot price on the right of each row in the loot list | Цена продажи справа в строках списка лута");
        PriceColumnOffset = Config.Bind("Loot", "PriceColumnOffset", 75f, new ConfigDescription("Price offset from the right edge of the row, px | Отступ цены от правого края строки, px", new AcceptableValueRange<float>(0f, 400f)));

        InventorySell = Config.Bind("Inventory", "Enabled", true, "Sell from the inventory by holding the Drop key (a short press still drops) | Продажа из инвентаря удержанием клавиши «Выбросить»");
        TooltipPrice = Config.Bind("Inventory", "TooltipPrice", true, "Add the real on-the-spot price to the item tooltip: \"10 → 3\" | Дописывать в подсказку предмета реальную цену продажи на месте");
        InventoryHoldTime = Config.Bind("Inventory", "HoldTime", 1.0f, new ConfigDescription("Seconds to hold in the inventory | Сколько секунд держать в инвентаре", new AcceptableValueRange<float>(0.5f, 10f)));

        ShowWorldPrice = Config.Bind("World", "ShowWorldPrice", true, "Show the price of the item you look at | Показывать цену предмета, на который смотришь");
        WorldSell = Config.Bind("World", "WorldSell", true, "Sell a lying item with WorldSellKey (picked up normally, then sold) | Продавать лежащий предмет клавишей");
        WorldSellKey = Config.Bind("World", "WorldSellKey", "G", "Key for selling from the ground (letter/digit/F1..F12). R is taken by the weapon toggle | Клавиша продажи с земли");
        WorldHoldTime = Config.Bind("World", "WorldHoldTime", 0f, new ConfigDescription("Seconds to hold; 0 = a simple press | Сколько секунд держать; 0 = просто нажатие", new AcceptableValueRange<float>(0f, 5f)));

        GoldPopups = Config.Bind("Notifications", "GoldPopups", true, "Floating \"+N coins\" above the list after a sale | Всплывающие «+N монет» после продажи");
        ErrorStyle = Config.Bind("Notifications", "ErrorStyle", "Overlay",
            new ConfigDescription("Where to show \"can't sell\" messages: Overlay = under the crosshair, Game = the game's own banner, Both | Где показывать ошибки",
                new AcceptableValueList<string>("Overlay", "Game", "Both")));
        UiScale = Config.Bind("Notifications", "UiScale", 1.0f, new ConfigDescription("Size of popups and lists (scales with resolution automatically) | Размер надписей", new AcceptableValueRange<float>(0.6f, 2.5f)));

        BuybackEnabled = Config.Bind("Buyback", "Enabled", true, "Recent sales can be bought back for the same price (until you quit or load a save) | Выкуп недавно проданного за ту же сумму");
        BuybackKey = Config.Bind("Buyback", "Key", "U", "Tap — open/close the recent sales list (Up/Down select, Right/Enter buy back, Esc close); hold 0.6 s — buy back the last sale | Коротко — открыть/закрыть список (↑↓ выбор, →/Enter выкупить, Esc закрыть); держать — выкупить последнюю продажу");
        BuybackSize = Config.Bind("Buyback", "Size", 25, new ConfigDescription("How many recent sales to remember | Сколько продаж помнить", new AcceptableValueRange<int>(1, 50)));

        BlockKeys = Config.Bind("Safety", "BlockKeys", true, "Never sell keys | Не продавать ключи");
        BlockImportant = Config.Bind("Safety", "BlockImportant", true, "Never sell items the game marks as important | Не продавать важные предметы");
        BlockStolen = Config.Bind("Safety", "BlockStolen", true, "Never sell stolen items; no selling in owned containers or while pickpocketing | Не продавать краденое");
        BlockEquipped = Config.Bind("Safety", "BlockEquipped", true, "Never sell equipped items (inventory) | Не продавать надетое");
        BlockFavourite = Config.Bind("Safety", "BlockFavourite", true, "Never sell favourites (inventory) | Не продавать избранное");
        MinPrice = Config.Bind("Safety", "MinPrice", 1, "Do not sell if the final price is lower | Не продавать дешевле");
        LastMerchantId = Config.Bind("State", "LastMerchantId", "", "Internal: last merchant used for price quotes (filled automatically) | Служебное");
        LogSales = Config.Bind("Debug", "LogSales", false, "Write sales to the log | Писать продажи в лог");
        DumpKeys = Config.Bind("Debug", "DumpKeys", false, "Dump all game actions and their keys to the log | Выписать в лог все действия игры");

        Loc.Table = Strings.Build();
        Loc.Forced = () => Language.Value;
        Loc.Log = s => L.LogInfo(s);

        ConsoleWindow.Apply(ShowConsole.Value);
        ShowConsole.SettingChanged += (object sender, EventArgs args) => ConsoleWindow.Apply(ShowConsole.Value);
        try { new Harmony(Guid).PatchAll(typeof(Patches)); }
        catch (Exception e) { L.LogError("Patching failed, mod is inactive: " + e); return; }
        AddComponent<SellerTicker>();
        L.LogInfo($"QuickSeller {Version} ({Compat.Runtime}): penalty {PenaltyPercent.Value}%, loot {(ContainerHoldTime.Value <= 0.01f ? "tap" : ContainerHoldTime.Value + "s")}, sell all {SellAll.Value}, inventory hold {InventoryHoldTime.Value}s, world [{WorldSellKey.Value}]");
    }
}

[HarmonyPatch]
internal static class Patches
{
    [HarmonyPostfix, HarmonyPatch(typeof(Shop), nameof(Shop.OpenShop))]
    private static void OpenShop(Shop __instance)
    {
        try
        {
            Core.LastShop = __instance;
            var id = __instance.ID;
            if (!string.IsNullOrEmpty(id) && id != Plugin.LastMerchantId.Value) Plugin.LastMerchantId.Value = id;
        }
        catch (Exception e) { Plugin.L.LogError("OpenShop: " + e.Message); }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(PContainerUI), nameof(PContainerUI.CreatePrompts))]
    private static void CreatePrompts(PContainerUI __instance)
    {
        try { ContainerSell.Attach(__instance); }
        catch (Exception e) { Plugin.L.LogError("ContainerSell.Attach: " + e); }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(ContainerUI), nameof(ContainerUI.OnDiscard))]
    private static void Discard(ContainerUI __instance)
    {
        try { ContainerSell.OnDiscard(__instance); } catch { }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(PContainerElement), nameof(PContainerElement.SetData))]
    private static void RowData(PContainerElement __instance, Awaken.TG.Main.Heroes.Items.Item item)
    {
        try { PriceColumn.Apply(__instance, item); }
        catch (Exception e) { PriceColumn.Fail(e); }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(BagUI), nameof(BagUI.AfterViewSpawned))]
    private static void BagSpawned(BagUI __instance)
    {
        try { InventorySell.AttachBag(__instance); }
        catch (Exception e) { Plugin.L.LogError("InventorySell.AttachBag: " + e); }
    }

    // Item list opened from an equipment slot (Equipment tab → slot) — has its own Drop prompt.
    [HarmonyPostfix, HarmonyPatch(typeof(Awaken.TG.Main.Heroes.CharacterSheet.Items.Equipment.EquipmentChooseUI), nameof(Awaken.TG.Main.Heroes.CharacterSheet.Items.Equipment.EquipmentChooseUI.AddPrompts))]
    private static void ChoosePrompts(Awaken.TG.Main.Heroes.CharacterSheet.Items.Equipment.EquipmentChooseUI __instance)
    {
        try { InventorySell.AttachChoose(__instance); }
        catch (Exception e) { Plugin.L.LogError("InventorySell.AttachChoose: " + e); }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(Awaken.TG.Main.Heroes.CharacterSheet.Items.Equipment.EquipmentChooseUI), nameof(Awaken.TG.Main.Heroes.CharacterSheet.Items.Equipment.EquipmentChooseUI.HoveredItemsChanged))]
    private static void ChooseHover(Awaken.TG.Main.Heroes.CharacterSheet.Items.Equipment.EquipmentChooseUI __instance, Awaken.TG.Main.Heroes.Items.Item item)
    {
        try { InventorySell.ChooseHovered(__instance, item); } catch { }
    }

    // Item tooltip: "10" (the game's base value) → "10 → 3" (what you get here and now).
    [HarmonyPostfix, HarmonyPatch(typeof(Awaken.TG.Main.Heroes.Items.Tooltips.Components.ItemTooltipFooterComponent), nameof(Awaken.TG.Main.Heroes.Items.Tooltips.Components.ItemTooltipFooterComponent.SetupCounters))]
    private static void TooltipCounters(Awaken.TG.Main.Heroes.Items.Tooltips.Components.ItemTooltipFooterComponent __instance, Awaken.TG.Main.Heroes.Items.Tooltips.Descriptors.IItemDescriptor descriptor)
    {
        try { TooltipPrice.Apply(__instance, descriptor); } catch (Exception e) { TooltipPrice.Fail(e); }
    }

    // Esc while the recent-sales list is open closes the list, not opens the pause menu.
    [HarmonyPostfix, HarmonyPatch(typeof(Awaken.TG.Main.UI.Menu.MenuUI), nameof(Awaken.TG.Main.UI.Menu.MenuUI.OnInitialize))]
    private static void MenuOpened(Awaken.TG.Main.UI.Menu.MenuUI __instance)
    {
        try { Buyback.OnPauseMenu(__instance); } catch { }
    }
}
