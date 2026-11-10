using System;
using System.Collections.Generic;
using System.Linq;
using FiresCore.Classes;
using FiresCore.Npc.Archetypes;
using FiresRPGClasses.Data;
using FiresRPGClasses.Paragon;
using FiresRPGClasses.State;
using FiresRPGClasses.UI;
using Newtonsoft.Json;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// 0.1.210 / 0.1.211 (Fire 10-30 via [lead], the Studio's God client: "There are no limits. We can adjust our level and our
    /// attribute points spent and our XP freely ... lose levels, lose experience, lose attributes or ... gain them. There are no rules.
    /// We are a god."): the ONE add-only facade FiresGodComplex wraps (by reflection, on the paired admin's own client - the class
    /// record is that character's m_customData; nothing here is an RPC). A god edit writes the record directly, both directions, any
    /// value (no points, prerequisites, level or weapon checks), saves it, and marks it God so the load-time reconcile leaves it alone
    /// (<see cref="SetGod"/>(false) hands it back to the rules). The bar / Paragon slot moves still go through BarController /
    /// ParagonUi so the HUD and the slotted effects follow. The class_* console commands call these same methods.
    /// Every method returns JSON {"ok":bool,"why":string,"state":{...}} with the state after the call (State() alone = read).
    /// Local player only; companions are Core's FiresCore.Npc.CompanionEditApi.
    /// </summary>
    public static class GodApi
    {
        private static ProgressionRates Rates => TalentRules.Rates;

        public static string State() => Result(true, "");

        /// <summary>false = the record goes back to the rules (the next load reconciles it against the point budgets).</summary>
        public static string SetGod(bool on) => Edit(d => { d.God = on; return on ? "god record" : "back to the rules at the next load"; }, mark: false);

        public static string SetLevel(int level) => Edit(d => { int was = d.Progress.Level; d.Progress.Level = Math.Max(ProgressionEngine.FirstLevel, level); return $"level {was} -> {d.Progress.Level}"; });
        public static string SetXp(long xp) => Edit(d => { long was = d.Progress.Xp; d.Progress.Xp = Math.Max(0L, xp); return $"xp {was} -> {d.Progress.Xp}"; });
        /// <summary>Fire 10-30 ("double clicking our experience bar ... add or remove experience, add or remove levels"): + = the normal XP grant
        /// (levels up as play does, through ClassProgressState.AddXp); - = XP taken off, dropping levels while it runs below 0 (each dropped
        /// level gives back that level's XP to fill), stopping at level 1 / 0 XP. No refunds: what was learned stays (a god record).</summary>
        public static string AddXp(long delta) => Edit(d =>
        {
            var p = d.Progress; int lv = p.Level; long xp = p.Xp;
            if (delta >= 0) p.AddXp(delta, Rates);
            else
            {
                p.Xp += delta;
                while (p.Xp < 0 && p.Level > ProgressionEngine.FirstLevel) { p.Level--; p.Xp += ProgressionEngine.XpForNextLevel(p.Level, p.ValorLevel, Rates); }
                if (p.Xp < 0) p.Xp = 0;
            }
            return $"xp {(delta >= 0 ? "+" : "")}{delta}: level {lv} -> {p.Level}, xp {xp} -> {p.Xp}";
        });

        public static string AddLevels(int delta) => SetLevel(((Data?.Progress.Level) ?? ProgressionEngine.FirstLevel) + delta);

        public static string SetValorLevel(int level) => Edit(d => { int was = d.Progress.ValorLevel; d.Progress.ValorLevel = Math.Max(0, level); return $"Valor level {was} -> {d.Progress.ValorLevel}"; });
        public static string SetValorXp(long xp) => Edit(d => { long was = d.Progress.ValorXp; d.Progress.ValorXp = Math.Max(0L, xp); return $"Valor xp {was} -> {d.Progress.ValorXp}"; });

        /// <summary>Free attribute points come from the bonus pool (earned + bonus - spent); Core shows at most 100 bonus on top of the earned.</summary>
        public static string SetBonusAttributePoints(int points) => Edit(d => { int was = d.Progress.BonusAttributePoints; d.Progress.BonusAttributePoints = Math.Max(0, points); return $"bonus attribute points {was} -> {d.Progress.BonusAttributePoints}"; });

        public static string SetAttribute(string kind, int value)
        {
            if (!Enum.TryParse(kind, true, out AttributeKind k)) return Result(false, $"no attribute '{kind}' ({string.Join(", ", Enum.GetNames(typeof(AttributeKind)))})");
            return Edit(d => { int was = d.Progress.Attribute(k); if (value <= 0) d.Progress.Attributes.Remove(k); else d.Progress.Attributes[k] = value; return $"{k} {was} -> {Math.Max(0, value)}"; });
        }

        /// <summary>Any node to any rank (0 = forgotten), no points / prerequisites / level; a forgotten node leaves both bars and the passive loadout.</summary>
        public static string SetNodeRank(string nodeId, int rank)
        {
            ClassTreeNode node = ClassTrees.Node(nodeId);
            if (node == null) return Result(false, $"no class node '{nodeId}'");
            return Edit(d =>
            {
                if (!d.Progress.Talents.TryGetValue(node.Archetype, out var tree)) d.Progress.Talents[node.Archetype] = tree = new Dictionary<string, int>();
                int was = tree.TryGetValue(nodeId, out int r) ? r : 0;
                if (rank <= 0)
                {
                    tree.Remove(nodeId);
                    d.Progress.UnequipPassive(nodeId);
                    foreach (BarSide s in new[] { BarSide.Front, BarSide.Back })
                        for (int slot = BarLayout.FirstSkillSlot; slot <= BarLayout.LastSkillSlot; slot++)
                            if (d.Bar(s).SkillAt(slot) == nodeId) d.Bar(s).SetSkill(slot, "");
                }
                else tree[nodeId] = rank;
                return $"{nodeId} rank {was} -> {Math.Max(0, rank)}";
            });
        }

        public static string Learn(string nodeId, int ranks = 1)
        {
            ClassTreeNode node = ClassTrees.Node(nodeId);
            if (node == null) return Result(false, $"no class node '{nodeId}'");
            var data = Data;
            int now = data != null ? data.Progress.TalentRank(node.Archetype, nodeId) : 0;
            return SetNodeRank(nodeId, Math.Min(Math.Max(1, node.MaxRank), now + Math.Max(1, ranks)));
        }

        public static string Unlearn(string nodeId) => SetNodeRank(nodeId, 0);

        public static string SetWeapon(string side, string prefab)
        {
            if (!Side(side, out BarSide s, out string why) || !Ready(out why)) return Result(false, why);
            var item = Player.m_localPlayer.GetInventory()?.GetAllItems().Find(i => i?.m_dropPrefab != null && string.Equals(i.m_dropPrefab.name, prefab, StringComparison.OrdinalIgnoreCase));
            if (item == null) return Result(false, $"no '{prefab}' in the inventory");
            return BarController.TryAssignWeapon(s, item) ? Done($"{s} bar weapon is now {prefab}") : Result(false, $"{prefab} can't go on the {s} bar");
        }

        /// <summary>The bar's class, the weapon notwithstanding (the window's path first, so the HUD follows; else written directly).</summary>
        public static string SetArchetype(string side, string archetype)
        {
            if (!Side(side, out BarSide s, out string why) || !Ready(out why)) return Result(false, why);
            if (!WerewolfClass.TryParse(archetype, out ArchetypeClass a) || (a != ArchetypeClass.None && !ClassTrees.Archetypes.Contains(a))) return Result(false, $"no class '{archetype}'");
            if (Local && BarController.TryChooseArchetype(s, a)) return Done($"{s} bar is now {ClassNames.Of(a)}");
            return Edit(d => { d.Bar(s).Archetype = a; return $"{s} bar is now {ClassNames.Of(a)} (set directly: its weapon wouldn't drive it)"; }, refreshBars: s);
        }

        /// <summary>Slot 2-7 = a skill, 8 = the ultimate; a node not yet learned is learned first (rank 1). "" empties the slot.</summary>
        public static string SlotSkill(string side, int slot, string nodeId)
        {
            if (!Side(side, out BarSide s, out string why) || !Ready(out why)) return Result(false, why);
            nodeId = nodeId ?? "";
            if (slot == BarLayout.UltimateSlot)
                return Edit(d => { d.Bar(s).UltimateId = nodeId; d.Bar(s).UltimateMorph = 0; return $"{s} ultimate = {(nodeId.Length > 0 ? nodeId : "(empty)")}"; }, refreshBars: s);
            if (!BarLayout.IsSkillSlot(slot)) return Result(false, "slot 2-7 = skills, 8 = the ultimate (1 = SetWeapon)");
            ClassTreeNode node = nodeId.Length > 0 ? ClassTrees.Node(nodeId) : null;
            if (nodeId.Length > 0 && node == null) return Result(false, $"no class node '{nodeId}'");
            if (node != null && Data.Progress.TalentRank(node.Archetype, nodeId) <= 0) SetNodeRank(nodeId, 1);
            if (Local && BarController.TrySlotSkill(s, slot, nodeId)) return Done($"{s} slot {slot} = {(nodeId.Length > 0 ? nodeId : "(empty)")}");
            return Edit(d => { d.Bar(s).SetSkill(slot, nodeId); return $"{s} slot {slot} = {nodeId} (set directly)"; }, refreshBars: s);
        }

        /// <summary>Any star to any rank (0 = gone, and unslotted), no points / unlocks.</summary>
        public static string SetParagonRank(string starId, int rank)
        {
            ParagonStarDef star = ParagonCatalog.Star(starId);
            if (star == null) return Result(false, $"no Paragon star '{starId}'");
            return Edit(d =>
            {
                var p = d.Progress; int was = p.ParagonRank(starId);
                if (rank <= 0)
                {
                    p.ParagonRanks.Remove(starId);
                    for (int i = 0; i < ClassProgressState.ParagonSlotsPerTree; i++) if (p.ParagonSlot(star.Tree, i) == starId) p.UnslotParagon(star.Tree, i);
                }
                else p.ParagonRanks[starId] = star.MaxRank > 0 ? Math.Min(rank, star.MaxRank) : rank;
                return $"{starId} rank {was} -> {p.ParagonRank(starId)}";
            });
        }

        public static string ParagonSlot(string tree, int slot, string starId)
        {
            if (!Ready(out string why)) return Result(false, why);
            if (Data.Progress.ParagonRank(starId) < 1) SetParagonRank(starId, 1);
            if (!Local) return Edit(d => d.Progress.TrySlotParagon(tree, slot, starId) ? $"{starId} in {tree} slot {slot + 1}" : throw new InvalidOperationException("refused"));
            return ParagonUi.TrySlot(tree, slot, starId, out why) ? Done($"{starId} in {tree} slot {slot + 1}") : Result(false, why);
        }

        public static string ParagonUnslot(string tree, int slot) =>
            !Ready(out string why) ? Result(false, why)
            : !Local ? Edit(d => { d.Progress.UnslotParagon(tree, slot); return $"{tree} slot {slot + 1} emptied"; })
            : ParagonUi.TryUnslot(tree, slot) ? Done($"{tree} slot {slot + 1} emptied") : Result(false, "refused (in combat?)");

        public static string ParagonRespec(string tree) =>
            !Ready(out string why) ? Result(false, why)
            : !Local ? Edit(d =>
              {
                  if (string.IsNullOrEmpty(tree)) { d.Progress.RespecParagon(); return "all respec'd"; }
                  foreach (string id in d.Progress.ParagonRanks.Keys.ToList()) if (ParagonCatalog.Star(id)?.Tree == tree) d.Progress.ParagonRanks.Remove(id);
                  for (int i = 0; i < ClassProgressState.ParagonSlotsPerTree; i++) d.Progress.UnslotParagon(tree, i);
                  return tree + " respec'd";
              })
            : ParagonUi.TryRespec(string.IsNullOrEmpty(tree) ? null : tree, out why) ? Done($"{(string.IsNullOrEmpty(tree) ? "all" : tree)} respec'd") : Result(false, why);

        // ---- helpers ----
        // one god write: the record changed, marked God (unless the edit is SetGod itself), saved, the bars told
        private static string Edit(Func<ClassPlayerData, string> change, bool mark = true, BarSide? refreshBars = null)
        {
            if (!Ready(out string why)) return Result(false, why);
            var data = Data;
            string what;
            try { what = change(data); } catch (Exception e) { return Result(false, "failed: " + e.Message); }
            if (mark) data.God = true;
            Save();
            if (refreshBars.HasValue && Local) BarController.NotifyBarsChanged(refreshBars.Value);
            FiresRPGClassesPlugin.Log.LogDebug("[God] " + what);
            return Result(true, what);
        }

        private static string Done(string what)
        {
            var data = Data;
            if (data != null) { data.God = true; Save(); }
            FiresRPGClassesPlugin.Log.LogDebug("[God] " + what);
            return Result(true, what);
        }

        private static bool Ready(out string why)
        {
            if (s_target != null) { why = null; return true; }
            why = Player.m_localPlayer == null || Data == null ? "no character in a world here"
                : ClassPlayerStore.LocalIsProtected ? "the class record is protected this session (see the log)" : null;
            return why == null;
        }

        // ---- 0.1.212 ([ghost] / [visual], Fire 10-30: the server-side Studio god clone has no local player; its class record lives in
        // GodProfile.Shadow.m_customData): the SAME edit methods on a record read from a customData dictionary and written back into it.
        // Only the record changes - the local bar HUD / Paragon window paths (BarController, ParagonUi) are local-player code and are
        // skipped for a target; the target path writes the same fields directly.
        public const string CustomDataKey = ClassPlayerStore.CustomDataKey;
        private static readonly JsonSerializerSettings TargetJson = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, ObjectCreationHandling = ObjectCreationHandling.Auto };
        [ThreadStatic] private static ClassPlayerData s_target;
        [ThreadStatic] private static IDictionary<string, string> s_targetStore;

        private static ClassPlayerData Data => s_target ?? ClassPlayerStore.Local;
        private static bool Local => s_target == null;

        private static void Save()
        {
            if (s_target == null) { ClassPlayerStore.SaveLocal(); return; }
            s_targetStore[CustomDataKey] = JsonConvert.SerializeObject(s_target, TargetJson);
        }

        /// <summary>Read a target's class record (a character's customData dictionary, e.g. GodProfile.Shadow.m_customData).</summary>
        public static string StateFor(IDictionary<string, string> customData) => With(customData, () => Result(true, ""));

        /// <summary>Run one GodApi method (by name, string args) on a target's record, then write it back into the dictionary.
        /// method = any public GodApi edit here ("SetLevel", "SetNodeRank", "SlotSkill" ...); SetWeapon needs a carried item (local only).</summary>
        public static string EditFor(IDictionary<string, string> customData, string method, string[] args)
        {
            if (string.Equals(method, nameof(SetWeapon), StringComparison.Ordinal)) return Result(false, "SetWeapon needs the local player's inventory; set the bar's weapon prefab on the clone's own gear");
            var m = typeof(GodApi).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .FirstOrDefault(x => x.Name == method && x.ReturnType == typeof(string) && x.GetParameters().Length >= (args?.Length ?? 0)
                    && x.GetParameters().All(p => p.ParameterType == typeof(string) || p.ParameterType == typeof(int) || p.ParameterType == typeof(long) || p.ParameterType == typeof(bool)));
            if (m == null) return Result(false, $"no GodApi method '{method}' taking {(args?.Length ?? 0)} plain argument(s)");
            var ps = m.GetParameters(); var call = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                string a = args != null && i < args.Length ? args[i] : null;
                if (a == null) { if (ps[i].HasDefaultValue) { call[i] = ps[i].DefaultValue; continue; } return Result(false, $"{method}: missing argument {ps[i].Name}"); }
                try { call[i] = Convert.ChangeType(a, ps[i].ParameterType, System.Globalization.CultureInfo.InvariantCulture); }
                catch { return Result(false, $"{method}: '{a}' is not a {ps[i].ParameterType.Name} for {ps[i].Name}"); }
            }
            return With(customData, () => (string)m.Invoke(null, call));
        }

        private static string With(IDictionary<string, string> customData, Func<string> body)
        {
            if (customData == null) return JsonConvert.SerializeObject(new { ok = false, why = "no customData", state = (object)null });
            var was = s_target; var wasStore = s_targetStore;
            try
            {
                ClassPlayerData d = null;
                if (customData.TryGetValue(CustomDataKey, out string raw) && !string.IsNullOrEmpty(raw)) d = JsonConvert.DeserializeObject<ClassPlayerData>(raw, TargetJson);
                s_target = d ?? new ClassPlayerData(); s_targetStore = customData;
                return body();
            }
            catch (Exception e) { return JsonConvert.SerializeObject(new { ok = false, why = "failed: " + e.Message, state = (object)null }); }
            finally { s_target = was; s_targetStore = wasStore; }
        }

        private static bool Side(string side, out BarSide s, out string why)
        {
            why = null; s = BarSide.Front;
            if (string.Equals(side, "front", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(side, "back", StringComparison.OrdinalIgnoreCase)) { s = BarSide.Back; return true; }
            why = "side must be front or back"; return false;
        }

        private static string Result(bool ok, string why)
        {
            object state = null;
            var data = Data;
            if ((s_target != null || Player.m_localPlayer != null) && data != null)
            {
                var p = data.Progress;
                object Bar(BarLayout b) => new { archetype = ClassNames.Of(b.Archetype), weapon = b.WeaponPrefab, offHand = b.OffHandPrefab, skills = b.SkillNodeIds.ToArray(), ultimate = b.UltimateId };
                state = new
                {
                    god = data.God, level = p.Level, xp = p.Xp, valorLevel = p.ValorLevel, valorXp = p.ValorXp, activeBar = data.ActiveBar.ToString(),
                    front = Bar(data.Front), back = Bar(data.Back),
                    talents = p.Talents.SelectMany(t => t.Value.Select(kv => new { id = kv.Key, archetype = ClassNames.Of(t.Key), rank = kv.Value, name = ClassTrees.Node(kv.Key)?.DisplayName ?? kv.Key, icon = "cls_" + kv.Key })).ToArray(),
                    attributes = Enum.GetValues(typeof(AttributeKind)).Cast<AttributeKind>().ToDictionary(k => k.ToString(), k => p.Attribute(k)),
                    bonusAttributePoints = p.BonusAttributePoints,
                    paragon = new { ranks = p.ParagonRanks, slots = p.ParagonSlotted },
                    free = new { skill = p.UnspentSkillPoints(Rates), attribute = p.UnspentAttributePoints(Rates), paragon = p.UnspentParagonPoints() },
                };
            }
            return JsonConvert.SerializeObject(new { ok, why = why ?? "", state });
        }
    }
}
