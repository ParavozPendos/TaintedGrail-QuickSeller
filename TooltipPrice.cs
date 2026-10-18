using System;
using Awaken.TG.Main.Heroes.Items.Tooltips.Components;
using Awaken.TG.Main.Heroes.Items.Tooltips.Descriptors;

namespace QuickSeller;

// The tooltip price is the item's base value; selling on the spot gives much less.
// While the inventory or a loot window is open, the real on-the-spot price is appended: "10 → 3".
internal static class TooltipPrice
{
    private const string Mark = " <color=#E9C25A>→ ";
    private static int _errors;

    internal static void Apply(ItemTooltipFooterComponent footer, IItemDescriptor descriptor)
    {
        if (!Plugin.Enabled.Value || !Plugin.TooltipPrice.Value || _errors > 5) return;
        if (footer == null || descriptor == null) return;
        if (!InventorySell.Active && !ContainerSell.Active) return;
        if (Compat.As<VendorItemDescriptor>(descriptor) != null) return;          // shop: the game shows real prices there
        var ed = Compat.As<ExistingItemDescriptor>(descriptor);
        var item = ed?.Item;
        if (item == null) return;
        var text = footer.priceText;
        if (text == null) return;
        string cur = text.text ?? "";
        int at = cur.IndexOf(Mark, StringComparison.Ordinal);
        if (at >= 0) cur = cur.Substring(0, at);
        var q = Core.Evaluate(item, InventorySell.Active);
        if (q.Why != null || q.Net <= 0) { if (at >= 0) text.text = cur; return; }
        text.richText = true;
        text.text = cur + Mark + q.Net + "</color>";
    }

    internal static void Fail(Exception e)
    {
        if (++_errors <= 3) Plugin.L.LogWarning("TooltipPrice: " + e.Message);
    }
}
