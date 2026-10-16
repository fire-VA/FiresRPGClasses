using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresRPGClasses.Data;
using FiresRPGClasses.State;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Druid / Necromancer actives that need no new Core effect (0.1.80, Fire 10-08: build the "Not yet implemented" talents, group 4):
    /// - Regrowth: the ally under the crosshair (or you) heals {heal} over 10 s, a tenth each second;
    /// - Barkskin: {dr} less damage taken for 10 s (Core's class ward at the nearest preset not above it);
    /// - Bone Wall: a 6 m wall within 20 m, facing you, that blocks enemies and projectiles for {time} s (three vanilla stone walls,
    ///   no drops, removed by its creator's peer).
    /// Ghostwalk lives in PossessionKit (it is a possession).
    /// </summary>
    public static class DruidNecroKit
    {
        private const string RegrowthId = "druid_regrowth";
        private const string BarkskinId = "druid_barkskin";
        private const string BoneWallId = "necromancer_bone_wall";
        private const string WitherId = "necromancer_wither";
        private const string PestilenceId = "necromancer_grave_pestilence";
        // Core 0.2.338's debuffs, by name (an older Core has no such effect: the apply is refused and the poison still lands).
        internal const string Withered = "ClassWithered";
        internal const string Wounded = "ClassWounded";
        private const float WitherSeconds = 10f, PestilenceRadius = 8f, PestilenceSeconds = 10f, ZoneLinger = 1.5f;
        private const float RegrowthSeconds = 10f, BarkskinSeconds = 10f;
        private const string WallPiece = "stone_wall_2x1";
        private const float WallSegment = 2f;
        private static bool _registered;

        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;
            ClassSkillKit.Register(RegrowthId, Regrowth);
            ClassSkillKit.Register(BarkskinId, Barkskin);
            ClassSkillKit.Register(BoneWallId, BoneWall);
            ClassSkillKit.Register(WitherId, Wither);
            ClassSkillKit.Register(PestilenceId, GravePestilence);
            ClassSkillKit.Register(PartialFormId, PartialForm);
            ClassSkillKit.Register(LichFormId, LichForm);
            ClassSkillKit.Register(HowlId, AlphaHowl);
            ClassSkillKit.Register(WildHuntId, WildHunt);
            ClassSkillKit.Register(LegionId, Legion);
        }

        private static bool Regrowth(ClassTreeNode node, Character caster, CastTarget target)
        {
            Character ally = target.TargetCharacter != null && !target.TargetCharacter.IsDead() && !ClassTargeting.IsEnemyTarget(caster, target.TargetCharacter)
                ? target.TargetCharacter : caster;
            float total = node.Value("heal", DesignKit.RankOf(node), 20f);
            caster.StartCoroutine(HealOverTime(ally, total));
            AbilityRPCManager.SpawnFX(ally.transform.position, "vfx_Potion_health_medium");
            FiresRPGClassesPlugin.Log?.LogDebug($"[DruidNecroKit] {RegrowthId}: {ally.m_name} heals {total:0} over {RegrowthSeconds:0} s");
            return true;
        }

        private static IEnumerator HealOverTime(Character ally, float total)
        {
            float each = total / RegrowthSeconds;
            for (int i = 0; i < (int)RegrowthSeconds; i++)
            {
                yield return new WaitForSeconds(1f);
                if (ally == null || ally.IsDead()) yield break;
                ally.Heal(each, true);
            }
        }

        private static bool Barkskin(ClassTreeNode node, Character caster, CastTarget target)
        {
            float dr = node.Value("dr", DesignKit.RankOf(node), 0.15f);
            string ward = dr >= 0.25f - 0.001f ? StatusEffectManager.EFFECT_CLASS_WARD25
                : dr >= 0.2f - 0.001f ? StatusEffectManager.EFFECT_CLASS_WARD20
                : dr >= 0.15f - 0.001f ? StatusEffectManager.EFFECT_CLASS_WARD15
                : StatusEffectManager.EFFECT_CLASS_WARD10;
            bool on = AbilityRPCManager.ApplySelfBuff(caster, ward, BarkskinSeconds);
            if (on) AbilityRPCManager.SpawnFX(caster.transform.position, "fx_shaman_protect");
            FiresRPGClassesPlugin.Log?.LogDebug($"[DruidNecroKit] {BarkskinId}: {(on ? ward : "refused")} for {BarkskinSeconds:0} s");
            return on;
        }

        private static bool BoneWall(ClassTreeNode node, Character caster, CastTarget target)
        {
            GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(WallPiece) : null;
            if (prefab == null || !target.Resolved) return false;
            float seconds = node.Value("time", DesignKit.RankOf(node), 8f);
            Vector3 toCaster = caster.transform.position - target.Point;
            toCaster.y = 0f;
            Quaternion facing = Quaternion.LookRotation(toCaster.sqrMagnitude > 0.01f ? toCaster.normalized : -caster.transform.forward);
            Vector3 side = facing * Vector3.right;
            var made = new List<GameObject>();
            for (int i = -1; i <= 1; i++)
            {
                Vector3 at = target.Point + side * (i * WallSegment);
                if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(at, out float ground)) at.y = ground;
                var go = Object.Instantiate(prefab, at, facing);
                foreach (var wear in go.GetComponentsInChildren<WearNTear>()) wear.m_noRoofWear = true;
                BoneWallLook.MarkAndDress(go);   // 0.1.99 (Fire 10-12): bones, not stone - same collider and health
                made.Add(go);
            }
            AbilityRPCManager.SpawnFX(target.Point, "vfx_skeleton_death");
            caster.StartCoroutine(RemoveAfter(made, seconds));
            FiresRPGClassesPlugin.Log?.LogDebug($"[DruidNecroKit] {BoneWallId}: a {made.Count * WallSegment:0} m wall at {target.Point} for {seconds:0} s");
            return true;
        }

        // "Curses the enemy under your crosshair: {dmg} poison damage a second for 10 s." The curse is Withered (+15% damage taken,
        // 20% slow); the poison is vanilla's (the hit's poison runs as its own damage over time).
        private static bool Wither(ClassTreeNode node, Character caster, CastTarget target)
        {
            Character enemy = target.TargetCharacter;
            if (enemy == null || enemy.IsDead() || !ClassTargeting.IsEnemyTarget(caster, enemy)) return false;
            ApplyWither(caster, enemy, node.Value("dmg", DesignKit.RankOf(node), 4f));
            return true;
        }

        /// <summary>Wither on one enemy: the curse and the poison; Plague Bearer watches it (DruidNecroPassives).</summary>
        internal static void ApplyWither(Character caster, Character enemy, float dps)
        {
            AbilityRPCManager.ApplySingleEffect(caster, enemy, Withered, WitherSeconds);
            Vector3 away = enemy.transform.position - caster.transform.position;
            away.y = 0f;
            var hit = new HitData { m_point = enemy.GetCenterPoint(), m_dir = away.sqrMagnitude > 0.01f ? away.normalized : caster.transform.forward };
            hit.SetAttacker(caster);
            hit.m_damage.m_poison = dps * WitherSeconds;
            enemy.Damage(hit);
            AbilityRPCManager.SpawnFX(enemy.transform.position, "vfx_BombBlob_explode_poison");
            DruidNecroPassives.Withering(enemy);
            FiresRPGClassesPlugin.Log?.LogDebug($"[DruidNecroKit] {WitherId}: {enemy.m_name} withered, {dps:0} poison a second for {WitherSeconds:0} s");
        }

        /// <summary>The learned Wither's poison per second (Plague Bearer's jump uses it), or its rank 1 value.</summary>
        internal static float WitherDps()
        {
            ClassTreeNode node = ClassTrees.Node(WitherId);
            return node == null ? 4f : node.Value("dmg", Mathf.Max(1, DesignKit.RankOf(node)), 4f);
        }

        // "A cloud of pestilence for 10 s: every enemy inside is Withered and Wounded."
        private static bool GravePestilence(ClassTreeNode node, Character caster, CastTarget target)
        {
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : PestilenceRadius;
            float duration = node.EffectDuration > 0f ? node.EffectDuration : PestilenceSeconds;
            Vector3 point = target.Resolved ? target.Point : caster.transform.position;
            ClassZone zone = ClassZones.ZoneAt(caster, point, radius, duration, ZoneKind.Harmful, enemy =>
            {
                AbilityRPCManager.ApplySingleEffect(caster, enemy, Withered, ZoneLinger);
                AbilityRPCManager.ApplySingleEffect(caster, enemy, Wounded, ZoneLinger);
                DruidNecroPassives.Withering(enemy);
            }, 1f, "vfx_BombBlob_explode_poison", 2f);
            if (zone != null) TargetingController.ShowCastRing(node, point, duration);
            return zone != null;
        }

        // ---- group 5 (0.1.81) -------------------------------------------------------------------------------------------
        // Partial Form: "For 30 s take a library form's movement, health and dodge while keeping your own weapons and skills."
        // Approximation until a real half-form exists: a 20% haste and a 15% ward for 30 s, and only with a form in the library.
        private const string PartialFormId = "druid_partial_form";
        private const float PartialSeconds = 30f;

        private static bool PartialForm(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player)) return false;
            if (PossessionKit.Forms(player).Count == 0) { player.Message(MessageHud.MessageType.Center, node.DisplayName + ": no form remembered yet: Wildshape first"); return false; }
            float seconds = node.EffectDuration > 0f ? node.EffectDuration : PartialSeconds;
            bool haste = AbilityRPCManager.ApplySelfBuff(caster, StatusEffectManager.EFFECT_CLASS_HASTE20, seconds);
            bool ward = AbilityRPCManager.ApplySelfBuff(caster, StatusEffectManager.EFFECT_CLASS_WARD15, seconds);
            FiresRPGClassesPlugin.Log?.LogDebug($"[DruidNecroKit] {PartialFormId}: {PossessionKit.Forms(player)[0]}'s pace and hide for {seconds:0} s (haste {haste}, ward {ward})");
            return haste || ward;
        }

        // Lich Form: "For {time} s your own body becomes undead: you float, your spells cost health instead of eitr, and you take
        // {dr} less damage." The ward is Core's class ward by rank; the spell cost goes through ClassEitrCostHooks (LichPays). The
        // float is not built yet (base only).
        private const string LichFormId = "necromancer_lich_form";
        private static float _lichUntil;

        private static bool LichForm(ClassTreeNode node, Character caster, CastTarget target)
        {
            int rank = DesignKit.RankOf(node);
            float seconds = node.Value("time", rank, 12f);
            float dr = node.Value("dr", rank, 0.1f);
            string ward = dr >= 0.2f - 0.001f ? StatusEffectManager.EFFECT_CLASS_WARD20
                : dr >= 0.15f - 0.001f ? StatusEffectManager.EFFECT_CLASS_WARD15 : StatusEffectManager.EFFECT_CLASS_WARD10;
            bool on = AbilityRPCManager.ApplySelfBuff(caster, ward, seconds);
            if (on) { _lichUntil = Time.time + seconds; AbilityRPCManager.SpawnFX(caster.transform.position, "vfx_ghost_death"); }
            FiresRPGClassesPlugin.Log?.LogDebug($"[DruidNecroKit] {LichFormId}: {(on ? ward + ", spells cost health" : "refused")} for {seconds:0} s");
            return on;
        }

        /// <summary>For FiresClassTest: Lich Form is running.</summary>
        public static bool LichActive => Time.time < _lichUntil;

        /// <summary>ClassEitrCostHooks: in Lich Form a spell's eitr is paid in health (never the last point); true = paid, skip the eitr.</summary>
        internal static bool LichPays(Player player, float eitr)
        {
            if (player == null || player != Player.m_localPlayer || !LichActive || eitr <= 0f) return false;
            if (player.GetHealth() <= eitr + 1f) return false;
            player.SetHealth(player.GetHealth() - eitr);
            return true;
        }

        // ---- group 6 (0.1.81) -------------------------------------------------------------------------------------------
        // Alpha Howl: "You, your allies and your summons within 15 m deal {dmg} more damage for 10 s." Your hits and your summons'
        // get the bonus on your peer (DruidNecroPassives' Character.Damage hook); other players in range get Core's 25% damage
        // preset only when the rank's bonus reaches 25% (below that they get none yet: Core has no smaller preset).
        private const string HowlId = "druid_alpha_howl";
        private const float HowlRadius = 15f, HowlSeconds = 10f;
        private static float _howlUntil, _howlBonus;

        private static bool AlphaHowl(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player)) return false;
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : HowlRadius;
            float seconds = node.EffectDuration > 0f ? node.EffectDuration : HowlSeconds;
            _howlBonus = node.Value("dmg", DesignKit.RankOf(node), 0.1f);
            _howlUntil = Time.time + seconds;
            int allies = 0;
            if (_howlBonus >= 0.25f - 0.001f)
                foreach (Player other in Player.GetAllPlayers())
                    if (other != null && other != player && !other.IsDead() && Vector3.Distance(other.transform.position, player.transform.position) <= radius
                        && AbilityRPCManager.ApplySingleEffect(caster, other, StatusEffectManager.EFFECT_CLASS_DAMAGE25, seconds)) allies++;
            AbilityRPCManager.SpawnFX(player.transform.position, "fx_GoblinShaman_shout");
            FiresRPGClassesPlugin.Log?.LogDebug($"[DruidNecroKit] {HowlId}: +{_howlBonus:P0} damage for you and your summons for {seconds:0} s; {allies} other player(s) roused");
            return true;
        }

        /// <summary>For FiresClassTest: Alpha Howl's bonus while it runs, else 0.</summary>
        public static float HowlBonus => Time.time < _howlUntil ? _howlBonus : 0f;

        // Wild Hunt: "You and your pack sprint and hunt-mark every creature within 40 m." The sprint is Core's 20% haste on you and
        // your summons; the mark is the Rogue's Death Mark (+30% damage taken) on every enemy in range, for 15 s.
        private const string WildHuntId = "druid_wild_hunt";
        private const float HuntRadius = 40f, HuntSeconds = 15f;

        private static bool WildHunt(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player)) return false;
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : HuntRadius;
            AbilityRPCManager.ApplySelfBuff(caster, StatusEffectManager.EFFECT_CLASS_HASTE20, HuntSeconds);
            int pack = 0, marked = 0;
            foreach (FiresCore.Summon.SummonInfo s in FiresCore.Summon.SummonApi.MySummons())
            {
                GameObject go = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(s.Id) : null;
                Character c = go != null ? go.GetComponent<Character>() : null;
                if (c != null && AbilityRPCManager.ApplySingleEffect(caster, c, StatusEffectManager.EFFECT_CLASS_HASTE20, HuntSeconds)) pack++;
            }
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null || c.IsDead() || !ClassTargeting.IsEnemyTarget(caster, c)) continue;
                if (Vector3.Distance(c.transform.position, player.transform.position) > radius) continue;
                if (AbilityRPCManager.ApplySingleEffect(caster, c, StatusEffectManager.EFFECT_DEATH_MARK, HuntSeconds)) marked++;
            }
            AbilityRPCManager.SpawnFX(player.transform.position, "fx_GoblinShaman_shout");
            FiresRPGClassesPlugin.Log?.LogDebug($"[DruidNecroKit] {WildHuntId}: sprint for you + {pack} summon(s), {marked} creature(s) hunt-marked");
            return true;
        }

        // Legion: "Every undead within 30 m below 50% health becomes your thrall for 30 s." Each one falls and rises as your summon
        // (Core SummonApi, its own cap group of 10, the Legion's timer); a beaten undead you can't raise stays as it was.
        private const string LegionId = "necromancer_legion";
        private const float LegionRadius = 30f, LegionSeconds = 30f, LegionHealth = 0.5f;
        private const int LegionCap = 10;   // 0.1.131: the fallback of [13 - Balance] "Legion server limit" (synced, performance)
        private static int LegionLimit => Mathf.Max(1, BalanceConfig.Value(BalanceConfig.LegionServerMax, LegionCap));

        private static bool Legion(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (!(caster is Player player)) return false;
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : LegionRadius;
            var options = new FiresCore.Summon.SummonOptions
            {
                Duration = LegionSeconds,
                Mode = FiresCore.Summon.SummonMode.Follow,
                CapGroup = "rpg-legion",
                Cap = LegionLimit,
                SourceTag = LegionId,
            };
            int raised = 0;
            string why = null;
            foreach (Character c in Character.GetAllCharacters().ToArray())
            {
                if (c == null || c.IsDead() || c.IsTamed() || c.GetFaction() != Character.Faction.Undead || !(c is Humanoid)) continue;
                if (c.GetHealthPercentage() >= LegionHealth || Vector3.Distance(c.transform.position, player.transform.position) > radius) continue;
                string prefab = global::Utils.GetPrefabName(c.gameObject);
                Vector3 at = c.transform.position;
                ZDOID id = FiresCore.Summon.SummonApi.Summon(prefab, options, out why);
                if (id.IsNone()) break;
                ZNetView nview = c.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
                ZNetScene.instance?.Destroy(c.gameObject);
                GameObject go = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(id) : null;
                if (go != null) go.transform.position = at;
                AbilityRPCManager.SpawnFX(at, "vfx_skeleton_death");
                raised++;
            }
            FiresRPGClassesPlugin.Log?.LogDebug($"[DruidNecroKit] {LegionId}: {raised} thrall(s) for {LegionSeconds:0} s{(raised == 0 && why != null ? " (" + why + ")" : "")}");
            if (raised == 0) player.Message(MessageHud.MessageType.Center, node.DisplayName + ": no beaten undead within " + radius.ToString("0") + " m");
            return raised > 0;
        }

        private static IEnumerator RemoveAfter(List<GameObject> parts, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            foreach (GameObject go in parts)
            {
                if (go == null) continue;
                var nview = go.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid() && !nview.IsOwner()) nview.ClaimOwnership();
                ZNetScene.instance?.Destroy(go);
            }
        }
    }
}
