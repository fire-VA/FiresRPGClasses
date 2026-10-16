using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using FiresCore.UI.Shell;
using UnityEngine;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// UI overhaul step 2a ([classes-ui], plan Tools\ui_study_seneal\CLASSES_MIGRATION.md): the Classes window as a Tab module of
    /// Core's Fires hub, with its content unchanged. The pages ClassesPanel builds once are re-parented into the shell's content area
    /// while hosted and handed back to the old window otherwise, so the views are never built twice and a live toggle works.
    /// Hosted only while Core's [UI] Fires UI override AND [UI] Classes in hub are both on. Off = today's window, byte for byte.
    /// </summary>
    internal sealed class ClassesHub : FiresShellModuleBase
    {
        public const string ModuleId = "rpgclasses.classes";
        private static ConfigEntry<bool> _inHub;
        private static ClassesHub _instance;
        private static IShellHandle _handle;

        internal static bool Hosted => _inHub != null && _inHub.Value && FiresShell.OverrideOn && _instance != null;

        public override string Id => ModuleId;
        public override string Title => "Classes";
        public override int Order => 10;
        // 0.1.151 (Core 0.2.498): compact hub chrome, and lay the content out at the Classes page's own size (old page 1440x860
        // plus margins), scaled to fit as one piece; was ~0.66 in a height-bound content area (Fire: unreadable).
        public override bool Dense => true;
        public override Vector2 MinContentSize => new Vector2(1524f, 890f);

        private RectTransform _content;
        private IShellContext _ctx;
        private bool _selecting;

        // Sub-view ids, in the order of the mockup's row; "appearance" launches the Fires Character Creator.
        private static readonly (string id, ClassesTab tab, string title)[] Subs =
        {
            ("character", ClassesTab.Character, "Character"),
            ("talents", ClassesTab.Talents, "Talents"),
            ("passives", ClassesTab.Passives, "Passives"),
            ("ultimates", ClassesTab.Ultimates, "Ultimates"),
            ("summons", ClassesTab.Summons, "Summons"),
            ("bars", ClassesTab.Bars, "Bars"),
            ("paragon", ClassesTab.Paragon, "Paragon"),
            ("class", ClassesTab.Class, "Class"),
            ("skills", ClassesTab.Skills, "Skills"),
            ("achievements", ClassesTab.Achievements, "Achievements"),
        };

        internal static string SubId(ClassesTab tab) { foreach (var s in Subs) if (s.tab == tab) return s.id; return "talents"; }
        private static bool TryTab(string id, out ClassesTab tab)
        {
            foreach (var s in Subs) if (string.Equals(s.id, id, StringComparison.OrdinalIgnoreCase)) { tab = s.tab; return true; }
            tab = ClassesTab.Talents; return false;
        }

        internal static void BindConfig(ConfigFile config)
        {
            _inHub = config.Bind("UI", "Classes in hub", true,
                "With FiresUnifiedCore's [UI] Fires UI override on, the Classes window is a tab of the Fires hub (default: follows the override). Set false to keep the old Classes window even with the override on. Override off: always the old window.");
            _inHub.SettingChanged += (_, __) => OnModeChanged();
        }

        /// <summary>Client only, after ClassesPanel exists. Safe before the hub exists (Core queues it).</summary>
        internal static void Register()
        {
            if (_instance != null) return;
            _instance = new ClassesHub();
            try
            {
                _handle = FiresShell.Register(_instance);
                FiresShell.OverrideChanged += _ => OnModeChanged();
                FiresRPGClassesPlugin.Log?.LogDebug($"[ClassesHub] registered '{ModuleId}' (in hub {_inHub?.Value}, override {FiresShell.OverrideOn}, hosted {Hosted})");
            }
            catch (Exception e) { FiresRPGClassesPlugin.Log?.LogWarning("[ClassesHub] not registered: " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>A live flip of either key: close whatever is open; the next open takes the right path. Pages go home.</summary>
        private static void OnModeChanged()
        {
            bool hosted = Hosted;
            FiresRPGClassesPlugin.Log?.LogDebug($"[ClassesHub] mode changed: hosted {hosted}; closing the open window");
            try { if (FiresShell.IsOpen) FiresShell.Close(); } catch { }
            ClassesPanel.Instance?.Close();
            _instance?.ReturnPages();
            _handle?.SetVisible(hosted);
        }

        public override void BuildTab(RectTransform content, IShellContext ctx)
        {
            _content = content;
            _ctx = ctx;
            var tabs = new List<(string id, string title, bool badge)>();
            foreach (var s in Subs) tabs.Add((s.id, s.title, false));
            tabs.Add(("appearance", "Appearance", false));
            ctx.SetSubTabs(tabs.ToArray(), OnSubSelected);
            ctx.SetFooterHints(("Tab", "next tab"), ("Q / E", "sub-view"), ("Click", "learn"), ("Esc", "close"));
            FiresRPGClassesPlugin.Log?.LogDebug($"[ClassesHub] BuildTab: content {content.rect.width:0}x{content.rect.height:0}, {tabs.Count} sub-tabs");
        }

        public override void OnShow(string subView)
        {
            var panel = ClassesPanel.Instance;
            if (panel == null || _content == null) return;
            panel.AdoptPages(_content);
            ClassesTab tab = TryTab(subView, out var t) ? t : panel.CurrentTab;
            ShowTab(tab);
        }

        public override void OnHide()
        {
            // 0.1.156: the editor hosted over the hub goes with it (nothing it made outlives the hub)
            if (ClassesPanel.AppearanceEditor.IsOpen()) { FiresRPGClassesPlugin.Log?.LogInfo("[ClassesHub] hub hides: closing the Fires Character Creator first"); ClassesPanel.AppearanceEditor.Close(); }
            ReturnPages();
        }

        // 0.1.151: the hub window is drag-resizable (Core 0.2.498); re-fit the hosted pages to the new content size
        public override void OnResize(Vector2 size) { if (_content != null) ClassesPanel.Instance?.RefitHub(_content); }

        /// <summary>Esc: an open talent card (IClassesOverlay) closes first; true = consumed, the hub stays open.</summary>
        public override bool OnEscape()
        {
            // 0.1.156: Esc closes the hosted Character Creator first and returns to the hub page under it
            if (ClassesPanel.AppearanceEditor.IsOpen()) { FiresRPGClassesPlugin.Log?.LogInfo("[ClassesHub] Esc: Fires Character Creator closed, hub stays"); ClassesPanel.AppearanceEditor.Close(); return true; }
            return ClassesPanel.Instance != null && ClassesPanel.Instance.CloseOverlayHosted();
        }

        private void OnSubSelected(string id)
        {
            if (_selecting) return;
            if (id == "appearance")
            {
                // 0.1.156 (Fire hand test: double open): the hub no longer Close()s (that restored the inventory under FCC).
                // FCC 1.0.19+ sees FiresShell.IsOpen and is hosted on top; the hub keeps input/hotbars/Esc. The sub-tab row
                // goes back to the page under the editor, so a second click is a fresh (idempotent) open request.
                bool already = ClassesPanel.AppearanceEditor.IsOpen();
                FiresRPGClassesPlugin.Log?.LogInfo($"[ClassesHub] Appearance: open Fires Character Creator over the hub (already open={already})");
                ClassesPanel.AppearanceEditor.Open();
                var panel = ClassesPanel.Instance;
                if (panel != null) { _selecting = true; try { _ctx?.SelectSubTab(SubId(panel.CurrentTab)); } finally { _selecting = false; } }
                return;
            }
            // 0.1.160 ([classes-ui], folded by [wishbone]; Core 0.2.521: ONE hub Achievements tab over every catalogue): the Classes "Achievements" sub-tab hands off to it,
            // pre-set to "valheim" (FiresRPGClasses' own catalogue). On an older Core the module is absent and the Classes view stays.
            if (id == "achievements" && HubAchievements())
            {
                FiresRPGClassesPlugin.Log?.LogInfo("[ClassesHub] Achievements: handed off to the hub tab fires.achievements (valheim)");
                FiresShell.Open("fires.achievements", "valheim");
                return;
            }
            if (TryTab(id, out var tab)) ShowTab(tab);
        }

        private static bool? s_hubAchievements;
        private static bool HubAchievements()
        {
            if (s_hubAchievements == null)
            {
                s_hubAchievements = Type.GetType("FiresCore.UI.Shell.ShellAchievementsModule, FiresUnifiedCore", false) != null;
                FiresRPGClassesPlugin.Log?.LogInfo($"[ClassesHub] hub Achievements tab {(s_hubAchievements.Value ? "found (Core 0.2.521+): the sub-tab hands off" : "absent (Core < 0.2.521): the Classes view stays")}");
            }
            return s_hubAchievements.Value;
        }

        private void ShowTab(ClassesTab tab)
        {
            var panel = ClassesPanel.Instance;
            if (panel == null) return;
            panel.ShowHosted(tab);
            // Summons only for summoner classes (ClassesPanel.Show already falls back to Talents)
            _selecting = true;
            try { _ctx?.SelectSubTab(SubId(panel.CurrentTab)); } finally { _selecting = false; }
        }

        /// <summary>A view asked to switch (e.g. Character's "go to Talents"): mirror it on the shell's sub-tab row.</summary>
        internal static void MirrorSubTab(ClassesTab tab)
        {
            if (_instance?._ctx == null || _instance._selecting) return;
            _instance._selecting = true;
            try { _instance._ctx.SelectSubTab(SubId(tab)); } finally { _instance._selecting = false; }
        }

        private void ReturnPages() => ClassesPanel.Instance?.ReturnPages();
    }
}
