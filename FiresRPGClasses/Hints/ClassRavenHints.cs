using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using FiresCore.Classes;
using FiresCore.Logging;
using FiresCore.Npc.Archetypes;
using FiresRPGClasses.Combat;
using FiresRPGClasses.Data;
using FiresRPGClasses.Input;
using FiresRPGClasses.State;
using FiresRPGClasses.UI;
using UnityEngine;

namespace FiresRPGClasses.Hints
{
    /// <summary>
    /// Hugin teaches the classes: seven raven hints through vanilla's own tutorial system, so each is
    /// shown once per character, at the first moment it matters:
    /// - fires_classes_window: the first spawn.
    /// - fires_classes_pick: the first time the Classes window is open.
    /// - fires_classes_bars: the first bar weapon in hand, or the first swap.
    /// - fires_classes_weapon: the first time a bar's weapon change switches its class.
    /// - fires_classes_points: the first unspent talent point.
    /// - fires_classes_attributes: the first unspent attribute point.
    /// - fires_classes_skills: the first talent rank bought.
    /// Every text is written from the live rules (key chords, server rates, rank costs, slot levels, weapons per class), so it
    /// can't drift from the code. The compendium entries (UI\ClassesHelp.cs) render Topic/Text live from here, which
    /// is why the tutorial texts carry no label: Raven adds a label-less text to no saved compendium copy.
    /// </summary>
    public static class ClassRavenHints
    {
        public const string WindowKey = "fires_classes_window";
        public const string PickKey = "fires_classes_pick";
        public const string BarsKey = "fires_classes_bars";
        public const string WeaponKey = "fires_classes_weapon";
        public const string PointsKey = "fires_classes_points";
        public const string AttributesKey = "fires_classes_attributes";
        public const string SkillsKey = "fires_classes_skills";

        private const string Section = "6 - UI";
        private const float TickSeconds = 1f;
        private const float SettleSeconds = 8f;
        private const float GapSeconds = 20f;

        private static readonly string[] OrderedKeys = { WindowKey, PickKey, BarsKey, WeaponKey, PointsKey, AttributesKey, SkillsKey };

        private static readonly Dictionary<string, string> Topics = new Dictionary<string, string>
        {
            { WindowKey, "The Classes window" },
            { PickKey, "Choosing a class" },
            { BarsKey, "Two bars" },
            { WeaponKey, "Your weapon picks the class" },
            { PointsKey, "Talent points" },
            { AttributesKey, "Attributes" },
            { SkillsKey, "Skills, passives and the ultimate" },
        };

        private static readonly FiresLog DebugLog = new FiresLog("FiresRPGClasses", () => false);

        public static ConfigEntry<bool> Enabled { get; private set; }

        /// <summary>The hint keys, in the order they usually come.</summary>
        public static IReadOnlyList<string> Keys => OrderedKeys;

        public static string Topic(string key) => key != null && Topics.TryGetValue(key, out string topic) ? topic : "";

        /// <summary>Always empty: see the class summary.</summary>
        public static string Label(string key) => "";

        /// <summary>The hint's words, built now from the live rules; "" for an unknown key.</summary>
        public static string Text(string key)
        {
            try
            {
                switch (key)
                {
                    case WindowKey: return WindowText();
                    case PickKey: return PickText();
                    case BarsKey: return BarsText();
                    case WeaponKey: return WeaponText();
                    case PointsKey: return PointsText();
                    case AttributesKey: return AttributesText();
                    case SkillsKey: return SkillsText();
                    default: return "";
                }
            }
            catch (Exception e)
            {
                FiresRPGClassesPlugin.Log.LogWarning($"[ClassHints] writing '{key}' threw {e.GetType().Name}: {e.Message}");
                return "";
            }
        }

        public static void Bind(ConfigFile config)
        {
            if (Enabled != null) return;
            Enabled = config.Bind(Section, "Raven hints", true,
                "Hugin explains each part of the classes once per character, the first time it matters.");
        }

        // ---- when ------------------------------------------------------------------------------------------------------

        private static readonly HashSet<string> _offered = new HashSet<string>();
        private static float _tickAt, _nextOfferAt;
        private static Player _player;
        private static float _playerSince;
        private static ClassPlayerData _watched;
        private static BarSide _lastActive;
        private static readonly Dictionary<BarSide, KeyValuePair<string, ArchetypeClass>> _lastBars =
            new Dictionary<BarSide, KeyValuePair<string, ArchetypeClass>>();
        private static bool _swapped, _weaponSwitched;

        /// <summary>From ClassCombatDriver.Update, once a frame; does its work once a second.</summary>
        public static void Tick()
        {
            if (Time.time < _tickAt) return;
            _tickAt = Time.time + TickSeconds;
            if (Enabled == null || !Enabled.Value) return;

            Player player = Player.m_localPlayer;
            if (player != _player)
            {
                _player = player;
                _playerSince = Time.time;
                _offered.Clear();
            }
            ClassPlayerData data = ClassPlayerStore.Local;
            if (player == null || data == null || !ClassTrees.IsLoaded) return;
            // Every text joins vanilla's list up front, whatever the HUD; only the raven's offer waits, so a hidden HUD never
            // leaves a hint unregistered.
            EnsureRegistered();
            Watch(data);
            // No raven while the HUD is hidden (Ctrl+F3); the hint stays unseen and
            // comes once the HUD is back.
            if (player.IsDead() || player.InIntro() || Hud.IsUserHidden() || Time.time - _playerSince < SettleSeconds || Time.time < _nextOfferAt) return;

            foreach (string key in OrderedKeys)
            {
                if (_offered.Contains(key) || player.HaveSeenTutorial(key) || !Due(key, player, data)) continue;
                Offer(player, key);
                return;
            }
        }

        // Swaps and weapon-driven class changes happen between ticks' offers, so they are latched as they are seen.
        private static void Watch(ClassPlayerData data)
        {
            if (!ReferenceEquals(data, _watched))
            {
                _watched = data;
                _lastActive = data.ActiveBar;
                _lastBars.Clear();
            }
            if (data.ActiveBar != _lastActive)
            {
                _lastActive = data.ActiveBar;
                _swapped = true;
            }
            foreach (BarSide side in new[] { BarSide.Front, BarSide.Back })
            {
                BarLayout bar = data.Bar(side);
                if (_lastBars.TryGetValue(side, out KeyValuePair<string, ArchetypeClass> last)
                    && last.Key != bar.WeaponPrefab && last.Value != bar.Archetype && last.Value != ArchetypeClass.None)
                    _weaponSwitched = true;
                _lastBars[side] = new KeyValuePair<string, ArchetypeClass>(bar.WeaponPrefab, bar.Archetype);
            }
        }

        private static bool Due(string key, Player player, ClassPlayerData data)
        {
            ProgressionRates rates = TalentRules.Rates;
            switch (key)
            {
                case WindowKey: return true;
                case PickKey: return ClassesPanel.Instance != null && ClassesPanel.Instance.IsOpen;
                case BarsKey: return _swapped || BarWeaponInHand(player, data);
                case WeaponKey: return _weaponSwitched;
                case PointsKey: return data.Progress.UnspentSkillPoints(rates) > 0;
                case AttributesKey: return data.Progress.UnspentAttributePoints(rates) > 0;
                case SkillsKey: return data.Progress.SpentSkillPoints > 0;
                default: return false;
            }
        }

        private static bool BarWeaponInHand(Player player, ClassPlayerData data) =>
            BarController.HasBarSlots
                ? BarController.BarWeapon(data.ActiveBar) != null && BarController.HoldingBarWeapon(data.ActiveBar)
                : player.RightItem != null;

        private static Tutorial _registeredOn;

        // Once per Tutorial instance (it is rebuilt with the scene): all seven texts, written from the rules as they are now.
        private static void EnsureRegistered()
        {
            Tutorial tutorial = Tutorial.instance;
            if (tutorial == null || ReferenceEquals(_registeredOn, tutorial)) return;
            _registeredOn = tutorial;
            foreach (string key in OrderedKeys)
            {
                string text = Text(key);
                if (!string.IsNullOrEmpty(text)) Set(tutorial, key, Topic(key), text);
            }
        }

        private static void Offer(Player player, string key)
        {
            Tutorial tutorial = Tutorial.instance;
            if (tutorial == null) return;
            string text = Text(key);
            if (string.IsNullOrEmpty(text)) return;
            Set(tutorial, key, Topic(key), text);
            player.ShowTutorial(key);
            _offered.Add(key);
            _nextOfferAt = Time.time + GapSeconds;
            DebugLog.Debug($"[ClassHints] raven offered {key}");
        }

        // Rewritten at each offer, so the raven reads the rules as they are now.
        private static void Set(Tutorial tutorial, string key, string topic, string text)
        {
            Tutorial.TutorialText existing = tutorial.m_texts.Find(t => t.m_name == key);
            if (existing != null)
            {
                existing.m_topic = topic;
                existing.m_label = "";
                existing.m_text = text;
                return;
            }
            tutorial.m_texts.Add(new Tutorial.TutorialText { m_name = key, m_topic = topic, m_label = "", m_text = text });
        }

        // ---- what ------------------------------------------------------------------------------------------------------

        private static string O(string text) => "<color=orange>" + text + "</color>";

        private static string WindowText()
        {
            string open = ClassesPanel.OpenKey != null ? KeyName(ClassesPanel.OpenKey) : "K";
            return "You fight with more than your arms now, little one. Press " + O(open) + " to open the " + O("Classes")
                + " window; the " + O("Skills") + " button in your inventory opens it too"
                + (BarController.HasBarSlots ? ", and so does the " + O("Classes") + " tab beside Crafting" : "") + ". "
                + "In it you choose the class on each of your two bars, spend talent and attribute points, equip passives and "
                + "watch your class level. That level rises as you fight, as you find new lands, places and things, as your "
                + "skills grow, and a little from felling, mining and picking.";
        }

        private static string PickText()
        {
            var text = new StringBuilder();
            text.Append("A class here is no oath, little one: it belongs to a ").Append(O("bar")).Append(". Under ")
                .Append(O("Class")).Append(" you choose one for your front bar and one for your back bar, from those the bar's "
                + "weapon can carry; the rest are dimmed. ");

            List<ArchetypeClass> archetypes = ClassTrees.Archetypes.ToList();
            ClassTree active = ClassTrees.Tree(ClassPlayerStore.Local?.Active.Archetype ?? ArchetypeClass.None);
            int paths = ClassTrees.Archetypes.Select(a => RealPaths(ClassTrees.Tree(a)).Count).DefaultIfEmpty(0).Max();
            text.Append("There are ").Append(archetypes.Count).Append(" classes, and every class's tree splits into ")
                .Append(paths).Append(' ').Append(O("paths"));
            List<string> activePaths = RealPaths(active);
            if (activePaths.Count > 0)
                text.Append(" (your ").Append(active.Archetype).Append(": ").Append(JoinAnd(activePaths)).Append(')');
            text.Append(". A path climbs in tiers: ").Append(TierLevels()).Append(". A talent opens at its level once you have "
                + "learned a talent in the tier above it on the same path; the starter counts for every path. A ")
                .Append(O("Master")).Append(" talent also needs ").Append(Points(ClassTrees.MasterPointsRequiredInPath))
                .Append(" spent in its path. Walk one path deep or several side by side.");
            return text.ToString();
        }

        private static string BarsText()
        {
            string slot1 = ClassKeys.SlotLabel(1), slot2 = ClassKeys.SlotLabel(2), slot7 = ClassKeys.SlotLabel(7), slot8 = ClassKeys.SlotLabel(8);
            bool drawnOnly = ClassKeys.SkillsNeedDrawnWeapon == null || ClassKeys.SkillsNeedDrawnWeapon.Value;
            bool slotOneSwaps = ClassKeys.SlotOneSwapsBars == null || ClassKeys.SlotOneSwapsBars.Value;
            var text = new StringBuilder();
            text.Append("Your skills ride on two bars, little one, ").Append(O("front")).Append(" and ").Append(O("back"))
                .Append(". Each has its own weapon (the one in the bar's first slot")
                .Append(BarController.HasBarSlots ? ", set in your inventory's bar slots" : "")
                .Append("), its own class and its own skills, set under ").Append(O("Bars")).Append(" in the Classes window. ")
                .Append(drawnOnly ? "With a weapon drawn, " : "")
                .Append(O(slot1)).Append(" draws the bar's weapon")
                .Append(slotOneSwaps ? " and, once it is in your hands, swaps to the other bar" : "")
                .Append("; ").Append(O(slot2)).Append(" to ").Append(O(slot7)).Append(" cast the skills on slots 2 to 7; ")
                .Append(O(slot8)).Append(" casts your ultimate.");
            if (ClassKeys.BarSwap != null && ClassKeys.BarSwap.Key.Value != KeyCode.None)
                text.Append(' ').Append(O(KeyName(ClassKeys.BarSwap))).Append(" swaps bars as well.");
            text.Append(" A swap takes a moment to settle, and a bar whose weapon has left your pack cannot fire until one is "
                + "back in its first slot.");
            return text.ToString();
        }

        private static string WeaponText()
        {
            var text = new StringBuilder();
            text.Append("Your bar changed its class, little one. The weapon in a bar's ").Append(O("first slot"))
                .Append(", with a shield beside it or not, decides which classes that bar can be. The bar's class decides which "
                + "of your skills it casts, and your dodge becomes that class's evasion once you have learned it. Change the "
                + "weapon and a class it cannot carry gives way to one it can. Your class level is the same whatever you hold, "
                + "and an ultimate grows by its own casts. What each class takes:");
            foreach (ArchetypeClass archetype in ClassTrees.Archetypes)
                text.Append('\n').Append(O(ClassNames.Of(archetype))).Append(": ").Append(WeaponArchetypes.WeaponsLine(archetype));
            return text.ToString();
        }

        private static string PointsText()
        {
            ProgressionRates rates = TalentRules.Rates;
            int[] costs = ClassTrees.RankCosts;
            var text = new StringBuilder();
            text.Append("You have a talent point to spend, little one. Every class level brings ")
                .Append(Points(rates.SkillPointsPerLevel));
            if (rates.ExtraSkillPointsPerTenthLevel > 0)
                text.Append(", and every tenth level ").Append(rates.ExtraSkillPointsPerTenthLevel).Append(" more");
            text.Append(". Spend them under ").Append(O("Talents")).Append(", in any class's tree, not only the ones on your bars. "
                + "A talent has up to ").Append(costs.Length).Append(" ranks: ");
            for (int i = 0; i < costs.Length; i++)
                text.Append(i == 0 ? "" : i == costs.Length - 1 ? " and the " : ", the ").Append(i == 0 ? "the " : "")
                    .Append(Ordinal(i + 1)).Append(" costs ").Append(Points(costs[i]));
            text.Append(", and each rank makes it stronger. A ").Append(O("Master")).Append(" talent waits for ")
                .Append(Points(ClassTrees.MasterPointsRequiredInPath)).Append(" spent in its path. Ultimates are never bought: the first opens at level ")
                .Append(UltimateCatalog.UltimateOneLevel).Append(" once you have ").Append(Points(UltimateCatalog.PointsInTreeForUltimateOne))
                .Append(" in that class's tree, the second at level ").Append(UltimateCatalog.UltimateTwoLevel).Append(" with ")
                .Append(UltimateCatalog.PointsInPathForUltimateTwo).Append(", and the hybrid of your two bars' classes at level ")
                .Append(UltimateCatalog.HybridUnlockLevel).Append(" with ").Append(UltimateCatalog.PointsInEachArchetypeForHybrid)
                .Append(" in each. ").Append(O("Reset")).Append(" on the Talents page gives a tree's points back.");
            return text.ToString();
        }

        private static string AttributesText()
        {
            ProgressionRates rates = TalentRules.Rates;
            var text = new StringBuilder();
            text.Append("You have an attribute point, little one. ");
            float perLevel = rates.AttributePointsPerLevel;
            if (perLevel >= 1f) text.Append("Every class level brings ").Append(perLevel.ToString("0.#")).Append(" of them");
            else if (perLevel > 0f) text.Append("One comes every ").Append(Mathf.RoundToInt(1f / perLevel)).Append(" class levels");
            if (perLevel > 0f) text.Append(rates.AttributePointsMaximum > 0 ? ", up to " + rates.AttributePointsMaximum + ". " : ", with no cap. ");   // a maximum of 0 means no cap
            text.Append("Spend them on the ").Append(O("Character")).Append(" page of the Classes window. What one point gives:");
            foreach (AttributeKind kind in Enum.GetValues(typeof(AttributeKind)))
            {
                var onePoint = new Dictionary<AttributeKind, int> { { kind, 1 } };
                List<string> gains = AttributeEffects.Preview(new ClassProgressState(), onePoint)
                    .Where(row => row.Attribute == kind && !Mathf.Approximately(row.Before, row.After))
                    .Select(row => Signed(row.After - row.Before, row.Percent) + " " + row.Label.ToLowerInvariant())
                    .ToList();
                if (gains.Count > 0) text.Append('\n').Append(O(kind.ToString())).Append(": ").Append(string.Join(", ", gains.ToArray()));
            }
            return text.ToString();
        }

        private static string SkillsText()
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            int slotsNow = data != null ? data.Progress.PassiveSlots : 0;
            List<int> slotLevels = PassiveSlotLevels();
            var text = new StringBuilder();
            text.Append("Talents come in kinds, little one. ").Append(O("Skills")).Append(" you cast: set them on a bar's slots 2 "
                + "to 7 under Bars, and they fire with that bar's keys while it is out. ").Append(O("Passives"))
                .Append(" you equip under Passives, and they stay on whichever bar is out; you have ")
                .Append(slotsNow == 1 ? "one passive slot" : slotsNow + " passive slots").Append(" now");
            if (slotLevels.Count > 0) text.Append(", and they open at levels ").Append(JoinAnd(slotLevels.Select(l => l.ToString()).ToList()));
            text.Append(". ").Append(O("Automatic")).Append(" talents are always on while their class is on the active bar, and a "
                + "class's ").Append(O("evasion")).Append(" replaces your dodge roll the same way. The ").Append(O("ultimate"))
                .Append(" sits on slot 8 from level ").Append(UltimateCatalog.SlotUnlockLevel)
                .Append(". It is never bought: it grows each time you cast it, and gains new powers as it levels.");
            return text.ToString();
        }

        // ---- helpers -----------------------------------------------------------------------------------------------------

        private static List<string> RealPaths(ClassTree tree) =>
            tree == null ? new List<string>()
                : tree.Paths.Where(p => p.Id != ClassTreeNode.AllPaths && !string.IsNullOrEmpty(p.DisplayName)).Select(p => p.DisplayName).ToList();

        // "Starter at level 1, Basic from 5, ..." from the loaded trees: each tier's lowest node level.
        private static string TierLevels()
        {
            var parts = new List<string>();
            foreach (NodeTier tier in Enum.GetValues(typeof(NodeTier)))
            {
                if (tier == NodeTier.Ultimate) continue;
                int level = ClassTrees.AllNodes.Where(n => n.Tier == tier && n.Slot != NodeSlot.Ultimate && n.Slot != NodeSlot.Evasion)
                    .Select(n => n.MinLevel).DefaultIfEmpty(0).Min();
                if (level > 0) parts.Add(tier + (parts.Count == 0 ? " at level " : " from ") + level);
            }
            return string.Join(", ", parts.ToArray());
        }

        private static List<int> PassiveSlotLevels()
        {
            var levels = new List<int>();
            int previous = ProgressionEngine.PassiveSlots(ProgressionEngine.FirstLevel, 0);
            if (previous > 0) levels.Add(ProgressionEngine.FirstLevel);
            for (int level = ProgressionEngine.FirstLevel + 1; level <= ProgressionEngine.MaxLevel; level++)
            {
                int slots = ProgressionEngine.PassiveSlots(level, 0);
                if (slots > previous) levels.Add(level);
                previous = slots;
            }
            return levels;
        }

        private static string KeyName(FiresCore.Input.KeyBinding binding)
        {
            string mods = (binding.Ctrl.Value ? "Ctrl+" : "") + (binding.Shift.Value ? "Shift+" : "") + (binding.Alt.Value ? "Alt+" : "");
            KeyCode key = binding.Key.Value;
            string name;
            switch (key)
            {
                case KeyCode.Mouse0: name = "left mouse button"; break;
                case KeyCode.Mouse1: name = "right mouse button"; break;
                case KeyCode.Mouse2: name = "middle mouse button"; break;
                case KeyCode.Mouse3: name = "mouse button 4"; break;
                case KeyCode.Mouse4: name = "mouse button 5"; break;
                default: name = key.ToString(); break;
            }
            return mods + name;
        }

        private static string Points(int points) => points + (points == 1 ? " point" : " points");

        private static string Ordinal(int n) => n == 1 ? "first" : n == 2 ? "second" : n == 3 ? "third" : n + "th";

        private static string Signed(float value, bool percent) =>
            (value >= 0f ? "+" : "") + (percent ? value.ToString("0.#") + " %" : value.ToString("0.##"));

        private static string JoinAnd(List<string> items) =>
            items.Count <= 1 ? string.Join("", items.ToArray())
                : string.Join(", ", items.Take(items.Count - 1).ToArray()) + " and " + items[items.Count - 1];
    }
}
