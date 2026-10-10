using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// `fires_ui_dump` - reads the live scene instead of reasoning about it.
    ///
    /// Written because three separate guesses at why the class bars render opaque and why the Classes
    /// window sits under the inventory were all wrong. Sorting and tinting are decided by values that
    /// only exist at runtime, so this prints them: every Canvas with its render mode and order, and the
    /// Image state of a VANILLA hotbar slot next to one of our clones, which is the only comparison that
    /// can say what we changed.
    /// </summary>
    internal static class ClassesUiDump
    {
        private const string CommandName = "fires_ui_dump";

        private static bool _dumped;

        /// <summary>Writes the report to the log once per session, unprompted.</summary>
        public static void DumpOnce(string why)
        {
            if (_dumped) return;
            _dumped = true;
            try { Debug.Log("[FiresRPGClasses] auto ui dump (" + why + ")\n" + Build()); }
            catch (Exception ex) { Debug.LogWarning("[FiresRPGClasses] ui dump failed: " + ex.Message); }
        }

        public static void Register()
        {
            new Terminal.ConsoleCommand(CommandName,
                "Dump every Canvas and the class-bar slot images to the log, for UI diagnosis.",
                args =>
                {
                    string report = Build();
                    Debug.Log(report);
                    args.Context?.AddString("fires_ui_dump written to the log (" + report.Length + " chars)");
                });
        }

        private static string Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== fires_ui_dump ===");
            DumpCanvases(sb);
            DumpSlotImages(sb);
            sb.AppendLine("=== end fires_ui_dump ===");
            return sb.ToString();
        }

        /// <summary>
        /// Every Canvas, sorted by the order they actually draw in. overrideSorting and isRootCanvas are
        /// the two that decide whether sortingOrder means anything at all for a nested canvas.
        /// </summary>
        private static void DumpCanvases(StringBuilder sb)
        {
            Canvas[] all = Resources.FindObjectsOfTypeAll<Canvas>();
            var live = new List<Canvas>();
            foreach (Canvas c in all)
                if (c != null && c.gameObject.scene.IsValid()) live.Add(c);

            live.Sort((a, b) => a.sortingOrder.CompareTo(b.sortingOrder));

            sb.AppendLine("-- canvases (" + live.Count + "), lowest sortingOrder first --");
            foreach (Canvas c in live)
            {
                sb.AppendLine(string.Format(
                    "  order={0,6}  mode={1,-18} override={2,-5} root={3,-5} rootCanvas={4,-20} layer={5,-8} active={6,-5} raycaster={7,-5} {8}",
                    c.sortingOrder, c.renderMode, c.overrideSorting, c.isRootCanvas,
                    c.rootCanvas == null ? "none" : c.rootCanvas.name, c.sortingLayerName,
                    c.gameObject.activeInHierarchy, c.GetComponent<GraphicRaycaster>() != null, Path(c.transform)));
            }

            sb.AppendLine("-- the two that matter --");
            InventoryGui gui = InventoryGui.instance;
            Canvas invCanvas = gui != null ? gui.GetComponentInParent<Canvas>() : null;
            sb.AppendLine("  InventoryGui canvas : " + Describe(invCanvas));
            sb.AppendLine("  InventoryGui visible: " + InventoryGui.IsVisible());

            GameObject panel = GameObject.Find("ClassesPanel");
            sb.AppendLine("  ClassesPanel        : " + (panel == null ? "not built" : Path(panel.transform)));
            if (panel != null)
            {
                sb.AppendLine("    own canvas        : " + Describe(panel.GetComponent<Canvas>()));
                sb.AppendLine("    parent canvas     : " + Describe(panel.GetComponentInParent<Canvas>()));
                sb.AppendLine("    sibling index     : " + panel.transform.GetSiblingIndex()
                    + " of " + (panel.transform.parent == null ? 0 : panel.transform.parent.childCount));

                // Every Canvas on the way up. An ancestor with overrideSorting traps this subtree inside
                // ITS order, and a different rootCanvas from the inventory means the root decides and
                // nothing set below can win - the two shapes that make a raised sortingOrder do nothing.
                sb.AppendLine("    canvas chain upward:");
                for (Transform t = panel.transform; t != null; t = t.parent)
                {
                    Canvas c = t.GetComponent<Canvas>();
                    if (c != null) sb.AppendLine("      " + Describe(c));
                }
            }
        }

        /// <summary>
        /// A vanilla hotbar slot beside one of ours. If the backgrounds differ, the difference is in
        /// these fields - sprite, colour, material, type - and nowhere else.
        /// </summary>
        private static void DumpSlotImages(StringBuilder sb)
        {
            sb.AppendLine("-- slot images --");

            HotkeyBar bar = UnityEngine.Object.FindAnyObjectByType<HotkeyBar>();
            if (bar == null) { sb.AppendLine("  no HotkeyBar in scene"); return; }

            sb.AppendLine("  prefab         : " + (bar.m_elementPrefab == null ? "null" : bar.m_elementPrefab.name)
                + "   elementSpace=" + bar.m_elementSpace);

            if (bar.transform.childCount > 0)
                DumpElement(sb, "  VANILLA live   ", bar.transform.GetChild(0).gameObject);
            else
                sb.AppendLine("  VANILLA live   : the bar has no elements right now (no bound items)");

            if (bar.m_elementPrefab != null)
                DumpElement(sb, "  VANILLA prefab ", bar.m_elementPrefab);

            GameObject mine = GameObject.Find("FiresClassBars");
            if (mine == null) { sb.AppendLine("  OURS           : FiresClassBars not built"); return; }

            Transform front = mine.transform.Find("FrontBar");
            if (front != null && front.childCount > 0)
                DumpElement(sb, "  OURS front[0]  ", front.GetChild(0).gameObject);

            sb.AppendLine("  OURS container : " + Describe(mine.GetComponent<Canvas>())
                + "  parent=" + Path(mine.transform));
        }

        private static void DumpElement(StringBuilder sb, string label, GameObject element)
        {
            sb.AppendLine(label + ": " + element.name
                + "  activeSelf=" + element.activeSelf
                + "  button=" + (element.GetComponent<Button>() != null)
                + "  children=" + element.transform.childCount);

            foreach (Image img in element.GetComponentsInChildren<Image>(true))
            {
                // What renders is Graphic.color x CanvasRenderer.color. A Selectable tints through
                // CrossFadeColor, which writes the CANVAS RENDERER and never touches Graphic.color - so
                // printing only img.color shows a correct value on a visibly wrong slot.
                string rendered = img.canvasRenderer == null ? "n/a" : ColorText(img.canvasRenderer.GetColor());
                sb.AppendLine(string.Format(
                    "      {0,-14} sprite={1,-22} colour={2} renderer={3} type={4,-8} mat={5,-18} enabled={6,-5} active={7,-5} raycast={8}",
                    img.transform == element.transform ? "(root)" : img.name,
                    img.sprite == null ? "NULL" : img.sprite.name,
                    ColorText(img.color), rendered, img.type,
                    img.material == null ? "null" : img.material.name,
                    img.enabled, img.gameObject.activeSelf, img.raycastTarget));
            }
        }

        private static string ColorText(Color c) =>
            string.Format("rgba({0:0.00},{1:0.00},{2:0.00},{3:0.00})", c.r, c.g, c.b, c.a);

        private static string Describe(Canvas c) =>
            c == null
                ? "none"
                : string.Format("{0}  order={1} mode={2} override={3} root={4}",
                    Path(c.transform), c.sortingOrder, c.renderMode, c.overrideSorting, c.isRootCanvas);

        private static string Path(Transform t)
        {
            var parts = new List<string>();
            while (t != null) { parts.Insert(0, t.name); t = t.parent; }
            return string.Join("/", parts.ToArray());
        }
    }
}
