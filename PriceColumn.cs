using System;
using System.Collections.Generic;
using Awaken.TG.Main.Heroes.Items;
using Awaken.TG.Main.Locations.Containers;
using UnityEngine;
using UnityEngine.UIElements;

namespace QuickSeller;

// On-the-spot price on the right of each loot list row. Rows are reused, so the label is kept per row.
internal static class PriceColumn
{
    private sealed class Row { public Label Label; public VisualElement Parent; public ObjId Item; }
    private static readonly Dictionary<ObjId, Row> Rows = new();
    private static int _errors;

    internal static void Apply(PContainerElement row, Item item)
    {
        if (_errors > 5) return;
        var name = row._nameLabel;
        var parent = name?.parent;
        if (parent == null) return;
        if (!Rows.TryGetValue(row.Id(), out var r) || r.Label == null || r.Label.parent == null)
        {
            if (Rows.Count > 200) Rows.Clear();
            var lbl = new Label();
            lbl.name = "quickseller-price";
            lbl.pickingMode = PickingMode.Ignore;
            var st = lbl.style;
            st.position = Position.Absolute;
            st.right = Plugin.PriceColumnOffset.Value;  // left of the quantity column
            st.top = 0f;
            st.bottom = 0f;
            st.unityTextAlign = TextAnchor.MiddleRight;
            st.color = new Color(0.91f, 0.76f, 0.35f, 1f);
            st.fontSize = 18f;
            parent.Add(lbl);
            r = new Row { Label = lbl, Parent = parent };
            Rows[row.Id()] = r;
        }
        r.Item = item.Id();
        string text = "";
        if (item != null && Plugin.ShowPriceColumn.Value)
        {
            var q = Core.Evaluate(item, false);
            if (q.Why == null && q.Net > 0) text = q.Net.ToString();
        }
        r.Label.style.right = Plugin.PriceColumnOffset.Value;
        r.Label.text = text;
    }

    private static bool Visible(Row r)
    {
        try { return r.Parent != null && r.Parent.panel != null && r.Label != null && r.Label.parent != null; } catch { return false; }
    }

    // Where a "+N" popup for this item should start: its price in the list, else the row, else above the crosshair.
    internal static Vector2 PopupPos(Item item)
    {
        try
        {
            if (item != null)
                foreach (var r in Rows.Values)
                {
                    if (r.Item != item.Id() || !Visible(r)) continue;
                    if (!string.IsNullOrEmpty(r.Label.text) && Popups.ScreenPos(r.Label, out var p)) return p;
                    if (Popups.ScreenPos(r.Parent, out p)) return p;
                }
        }
        catch { }
        return Popups.AboveCrosshair();
    }

    // Top of the visible list (for "Sell all").
    internal static Vector2 PopupPosTop()
    {
        Vector2 best = default; bool found = false;
        try
        {
            foreach (var r in Rows.Values)
            {
                if (!Visible(r) || !Popups.ScreenPos(r.Parent, out var p)) continue;
                if (!found || p.y < best.y) { best = p; found = true; }
            }
        }
        catch { }
        return found ? best : Popups.AboveCrosshair();
    }

    internal static void Fail(Exception e)
    {
        if (++_errors <= 3) Plugin.L.LogWarning("PriceColumn: " + e.Message);
    }
}
