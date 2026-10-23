using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using FiresCore.UI.Shell;
using UnityEngine;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// The Classes window as a Tab module of Core's Fires hub, with its content unchanged. The pages ClassesPanel builds once are
    /// re-parented into the shell's content area while hosted and handed back to the standalone window otherwise, so the views are
    /// never built twice and a live toggle works. Hosted only while Core's Fires UI override AND "Classes in hub" are both on;
    /// otherwise the standalone window is used unchanged.
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
        // A pill under the PLAYER tab - Stats 0, Appearance 10, Classes 20, Leaderboard 30.
        public override string ParentTab => "player";
        public override int PillOrder => 20;
        public override int Order => 10;
        // Compact hub chrome, and lay the content out at the Classes page's own size (page 1440x860 plus margins), scaled to fit
        // as one piece; a height-bound content area would shrink it until unreadable.
        public override bool Dense => true;
        public override Vector2 MinContentSize => new Vector2(1524f, 890f);

        private RectTransform _content;
        private IShellContext _ctx;
        private bool _selecting;

        // Sub-view ids, in the order of the mockup's row; "appearance" launches the Fires Character Creator.
        private static readonly (string id, ClassesTab tab, string title)[] Subs =
        {
            ("character", ClassesTab.Character, "Overview"),   // "Overview" so nothing under CLASSES reads like a top tab (id kept)
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
            _inHub = config.Bind("6 - UI", "Classes in hub", true,   // TidyConfig carries values from the old [UI] section over
                "With the Fires UI override on, show the Classes window as a tab of the Fires hub instead of its own window.");
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
            SetTabs();
            ctx.SetFooterHints(("Tab", "next tab"), ("Q / E", "sub-view"), ("Click", "learn"), ("Esc", "close"));
            FiresRPGClassesPlugin.Log?.LogDebug($"[ClassesHub] BuildTab: content {content.rect.width:0}x{content.rect.height:0}, {_tabIds.Count} sub-tabs");
        }

        // The sub-tab row is rebuilt on every show, so Summons is listed only for a summoner class, and a pill is only ever
        // selected if it is in the row (Achievements has none under the hub; selecting it would light up another pill).
        private readonly HashSet<string> _tabIds = new HashSet<string>();
        private void SetTabs()
        {
            var tabs = new List<(string id, string title, bool badge)>();
            _tabIds.Clear();
            // No sub-tab duplicates a hub top tab - Appearance is FCC's own top tab, and Achievements is the hub's
            // (kept here only on an older Core that has no hub Achievements tab).
            foreach (var s in Subs)
            {
                if (s.id == "achievements" && HubAchievements()) continue;
                if (s.tab == ClassesTab.Summons && !SummonsView.Available()) continue;
                tabs.Add((s.id, s.title, false)); _tabIds.Add(s.id);
            }
            _ctx?.SetSubTabs(tabs.ToArray(), OnSubSelected);
        }

        private void Select(ClassesTab tab)
        {
            string id = SubId(tab);
            if (!_tabIds.Contains(id)) return;
            _selecting = true;
            try { _ctx?.SelectSubTab(id); } finally { _selecting = false; }
        }

        public override void OnShow(string subView)
        {
            var panel = ClassesPanel.Instance;
            if (panel == null || _content == null) return;
            panel.AdoptPages(_content);
            SetTabs();
            ClassesTab tab = TryTab(subView, out var t) ? t : panel.CurrentTab;
            ShowTab(tab);
        }

        public override void OnHide()
        {
            // The editor hosted over the hub goes with it (nothing it made outlives the hub)
            if (ClassesPanel.AppearanceEditor.IsOpen()) { FiresRPGClassesPlugin.Log?.LogInfo("[ClassesHub] hub hides: closing the Fires Character Creator first"); ClassesPanel.AppearanceEditor.Close(); }
            ReturnPages();
        }

        // The hub window is drag-resizable; re-fit the hosted pages to the new content size
        public override void OnResize(Vector2 size) { if (_content != null) ClassesPanel.Instance?.RefitHub(_content); }

        /// <summary>Esc: an open talent card (IClassesOverlay) closes first; true = consumed, the hub stays open.</summary>
        public override bool OnEscape()
        {
            // Esc closes the hosted Character Creator first and returns to the hub page under it
            if (ClassesPanel.AppearanceEditor.IsOpen()) { FiresRPGClassesPlugin.Log?.LogInfo("[ClassesHub] Esc: Fires Character Creator closed, hub stays"); ClassesPanel.AppearanceEditor.Close(); return true; }
            return ClassesPanel.Instance != null && ClassesPanel.Instance.CloseOverlayHosted();
        }

        private void OnSubSelected(string id)
        {
            if (_selecting) return;
            if (id == "appearance")
            {
                // The hub does not Close() here: that would restore the inventory under FCC and open it twice.
                // FCC sees FiresShell.IsOpen and is hosted on top; the hub keeps input/hotbars/Esc. The sub-tab row
                // goes back to the page under the editor, so a second click is a fresh (idempotent) open request.
                bool already = ClassesPanel.AppearanceEditor.IsOpen();
                FiresRPGClassesPlugin.Log?.LogInfo($"[ClassesHub] Appearance: open Fires Character Creator over the hub (already open={already})");
                ClassesPanel.AppearanceEditor.Open();
                var panel = ClassesPanel.Instance;
                if (panel != null) { _selecting = true; try { _ctx?.SelectSubTab(SubId(panel.CurrentTab)); } finally { _selecting = false; } }
                return;
            }
            // Core has ONE hub Achievements tab over every catalogue: the Classes "Achievements" sub-tab hands off to it,
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
            // the hub's own Achievements tab takes Achievements (no Classes sub-tab for it under the hub)
            if (tab == ClassesTab.Achievements && HubAchievements()) { FiresShell.Open("fires.achievements", "valheim"); return; }
            panel.ShowHosted(tab);
            Select(panel.CurrentTab);   // Summons falls back to Talents for a non-summoner (ClassesPanel.Show)
        }

        /// <summary>A view asked to switch (e.g. Character's "go to Talents"): mirror it on the shell's sub-tab row.</summary>
        internal static void MirrorSubTab(ClassesTab tab)
        {
            if (_instance?._ctx == null || _instance._selecting) return;
            // A view opening Achievements directly hands off like ShowTab does, so the old pill does not stay lit
            if (tab == ClassesTab.Achievements && HubAchievements()) { FiresShell.Open("fires.achievements", "valheim"); return; }
            _instance.Select(tab);
        }

        private void ReturnPages() => ClassesPanel.Instance?.ReturnPages();
    }
}
