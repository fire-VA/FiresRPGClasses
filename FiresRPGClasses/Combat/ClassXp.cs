using System;
using System.Collections.Generic;
using FiresCore.Classes;
using FiresCore.Logging;
using FiresRPGClasses.State;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Class XP for players (Fire, 2026-09-30: "start the class xp hooks"; plan Tools\CLASS_XP_PLAN.md). Core has the rules
    /// (XpSources) and nothing called them, so a new character stayed class level 1. Each client awards its own character: the
    /// class record lives in that character's .fch. Every award goes through Award and logs one line:
    /// "[ClassXp] +N source (detail) -> L&lt;level&gt; &lt;xp&gt;/&lt;next&gt;".
    /// - kills: Core's KillXp, for the owner's death broadcast (ClassKillNetwork), within 50 m and active in a fight in the
    ///   last 30 s; the first kill of a creature type and of each boss is also a discovery;
    /// - discoveries: biomes, location names, items, recipes and pieces, the first time vanilla learns them (ClassXpHooks);
    /// - vanilla skill level-ups;
    /// - gathering (Fire's popup: a small capped share): each felled tree, split log, broken rock piece or picked item pays
    ///   0.5 % of a level under Core's hourly repeatable-work cap;
    /// - everyday actions (Fire 10-02, ClassXpActivity): exploring, crafting, cooking, building, processing, fishing, sailing,
    ///   each a small share under its own hourly cap.
    /// </summary>
    public static class ClassXp
    {
        private const string Tag = "[ClassXp] ";

        // Fire 10-01: per-award evidence goes out at Core's Debug level ([Logging] Log Level = Debug shows it); level-ups and
        // warnings stay on the plugin log.
        private static readonly FiresLog DebugLog = new FiresLog("FiresRPGClasses", () => false);
        private const float GatherShareOfLevel = 0.005f;
        private const float WorkCompleteSeconds = 10f;
        private const float WorkForgetSeconds = 20f;
        private const float WorkTickSeconds = 0.25f;
        private const float CappedLogSeconds = 60f;
        private const int MaxTrackedWork = 64;

        /// <summary>Raised after every award: the amount granted and its source (kill, discovery, skill, gathering).</summary>
        public static event Action<long, string> Awarded;

        /// <summary>Raised when an award takes the character to a new class level: the new level.</summary>
        public static event Action<int> LevelledUp;

        private static readonly Dictionary<string, long> _sessionBySource = new Dictionary<string, long>();

        /// <summary>XP granted this session, by source.</summary>
        public static IReadOnlyDictionary<string, long> SessionXpBySource => _sessionBySource;

        // One rolling hourly budget per capped source, so an hour of chopping never shuts out the fish caught after it: gathering
        // keeps Core's share of a level, the everyday actions ClassXpActivity's.
        private static readonly Dictionary<string, RepeatableWorkBudget> _budgets = new Dictionary<string, RepeatableWorkBudget>();

        private static float HourlyCapShare(string source) =>
            source == "gathering" ? XpSources.DefaultRepeatableHourlyCapShareOfLevel : ClassXpActivity.HourlyCapShare;
        private static float _lastActive = float.NegativeInfinity;
        private static float _nextWorkTick;
        private static float _lastCappedLog = float.NegativeInfinity;
        private static bool _warnedProtected;

        private static ProgressionRates Rates => ProgressionSettings.CurrentRates;

        // -- activity: "active in the fight" is global, as XpSources defines it ----------------------------------------------

        /// <summary>The local player dealt or took combat damage, or cast a class skill.</summary>
        public static void MarkActive() => _lastActive = Time.time;

        public static float SecondsSinceActive => Time.time - _lastActive;

        // -- awards ---------------------------------------------------------------------------------------------------------

        private static long LevelCost(ClassPlayerData data) => ProgressionEngine.XpForNextLevel(data.Progress.Level, data.Progress.ValorLevel, Rates);

        internal static long Award(XpSourceKind kind, long baseXp, string source, string detail)
        {
            Player player = Player.m_localPlayer;
            ClassPlayerData data = ClassPlayerStore.Local;
            if (player == null || data == null || baseXp <= 0) return 0;
            if (ClassPlayerStore.LocalIsProtected)
            {
                if (!_warnedProtected)
                    FiresRPGClassesPlugin.Log.LogWarning(Tag + "no XP this session: the stored class record is protected (see the error above)");
                _warnedProtected = true;
                return 0;
            }

            ProgressionRates rates = Rates;
            bool rested = player.GetSEMan() != null && player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectRested);
            long amount = (long)Math.Floor(baseXp * XpSources.MultiplierFor(kind, rates, 0f, rested));
            if (XpSources.IsCapped(kind))
            {
                long cap = RepeatableWorkBudget.HourlyCap(LevelCost(data), HourlyCapShare(source));
                if (!_budgets.TryGetValue(source, out RepeatableWorkBudget budget)) _budgets[source] = budget = new RepeatableWorkBudget();
                long granted = budget.Take(amount, cap, Time.realtimeSinceStartup);
                if (granted < amount && Time.time - _lastCappedLog >= CappedLogSeconds)
                {
                    _lastCappedLog = Time.time;
                    DebugLog.Debug($"{Tag}{source} at its hourly cap ({cap} XP an hour at L{data.Progress.Level}): {detail} paid {granted} of {amount}");
                }
                amount = granted;
            }
            if (amount <= 0) return 0;
            return Grant(player, data, amount, rates, source, detail);
        }

        /// <summary>
        /// class_xp (ClassAdminCommands, Fire 10-01 "a way to give myself XP so I can test all of the classes"): the same grant
        /// play ends in - levels, talent and attribute points, the level-up message and LevelledUp - for exactly this amount.
        /// </summary>
        /// <summary>A FiresLoreBooks reward ("GiveClassXP n", read to the last page): the normal grant, source "lorebook".</summary>
        internal static long GrantFromLore(long amount, string bookId)
        {
            Player player = Player.m_localPlayer;
            ClassPlayerData data = ClassPlayerStore.Local;
            if (player == null || data == null || amount <= 0 || ClassPlayerStore.LocalIsProtected) return 0;
            return Grant(player, data, amount, Rates, "lorebook", bookId);
        }

        internal static long GrantForTest(long amount)
        {
            Player player = Player.m_localPlayer;
            ClassPlayerData data = ClassPlayerStore.Local;
            if (player == null || data == null || amount <= 0 || ClassPlayerStore.LocalIsProtected) return 0;
            return Grant(player, data, amount, Rates, "console", "class_xp");
        }

        /// <summary>
        /// class_level: the class level set outright, XP into it reset. Lowered below what is spent, the talent trees (and the
        /// attributes, when those are over too) are refunded, as Fire asked. Returns the line the console prints.
        /// </summary>
        internal static string SetLevelForTest(int level)
        {
            Player player = Player.m_localPlayer;
            ClassPlayerData data = ClassPlayerStore.Local;
            if (player == null || data == null) return "no character";
            if (ClassPlayerStore.LocalIsProtected) return "the class record is protected this session (see the log)";

            ProgressionRates rates = Rates;
            int before = data.Progress.Level;
            data.Progress.Level = Mathf.Clamp(level, ProgressionEngine.FirstLevel, ProgressionEngine.MaxLevel);
            data.Progress.Xp = 0;
            string refunds = "";
            if (data.Progress.UnspentSkillPoints(rates) < 0)
            {
                refunds += $", {data.Progress.SpentSkillPoints} talent points refunded";
                data.Progress.Talents.Clear();
                data.Progress.PassiveLoadout.Clear();
            }
            if (data.Progress.UnspentAttributePoints(rates) < 0)
            {
                refunds += $", {data.Progress.SpentAttributePoints} attribute points refunded";
                data.Progress.Attributes.Clear();
            }
            ClassPlayerStore.SaveLocal();

            int now = data.Progress.Level;
            string line = $"class level {before} -> {now}{refunds}; unspent talent points {data.Progress.UnspentSkillPoints(rates)}, "
                + $"attribute points {data.Progress.UnspentAttributePoints(rates)}";
            FiresRPGClassesPlugin.Log.LogInfo(Tag + "class_level: " + line);
            if (now > before)
            {
                player.Message(MessageHud.MessageType.Center, $"Class level {now}");
                Raise(() => LevelledUp?.Invoke(now));
            }
            return line;
        }

        /// <summary>The XP after every source rule: stored, saved, announced; a level-up says so. Returns the amount.</summary>
        private static long Grant(Player player, ClassPlayerData data, long amount, ProgressionRates rates, string source, string detail)
        {
            int levelBefore = data.Progress.Level;
            int valorBefore = data.Progress.ValorLevel;
            ProgressionGain gain = data.Progress.AddXp(amount, rates);
            ClassPlayerStore.SaveLocal();
            _sessionBySource.TryGetValue(source, out long total);
            _sessionBySource[source] = total + amount;

            DebugLog.Debug($"{Tag}+{amount} {source} ({detail}) -> L{data.Progress.Level} {data.Progress.Xp}/{LevelCost(data)}");
            Raise(() => Awarded?.Invoke(amount, source));
            if (gain.Any)
            {
                int level = data.Progress.Level;
                FiresRPGClassesPlugin.Log.LogInfo($"{Tag}level up: L{levelBefore} -> L{level}"
                    + (gain.ValorLevelsGained > 0 ? $", Valor {valorBefore} -> {data.Progress.ValorLevel}" : "")
                    + $"; unspent skill points {data.Progress.UnspentSkillPoints(rates)}");
                player.Message(MessageHud.MessageType.Center, gain.LevelsGained > 0 ? $"Class level {level}" : $"Valor {data.Progress.ValorLevel}");
                Raise(() => LevelledUp?.Invoke(level));
            }
            return amount;
        }

        private static void Raise(Action raise)
        {
            try { raise(); }
            catch (Exception e) { FiresRPGClassesPlugin.Log.LogWarning(Tag + "a listener threw: " + e.Message); }
        }

        /// <summary>A one-time discovery: a share of the current level.</summary>
        internal static void Discovery(DiscoveryKind kind, string what)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null) return;
            Award(XpSourceKind.Discovery, XpSources.DiscoveryXp(kind, LevelCost(data)), "discovery", $"{kind} {what}");
        }

        internal static void SkillLevelUp(Skills.SkillType skill, float level)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null) return;
            Award(XpSourceKind.SkillLevelUp, XpSources.SkillLevelUpXp(LevelCost(data)), "skill", $"{skill} {level:0}");
        }

        internal static void Gathered(string what)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null) return;
            long xp = Math.Max(1L, (long)Math.Floor(LevelCost(data) * GatherShareOfLevel));
            Award(XpSourceKind.RepeatableWork, xp, "gathering", what);
        }

        /// <summary>An everyday action (ClassXpActivity): a share of the current level, under that source's hourly cap.</summary>
        internal static void Activity(string source, float shareOfLevel, string what)
        {
            ClassPlayerData data = ClassPlayerStore.Local;
            if (data == null || shareOfLevel <= 0f) return;
            long xp = Math.Max(1L, (long)Math.Floor(LevelCost(data) * shareOfLevel));
            Award(XpSourceKind.RepeatableWork, xp, source, what);
        }

        /// <summary>The owner's death broadcast reached this client: award the kill if the local player earned it.</summary>
        internal static void OnDeath(string prefab, float maxHealth, int level, bool boss, bool summoned, Vector3 position, float tierMultiplier = 1f)
        {
            Player player = Player.m_localPlayer;
            ClassPlayerData data = ClassPlayerStore.Local;
            if (player == null || data == null || player.IsDead()) return;
            float distance = Vector3.Distance(player.transform.position, position);
            float idle = SecondsSinceActive;
            if (!XpSources.IsEligibleForKill(distance, idle))
            {
                if (distance <= XpSources.DefaultKillRangeMetres * 2f)
                    DebugLog.Debug($"{Tag}kill of {prefab} not counted: {distance:0} m away, "
                        + (float.IsInfinity(idle) ? "no fight yet" : $"last in a fight {idle:0} s ago")
                        + $" (needs {XpSources.DefaultKillRangeMetres:0} m and {XpSources.ActiveInFightSeconds:0} s)");
                return;
            }
            int stars = Mathf.Max(0, level - 1);
            // Fires Monster Overhaul's tier (Veteran x1.5, Elite x3, Champion x6), from the owner's death broadcast.
            bool tiered = tierMultiplier > 0f && Mathf.Abs(tierMultiplier - 1f) > 0.001f;
            long killXp = XpSources.KillXp(maxHealth, stars, boss, summoned);
            if (tiered) killXp = (long)Math.Floor(killXp * tierMultiplier);
            Award(XpSourceKind.Kill, killXp, "kill",
                $"{prefab} {stars} star(s), {distance:0} m{(boss ? ", boss" : "")}{(summoned ? ", summoned" : "")}"
                + (tiered ? $", tier x{tierMultiplier:0.##}" : ""));
            if (data.Firsts.Add("creature:" + prefab)) Discovery(DiscoveryKind.CreatureType, prefab);
            if (boss && data.Firsts.Add("boss:" + prefab)) Discovery(DiscoveryKind.Boss, prefab);
            ClassPlayerStore.SaveLocal();
        }

        // -- gathering: trees, logs, small rocks and destructibles count once gone after the local player's hit -----------

        private sealed class WorkHit
        {
            public ZNetView View;
            public ZDOID Id;
            public string What;
            public float Time;
        }

        private static readonly List<WorkHit> _work = new List<WorkHit>();
        private static readonly Dictionary<ZDOID, float> _rockHits = new Dictionary<ZDOID, float>();
        private static readonly Dictionary<ZDOID, float> _picks = new Dictionary<ZDOID, float>();

        internal static void NoteWorkHit(Component target, string what)
        {
            ZNetView view = target != null ? target.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid()) return;
            ZDOID id = view.GetZDO().m_uid;
            foreach (WorkHit hit in _work)
                if (hit.Id == id) { hit.Time = Time.time; return; }
            if (_work.Count >= MaxTrackedWork) _work.RemoveAt(0);
            _work.Add(new WorkHit { View = view, Id = id, What = what, Time = Time.time });
        }

        /// <summary>A MineRock5 the local player hit: its pieces breaking soon after are the local player's work.</summary>
        internal static void NoteRockHit(Component rock)
        {
            ZNetView view = rock != null ? rock.GetComponent<ZNetView>() : null;
            if (view != null && view.IsValid()) _rockHits[view.GetZDO().m_uid] = Time.time;
        }

        internal static void RockPieceBroken(Component rock, string what)
        {
            ZNetView view = rock != null ? rock.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid()) return;
            if (!_rockHits.TryGetValue(view.GetZDO().m_uid, out float when) || Time.time - when > WorkCompleteSeconds) return;
            Gathered(what);
        }

        internal static void Picked(Component pickable, string what)
        {
            ZNetView view = pickable != null ? pickable.GetComponent<ZNetView>() : null;
            if (view == null || !view.IsValid()) return;
            ZDOID id = view.GetZDO().m_uid;
            if (_picks.TryGetValue(id, out float when) && Time.time - when < WorkCompleteSeconds) return;
            _picks[id] = Time.time;
            Gathered(what);
        }

        /// <summary>Called every frame by ClassCombatDriver: work the local player hit that is now gone for good was finished.</summary>
        internal static void Tick()
        {
            if (Time.time < _nextWorkTick) return;
            _nextWorkTick = Time.time + WorkTickSeconds;
            for (int i = _work.Count - 1; i >= 0; i--)
            {
                WorkHit hit = _work[i];
                if (hit.View == null)
                {
                    _work.RemoveAt(i);
                    // Destroyed, not just unloaded: the ZDO itself is gone (unloading keeps it).
                    if (Time.time - hit.Time <= WorkCompleteSeconds && ZDOMan.instance != null && ZDOMan.instance.GetZDO(hit.Id) == null)
                        Gathered(hit.What);
                    continue;
                }
                if (Time.time - hit.Time > WorkForgetSeconds) _work.RemoveAt(i);
            }
            if (_rockHits.Count > MaxTrackedWork) _rockHits.Clear();
            if (_picks.Count > MaxTrackedWork) _picks.Clear();
        }
    }
}
