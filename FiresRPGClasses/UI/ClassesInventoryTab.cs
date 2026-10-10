using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// Puts Classes in the inventory's Crafting tab row, so opening the inventory reaches the screen
    /// without going through another UI.
    ///
    /// <para>Registered against VAInventory's <c>InventoryTabs</c> by REFLECTION, so VAInventory stays
    /// optional - without it this is a no-op and the standalone window (default K) is unaffected.
    /// Registration can happen at Awake: VAInventory replays registrations the next time the inventory
    /// opens, so nothing waits on <c>InventoryGui</c> and load order does not matter.</para>
    ///
    /// <para>Contract: <c>Tools\VAINVENTORY_TAB_API.md</c>. This deliberately does NOT hide
    /// <c>Crafting/RecipeList</c> or <c>Crafting/Decription</c>, cross-hook the sibling buttons, or
    /// re-parent anything - <c>InventoryTabs</c> owns all of that, and doing it twice is how two mods each
    /// end up believing they own the panel.</para>
    /// </summary>
    internal static class ClassesInventoryTab
    {
        private const string TabsType = "VerdantsAscent.Managers.InventoryTabs";
        private const string TabId = "firesrpgclasses.classes";
        private const string TabLabel = "Classes";
        /// <summary>Lower sorts left. VAInventory ships Character at 10 and Backpack at 20.</summary>
        private const int TabOrder = 30;
        // VAInventory's Character tab (Managers\StatsPanel.cs), taken over by id: same label, same place in the row.
        private const string CharacterTabId = "vainventory.character";
        private const string CharacterTabLabel = "Character";
        private const int CharacterTabOrder = 10;

        private static readonly ClassesPrefabView View = new ClassesPrefabView();
        private static bool _registered;

        /// <summary>
        /// Idempotent, and called TWICE on purpose: once from plugin Awake, then again from
        /// <see cref="ClassesPanel"/>.Start with <paramref name="finalAttempt"/> set.
        ///
        /// <para>The Awake call alone was not enough. `AccessTools.TypeByName` only sees assemblies that are
        /// already loaded, and BepInEx loaded us BEFORE VAInventory (log line 218 against 280), so the lookup
        /// returned null and the tab never existed. The soft dependency on the plugin fixes the ordinary case;
        /// this retry means load order cannot hide the tab even if that attribute is ever dropped. Start is the
        /// right second point because every plugin Awake has run by then, and the component hosting it is only
        /// added on a client, so nothing new is patched or run headless.</para>
        /// </summary>
        internal static void Register(bool finalAttempt = false)
        {
            if (_registered) return;

            try
            {
                Type tabs = AccessTools.TypeByName(TabsType);
                if (tabs == null)
                {
                    // Saying nothing here is what let a non-registering tab pass verification by two sessions and
                    // the user: the log held zero [Classes] lines, so there was nothing to notice. Absence of a
                    // complaint is not evidence it worked.
                    if (finalAttempt)
                        FiresRPGClassesPlugin.Log?.LogInfo($"[Classes] {TabsType} not found, so no inventory tab this "
                            + "session - VAInventory is not installed or did not load. The standalone window is unaffected.");
                    return;
                }

                _registered = RegisterAsTab(tabs) || RegisterByArgs(tabs);
                if (_registered) FiresRPGClassesPlugin.Log?.LogInfo("[Classes] registered the Classes tab in the inventory.");
                if (_registered) TakeOverCharacterTab(tabs);
            }
            catch (Exception ex)
            {
                FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] could not register the inventory tab "
                    + $"({ex.GetType().Name}: {ex.Message}). The standalone window still works.");
            }
        }

        /// <summary>
        /// The Tab-object form, which is the only one carrying OnVisibility - and the boards need it, or
        /// they show whatever they held when the inventory was last opened.
        ///
        /// <para><c>Tab</c> IS publicly accessible - a direct <c>new InventoryTabs.Tab { ... }</c> would
        /// compile. Reflection here is a choice, so VAInventory stays a soft dependency rather than a hard
        /// one. Its members are public instance FIELDS, hence Activator plus SetValue.</para>
        ///
        /// <para>Worth knowing if you check this yourself: <c>Type.IsPublic</c> is FALSE for every nested
        /// type no matter how accessible it is. <c>IsNestedPublic</c> / <c>IsVisible</c> is the property
        /// that answers the question.</para>
        /// </summary>
        private static bool RegisterAsTab(Type tabs)
        {
            Type tabType = tabs.GetNestedType("Tab", BindingFlags.Public | BindingFlags.NonPublic);
            if (tabType == null) return false;

            MethodInfo register = tabs.GetMethod("Register", new[] { tabType });
            if (register == null) return false;

            object tab = Activator.CreateInstance(tabType, nonPublic: true);
            if (tab == null) return false;

            if (!Set(tabType, tab, "Id", TabId)) return false;
            if (!Set(tabType, tab, "Label", TabLabel)) return false;
            if (!Set(tabType, tab, "Build", (Action<RectTransform>)Build)) return false;
            // A LAUNCHER (VAInventory 1.0.35+, where Open wins over Build): the tab opens the full Classes window on the tree,
            // not a view clamped into the 570x650 crafting panel (Fire, 2026-09-29). Older VAInventory keeps the inline view.
            if (Set(tabType, tab, "Open", (Action)OpenFullWindow))
                FiresRPGClassesPlugin.Log?.LogInfo("[Classes] the inventory tab opens the full Classes window.");
            // Optional extras: a missing one costs ordering or refresh-on-open, not the tab itself.
            Set(tabType, tab, "IsAvailable", (Func<bool>)IsAvailable);
            Set(tabType, tab, "OnVisibility", (Action<bool>)OnVisibility);
            Set(tabType, tab, "Order", TabOrder);

            register.Invoke(null, new[] { tab });
            return true;
        }

        /// <summary>
        /// VAInventory's own Character tab shows a stats list clamped into the crafting panel. With Classes installed it opens
        /// the full Classes window on its Character board instead (Fire, popup 2026-09-29). InventoryTabs.Register replaces a
        /// tab by Id, so this re-registers VAInventory's id, label and slot as a launcher; without Classes, VAInventory's tab
        /// stays as it is. Needs the launcher form (VAInventory 1.0.35+); an older VAInventory keeps its tab.
        /// </summary>
        private static void TakeOverCharacterTab(Type tabs)
        {
            try
            {
                Type tabType = tabs.GetNestedType("Tab", BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo register = tabType != null ? tabs.GetMethod("Register", new[] { tabType }) : null;
                if (register == null || tabType.GetField("Open", BindingFlags.Public | BindingFlags.Instance) == null) return;

                object tab = Activator.CreateInstance(tabType, nonPublic: true);
                if (!Set(tabType, tab, "Id", CharacterTabId) || !Set(tabType, tab, "Label", CharacterTabLabel)
                    || !Set(tabType, tab, "Open", (Action)OpenCharacterBoard)) return;
                Set(tabType, tab, "Order", CharacterTabOrder);
                register.Invoke(null, new[] { tab });
                FiresRPGClassesPlugin.Log?.LogInfo("[Classes] the inventory's Character tab opens the Classes window's Character board.");
            }
            catch (Exception ex)
            {
                FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] could not take over the Character tab ({ex.GetType().Name}: {ex.Message}); "
                    + "VAInventory's own stays.");
            }
        }

        private static void OpenCharacterBoard()
        {
            try
            {
                if (ClassesPanel.Instance != null) ClassesPanel.Instance.Open(ClassesTab.Character);
            }
            catch (Exception ex)
            {
                FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] the Character tab could not open the Classes window "
                    + $"({ex.GetType().Name}: {ex.Message}).");
            }
        }

        /// <summary>The flat overload, for a VAInventory whose Tab shape has moved. No OnVisibility, so the
        /// boards refresh only when something else asks them to.</summary>
        private static bool RegisterByArgs(Type tabs)
        {
            MethodInfo register = tabs.GetMethod("Register", new[]
            {
                typeof(string), typeof(string), typeof(Action<RectTransform>), typeof(Func<bool>)
            });
            if (register == null)
            {
                FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] {TabsType} is present but exposes neither Register(Tab) nor "
                    + "Register(string, string, Action<RectTransform>, Func<bool>) - VAInventory is older than "
                    + "1.0.26, so the inventory tab is skipped.");
                return false;
            }

            register.Invoke(null, new object[]
            {
                TabId, TabLabel, (Action<RectTransform>)Build, (Func<bool>)IsAvailable
            });
            FiresRPGClassesPlugin.Log?.LogInfo("[Classes] used the flat Register overload; the boards will not refresh on "
                + "each open because that form carries no OnVisibility.");
            return true;
        }

        private static bool Set(Type tabType, object tab, string field, object value)
        {
            FieldInfo info = tabType.GetField(field, BindingFlags.Public | BindingFlags.Instance);
            if (info == null) return false;
            info.SetValue(tab, value);
            return true;
        }

        private static void OpenFullWindow()
        {
            try
            {
                if (ClassesPanel.Instance != null) ClassesPanel.Instance.Open(ClassesTab.Talents);
            }
            catch (Exception ex)
            {
                FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] the inventory tab could not open the Classes window "
                    + $"({ex.GetType().Name}: {ex.Message}).");
            }
        }

        private static void OnVisibility(bool shown)
        {
            try { View.OnVisibility(shown); }
            catch (Exception ex)
            {
                FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] the inventory tab threw on show/hide "
                    + $"({ex.GetType().Name}: {ex.Message}).");
            }
        }

        /// <summary>No bundle means no prefab to mount, and VAInventory leaves an unavailable tab out of
        /// the row entirely - better than a tab that opens onto nothing.</summary>
        private static bool IsAvailable() => ClassesUiBundle.PanelPrefab != null;

        /// <summary>Called ONCE with an empty, full-size rect. A throw here is caught and logged by
        /// VAInventory and will not take the inventory down, but it is handled anyway so the reason lands
        /// in our own log next to everything else about this screen.</summary>
        private static void Build(RectTransform parent)
        {
            try
            {
                if (!View.TryBuild(parent))
                    FiresRPGClassesPlugin.Log?.LogWarning("[Classes] the inventory tab could not build its view; "
                        + "the standalone window is unaffected.");
            }
            catch (Exception ex)
            {
                FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] the inventory tab threw while building "
                    + $"({ex.GetType().Name}: {ex.Message}).");
            }
        }
    }
}
