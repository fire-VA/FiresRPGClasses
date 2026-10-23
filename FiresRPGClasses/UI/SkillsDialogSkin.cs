using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using FiresCore.UI.Shell;
using FiresCore.UI.Shell.Kit;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Under the UI override the Classes Skills tab IS the skills page, so this skin lives in RPGClasses. The vanilla SkillsDialog only shows when [6 - UI] SkillsButtonOpensClasses is off (or a
    /// gamepad path calls it directly). Under the override it gets the kit look: a rounded plate, the total/cap in Gold, rounded row
    /// plates, kit fonts and bar colours, and the gamepad selection highlighted. It's a postfix on Setup with no prefab edits, so vanilla
    /// data, input and gamepad behaviour (incl. the 80 px m_spacing scroll math) stay intact. Override OFF: the postfix returns at the top.
    /// </summary>
    [HarmonyPatch]
    internal static class SkillsDialogSkin
    {
        private const string Section = "6 - UI";
        private const string Mark = "FiresSkin";
        private static ConfigEntry<bool> _on;
        private static readonly Color Ink = new Color(0.96f, 0.91f, 0.76f);

        internal static void BindConfig(ConfigFile config)
        {
            if (_on != null) return;
            _on = config.Bind(Section, "Skin vanilla Skills page", false,   // an optional change to the vanilla look, so default OFF
                "With the Fires UI override on, give Valheim's own Skills dialog the Fires look.");
        }

        private static bool Active() { try { return _on != null && _on.Value && FiresShell.OverrideOn; } catch { return false; } }

        [HarmonyPatch(typeof(SkillsDialog), nameof(SkillsDialog.Setup)), HarmonyPostfix]
        private static void AfterSetup(SkillsDialog __instance)
        {
            if (!Active() || __instance == null) return;
            try { Skin(__instance); }
            catch (System.Exception e) { FiresRPGClassesPlugin.Log?.LogWarning($"[Skills] vanilla page skin failed: {e.GetType().Name}: {e.Message}"); }
        }

        private static int _lastSelected = int.MinValue;

        [HarmonyPatch(typeof(SkillsDialog), "Update"), HarmonyPostfix]
        private static void AfterUpdate(SkillsDialog __instance)
        {
            if (__instance == null || __instance.m_selectionIndex == _lastSelected || !Active()) return;
            _lastSelected = __instance.m_selectionIndex;
            PaintSelection(__instance);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Skin(SkillsDialog d)
        {
            Transform root = d.transform;
            int hidden = 0;
            if (root.Find(Mark) == null)
            {
                // the frame: vanilla background images on the dialog root and its direct children (never the list, the bar or buttons)
                foreach (Image img in root.GetComponentsInChildren<Image>(true))
                {
                    Transform t = img.transform;
                    if (t != root && t.parent != root) continue;
                    if (img.GetComponent<Button>() != null || img.GetComponent<Scrollbar>() != null) continue;
                    if (d.m_listRoot != null && (t == d.m_listRoot || t.IsChildOf(d.m_listRoot))) continue;
                    if (d.skillListScrollRect != null && t == d.skillListScrollRect.transform) continue;
                    img.enabled = false; hidden++;
                }
                var plate = new GameObject(Mark, typeof(RectTransform), typeof(Image));
                var prt = (RectTransform)plate.transform;
                prt.SetParent(root, false); prt.SetAsFirstSibling();
                prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = new Vector2(-8f, -8f); prt.offsetMax = new Vector2(8f, 8f);
                var pimg = plate.GetComponent<Image>();
                pimg.sprite = KitSprites.RoundedFill(FiresTokens.Radius); pimg.type = Image.Type.Sliced; pimg.color = FiresTokens.WithAlpha(FiresTokens.Panel, 0.97f); pimg.raycastTarget = true;
                var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image));
                var ert = (RectTransform)edge.transform; ert.SetParent(prt, false); ert.anchorMin = Vector2.zero; ert.anchorMax = Vector2.one; ert.offsetMin = ert.offsetMax = Vector2.zero;
                var eimg = edge.GetComponent<Image>(); eimg.sprite = KitSprites.RoundedOutline(FiresTokens.Radius); eimg.type = Image.Type.Sliced; eimg.color = FiresTokens.Edge; eimg.raycastTarget = false;
            }

            // the dialog's own buttons (Close): rounded kit buttons, Gold text + outline, as the mockup
            foreach (Button btn in root.GetComponentsInChildren<Button>(true))
            {
                if (d.m_listRoot != null && btn.transform.IsChildOf(d.m_listRoot)) continue;
                var bimg = btn.targetGraphic as Image;
                if (bimg == null || btn.transform.Find(Mark) != null) continue;
                bimg.sprite = KitSprites.RoundedFill(FiresTokens.RadiusSmall); bimg.type = Image.Type.Sliced; bimg.color = FiresTokens.Well;
                var bo = new GameObject(Mark, typeof(RectTransform), typeof(Image));
                var bort = (RectTransform)bo.transform; bort.SetParent(btn.transform, false); bort.anchorMin = Vector2.zero; bort.anchorMax = Vector2.one; bort.offsetMin = bort.offsetMax = Vector2.zero;
                var boimg = bo.GetComponent<Image>(); boimg.sprite = KitSprites.RoundedOutline(FiresTokens.RadiusSmall); boimg.type = Image.Type.Sliced; boimg.color = FiresTokens.WithAlpha(FiresTokens.Gold, 0.75f); boimg.raycastTarget = false;
                foreach (TMP_Text bt in btn.GetComponentsInChildren<TMP_Text>(true)) { bt.color = FiresTokens.Gold; Font(bt, FontRole.SerifBold); }
            }

            // header: the total / cap, same values, Gold instead of vanilla orange
            if (d.m_totalSkillText != null)
            {
                d.m_totalSkillText.text = d.m_totalSkillText.text.Replace("<color=orange>", "<color=" + FiresTokens.RichHex(FiresTokens.Gold) + ">");
                d.m_totalSkillText.color = FiresTokens.Text;
                Font(d.m_totalSkillText, FontRole.SerifBold);
            }

            int rows = 0;
            foreach (GameObject row in Elements(d))
            {
                if (row == null) continue;
                rows++;
                if (row.transform.Find(Mark) == null)
                {
                    var bg = new GameObject(Mark, typeof(RectTransform), typeof(Image));
                    var rt = (RectTransform)bg.transform; rt.SetParent(row.transform, false); rt.SetAsFirstSibling();
                    rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(2f, 3f); rt.offsetMax = new Vector2(-2f, -3f);   // pad inside the row; m_spacing stays 80
                    var img = bg.GetComponent<Image>(); img.sprite = KitSprites.RoundedFill(FiresTokens.RadiusSmall); img.type = Image.Type.Sliced; img.color = FiresTokens.PanelRaised; img.raycastTarget = false;
                    var line = new GameObject("Edge", typeof(RectTransform), typeof(Image));
                    var lrt = (RectTransform)line.transform; lrt.SetParent(rt, false); lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = lrt.offsetMax = Vector2.zero;
                    var limg = line.GetComponent<Image>(); limg.sprite = KitSprites.RoundedOutline(FiresTokens.RadiusSmall); limg.type = Image.Type.Sliced; limg.color = FiresTokens.Hairline; limg.raycastTarget = false;
                }
                Text(row, "name", FiresTokens.Text, FontRole.SerifRegular);
                Text(row, "leveltext", FiresTokens.Gold, FontRole.SerifBold);
                Text(row, "bonustext", FiresTokens.Ok, FontRole.SansBold);
                Bar(row, "levelbar_total", FiresTokens.GoldDim);
                Bar(row, "levelbar", FiresTokens.Gold);
                Bar(row, "currentlevel", FiresTokens.GoldBright);
            }
            _lastSelected = int.MinValue;
            PaintSelection(d);
            FiresRPGClassesPlugin.Log?.LogInfo($"[Skills] vanilla page skinned: {rows} rows (override on, gamepad index {d.m_selectionIndex}, {hidden} vanilla frame image(s) hidden)");
        }

        private static IEnumerable<GameObject> Elements(SkillsDialog d)
        {
            var list = Traverse.Create(d).Field("m_elements").GetValue<List<GameObject>>();
            return list ?? new List<GameObject>();
        }

        private static void PaintSelection(SkillsDialog d)
        {
            int i = 0;
            foreach (GameObject row in Elements(d))
            {
                Transform plate = row != null ? row.transform.Find(Mark) : null;
                if (plate == null) { i++; continue; }
                bool sel = i == d.m_selectionIndex;
                var img = plate.GetComponent<Image>(); if (img != null) img.color = sel ? FiresTokens.Selected : FiresTokens.PanelRaised;
                var edge = plate.Find("Edge")?.GetComponent<Image>(); if (edge != null) edge.color = sel ? FiresTokens.GoldBright : FiresTokens.Hairline;
                i++;
            }
        }

        private static void Text(GameObject row, string child, Color color, FontRole role)
        {
            var t = FindDeep(row.transform, child)?.GetComponent<TMP_Text>();
            if (t == null) return;
            t.color = color;
            Font(t, role);
        }

        private static void Font(TMP_Text t, FontRole role)
        {
            TMP_FontAsset f = null;
            try { f = KitText.TmpFont(role); } catch { }
            if (f != null) t.font = f;
        }

        private static void Bar(GameObject row, string child, Color fill)
        {
            var g = FindDeep(row.transform, child)?.GetComponent<GuiBar>();
            if (g == null) return;
            g.SetColor(fill);
            var track = g.GetComponent<Image>();
            Image fillImg = g.m_bar != null ? g.m_bar.GetComponent<Image>() : null;
            if (track != null && track != fillImg) track.color = FiresTokens.Well;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }
    }
}
