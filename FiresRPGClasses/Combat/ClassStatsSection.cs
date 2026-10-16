using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FiresCore.Classes;
using FiresCore.UI.Shell;
using FiresRPGClasses.State;
using FiresRPGClasses.UI;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// 0.1.159 ([wishbone], Fire via [generator] 10-15: "the hub's Stats tab shows every mod's stats"): RPGClasses' card on Core 0.2.511's
    /// Stats tab (FiresShell.RegisterStatsSection, Tools\CORE_STATS_SECTIONS_API.md; order 400). Data only, from the local class record
    /// and Core's ProgressionEngine; Core draws the card, reads it on the main thread only while the tab shows, every second.
    /// Registered next to ClassesHub.Register; the Stats tab exists only in the hub (the Fires UI override), so with the override off
    /// nothing changes. Older Core (no RegisterStatsSection): the call sits in its own NoInlining method behind a try/catch, so only
    /// that method fails to JIT and a line says so. Text is Latin-1 (AveriaSans).
    /// </summary>
    internal static class ClassStatsSection
    {
        private const string Id = "rpgclasses.stats", Title = "Classes", ParagonTreeOrder = "W / F / C";
        private const int Order = 400;
        private static readonly List<StatRow> Rows = new List<StatRow>(14);   // reused: Core copies the values out each refresh

        internal static void Register()
        {
            try { RegisterWithCore(); FiresRPGClassesPlugin.Log?.LogDebug($"[ClassStats] '{Id}' registered on the hub's Stats tab (order {Order})"); }
            catch (Exception e) { FiresRPGClassesPlugin.Log?.LogDebug($"[ClassStats] no hub Stats tab here (Core older than 0.2.511?): {e.GetBaseException().Message}"); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RegisterWithCore() => FiresShell.RegisterStatsSection(Id, Title, Order, BuildRows, 1f);

        private static IList<StatRow> BuildRows()
        {
            Rows.Clear();
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || Player.m_localPlayer == null) { Rows.Add(new StatRow("Class record", "not loaded", StatState.Muted)); return Rows; }
            ClassProgressState p = data.Progress;
            ProgressionRates rates = ProgressionSettings.CurrentRates;

            BarLayout active = data.Active;
            Rows.Add(new StatRow("Active class", active.Archetype.ToString(), StatState.Normal, $"{data.ActiveBar} bar"));
            Rows.Add(new StatRow("Bars", $"{data.Front.Archetype} / {data.Back.Archetype}", StatState.Normal, "front / back"));

            int max = ProgressionEngine.MaxLevel;
            bool atCap = p.Level >= max;
            long toNext = ProgressionEngine.XpForNextLevel(p.Level, p.ValorLevel, rates);
            Rows.Add(new StatRow("Level", $"{p.Level} / {max}", atCap ? StatState.Good : StatState.Normal));
            Rows.Add(new StatRow(atCap ? "Valor" : "XP to next level", atCap ? p.ValorLevel.ToString() : $"{p.Xp:N0} / {toNext:N0}"));
            if (atCap) Rows.Add(new StatRow("XP to next Valor", $"{p.Xp:N0} / {toNext:N0}"));

            int skills = ProgressionEngine.UnspentSkillPoints(p.Level, p.ValorLevel, p.SpentSkillPoints, rates);
            int attrs = ProgressionEngine.UnspentAttributePoints(p.Level, p.ValorLevel, p.SpentAttributePoints, rates);
            int paragon = p.UnspentParagonPoints();
            Rows.Add(new StatRow("Skill points", skills.ToString(), skills > 0 ? StatState.Warn : StatState.Muted, skills > 0 ? "unspent" : null));
            Rows.Add(new StatRow("Attribute points", attrs.ToString(), attrs > 0 ? StatState.Warn : StatState.Muted, attrs > 0 ? "unspent" : null));
            if (atCap || paragon > 0)
            {
                Rows.Add(new StatRow("Paragon points", paragon.ToString(), paragon > 0 ? StatState.Warn : StatState.Muted, paragon > 0 ? "unspent" : null));
                Rows.Add(new StatRow("Paragon spent", $"{Spent("warfare")} / {Spent("fitness")} / {Spent("craft")}", StatState.Normal, ParagonTreeOrder));
            }

            int slots = ProgressionEngine.PassiveSlots(p.Level, p.ValorLevel);
            Rows.Add(new StatRow("Passive slots", $"{slots} / {ProgressionEngine.MaxPassiveSlots}", slots >= ProgressionEngine.MaxPassiveSlots ? StatState.Good : StatState.Normal));
            Rows.Add(new StatRow("Weapon type", BarController.WeaponTypeKey(data.ActiveBar)));
            string race = SafeRace();
            if (!string.IsNullOrEmpty(race)) Rows.Add(new StatRow("Race", race));
            return Rows;
        }

        private static int Spent(string tree) { try { return Paragon.ParagonUi.SpentInTree(tree); } catch { return 0; } }

        private static string SafeRace() { try { return VcsBridge.GetRace(Player.m_localPlayer) ?? ""; } catch { return ""; } }
    }
}
