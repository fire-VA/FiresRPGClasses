using System.Collections.Generic;
using System.Linq;
using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresRPGClasses.Data;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Gap batch 1 (Tools\CLASS_KIT_DESIGN_GAPS.md): the placed skills. Design rules: real lingering zones where you aimed
    /// (Core's ClassZones; anyone entering or staying is affected), objects are a coloured ground ring + vanilla FX, and an
    /// unstated number follows the default rule. A zone's effect is re-applied every tick for a little over a tick, so it lasts
    /// while you stay inside and falls off soon after you leave. Numbers are the node's yaml values at the caster's rank.
    /// </summary>
    public static class ZoneKit
    {
        private const string ToxicCloudId = "rogue_toxic_cloud";
        private const string BlizzardId = "mage_blizzard";
        private const string EitrWellId = "mage_eitr_well";
        private const string WrathId = "paladin_wrath_of_the_aesir";
        private const string BannerId = "tank_rallying_banner";
        private const string WardingTotemId = "healer_warding_totem";

        private const float TickSeconds = 1f;
        // A little over one tick: the zone's effect holds between ticks and ends about half a second after leaving.
        private const float LingerSeconds = 1.5f;

        private const float WrathLightning = 80f;
        private const float WrathHoly = 40f;   // holy (blunt + half again as spirit)
        private const int WrathTargets = 5;

        private static readonly string[] WardByRank =
        {
            StatusEffectManager.EFFECT_CLASS_WARD10, StatusEffectManager.EFFECT_CLASS_WARD15, StatusEffectManager.EFFECT_CLASS_WARD20,
        };
        private static readonly string[] BannerByRank =
        {
            StatusEffectManager.EFFECT_CLASS_BANNER10, StatusEffectManager.EFFECT_CLASS_BANNER15, StatusEffectManager.EFFECT_CLASS_BANNER20,
        };
        private static readonly string[] EitrWellByRank =
        {
            StatusEffectManager.EFFECT_CLASS_EITR_WELL100, StatusEffectManager.EFFECT_CLASS_EITR_WELL150, StatusEffectManager.EFFECT_CLASS_EITR_WELL200,
        };

        private static bool _registered;

        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;
            ClassSkillKit.Register(ToxicCloudId, ToxicCloud);
            ClassSkillKit.Register(BlizzardId, Blizzard);
            ClassSkillKit.Register(EitrWellId, EitrWell);
            ClassSkillKit.Register(WrathId, Wrath);
            ClassSkillKit.Register(BannerId, Banner);
            ClassSkillKit.Register(WardingTotemId, WardingTotem);
            ClassSkillKit.Register(BeaconId, Beacon);
            ClassSkillKit.Register(FirestormId, Firestorm);
            ClassSkillKit.Register(VolleyId, Volley);
            ClassSkillKit.Register(SmokeBombId, SmokeBomb);
            ClassSkillKit.Register(RootsId, EntanglingRoots);
            ClassSkillKit.Register(ThornfieldId, Thornfield);
            ClassSkillKit.Register(GroveId, HeartOfTheGrove);
            ClassSkillKit.Register(GraveChillId, GraveChill);
        }

        // ---- the placed ultimates (the tree rank picks the value, the ultimate level scales it 50% -> 100%) ----

        private const string BeaconId = "paladin_beacon_of_light";
        private const string FirestormId = "mage_firestorm";
        private const string VolleyId = "ranger_volley";
        private const string SmokeBombId = "rogue_smoke_bomb";
        private const int VolleyArrows = 12;
        private const float VolleySeconds = 3f;

        private sealed class PendingVolley
        {
            public Character Caster;
            public Vector3 Point;
            public float Radius;
            public float Pierce;
            public int Left;
            public float Every;
            public float Next;
        }

        private static readonly System.Collections.Generic.List<PendingVolley> Volleys = new System.Collections.Generic.List<PendingVolley>();

        /// <summary>50% of the tree rank's value at ultimate level 1, 100% at level 100.</summary>
        /// <summary>(EARLY_MAGIC_GEAR_DESIGN §9.5 extended to the class kit): "holy" damage = the amount as blunt
        /// plus half again as spirit. Spirit alone does nothing to most creatures (vanilla: only undead and ghosts take it), so a
        /// holy hit lands on everything and undead take 1.5x.</summary>
        internal static void AddHoly(HitData hit, float amount)
        {
            if (hit == null || amount <= 0f) return;
            hit.m_damage.m_blunt += amount;
            hit.m_damage.m_spirit += amount * HolySpiritShare;
        }

        internal const float HolySpiritShare = 0.5f;

        internal static float UltimateScale(ClassTreeNode node)
        {
            int level = State.ClassPlayerStore.Local?.UltimateLevel(node.Id) ?? 1;
            return 0.5f + 0.5f * Mathf.Clamp01((level - 1) / (float)(UltimateCatalog.MaxUltimateLevel - 1));
        }

        /// <summary>Called every frame by ClassCombatDriver: Volley's arrows and Smoke Bomb's leavers.</summary>
        public static void Tick()
        {
            if (SmokeWatch.Count > 0) WatchSmoke();
            if (Volleys.Count == 0) return;
            float now = Time.time;
            for (int i = Volleys.Count - 1; i >= 0; i--)
            {
                PendingVolley volley = Volleys[i];
                if (volley.Caster == null || volley.Caster.IsDead() || volley.Left <= 0) { Volleys.RemoveAt(i); continue; }
                if (now < volley.Next) continue;
                volley.Next = now + volley.Every;
                volley.Left--;
                Arrow(volley);
            }
        }

        // "allies within 8 m of it regenerate {heal} of max health per second" for 10 s.
        private static bool Beacon(ClassTreeNode node, Character caster, CastTarget target)
        {
            float share = node.Value("heal", DesignKit.RankOf(node)) * UltimateScale(node);
            bool ok = Zone(node, caster, target, 8f, 10f, ZoneKind.Helpful, "vfx_HealthUpgrade", 2f,
                ally => AbilityHeals.Apply(caster, ally, ally.GetMaxHealth() * share));
            // The "Searing Beacon" morph also burns enemies inside for 10 holy damage a second (10 blunt + 5
            // spirit), over the same circle and time. Only while that morph is slotted and unlocked (ultimate level 25).
            if (ok && SlottedMorph(BeaconId) == SearingBeaconMorph)
            {
                float holy = SearingBeaconHoly * UltimateScale(node);
                Harmful(node, caster, target, 8f, 10f, "vfx_FireballHit", 1f, enemy =>
                {
                    HitData hit = HitFrom(caster, enemy);
                    AddHoly(hit, holy);
                    enemy.Damage(hit);
                });
                FiresRPGClassesPlugin.Log?.LogDebug($"[Ultimates] {BeaconId}: morph '{SearingBeaconMorph}' - enemies inside take {holy:0.#} holy a second ({holy:0.#} blunt + {holy * HolySpiritShare:0.#} spirit)");
            }
            return ok;
        }

        private const string SearingBeaconMorph = "Searing Beacon";
        private const float SearingBeaconHoly = 10f;

        /// <summary>The morph name slotted for <paramref name="ultimateId"/> on the local player's bars, or "" (none, or morphs not unlocked).</summary>
        internal static string SlottedMorph(string ultimateId)
        {
            State.ClassPlayerData data = State.ClassPlayerStore.Local;
            UltimateDefinition definition = UltimateCatalog.Get(ultimateId);
            if (data == null || definition == null || !UltimateCatalog.MorphsUnlocked(data.UltimateLevel(ultimateId))) return "";
            foreach (State.BarSide side in new[] { State.BarSide.Front, State.BarSide.Back })
            {
                State.BarLayout bar = data.Bar(side);
                if (bar.UltimateId != ultimateId) continue;
                int i = bar.UltimateMorph;
                return i >= 0 && i < definition.Morphs.Count ? definition.Morphs[i].Name : "";
            }
            return "";
        }

        // "a fire storm for 6 s deals {dps} fire damage per second" in a 6 m circle.
        private static bool Firestorm(ClassTreeNode node, Character caster, CastTarget target)
        {
            float dps = node.Value("dps", DesignKit.RankOf(node)) * UltimateScale(node);
            return Harmful(node, caster, target, 6f, 6f, "vfx_FireballHit", 1f, enemy =>
            {
                HitData hit = HitFrom(caster, enemy);
                hit.m_damage.m_fire = dps;
                enemy.Damage(hit);
            });
        }

        // "12 arrows fall over 3 s, {pierce} pierce damage each" in a 5 m circle: each lands on an enemy in the circle when
        // there is one (a random one), else on a random point in it.
        private static bool Volley(ClassTreeNode node, Character caster, CastTarget target)
        {
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : 5f;
            Vector3 point = target.Resolved ? target.Point : caster.transform.position;
            Volleys.Add(new PendingVolley
            {
                Caster = caster,
                Point = point,
                Radius = radius,
                Pierce = node.Value("pierce", DesignKit.RankOf(node)) * UltimateScale(node),
                Left = VolleyArrows,
                Every = VolleySeconds / VolleyArrows,
                Next = Time.time,
            });
            TargetingController.ShowCastRing(node, point, VolleySeconds);
            return true;
        }

        private static void Arrow(PendingVolley volley)
        {
            var inside = Character.GetAllCharacters()
                .Where(enemy => enemy != null && !enemy.IsDead() && Vector3.Distance(enemy.transform.position, volley.Point) <= volley.Radius
                    && ClassTargeting.IsEnemyTarget(volley.Caster, enemy))
                .ToList();
            if (inside.Count == 0)
            {
                Vector2 offset = Random.insideUnitCircle * volley.Radius;
                Vector3 scatter = new Vector3(offset.x, 0f, offset.y);   // off the halo ring: world XZ, unchanged
                if (Ring.InField(volley.Point))
                {
                    Vector3 up = Ring.Up(volley.Point);
                    Vector3 t1 = Vector3.ProjectOnPlane(Mathf.Abs(Vector3.Dot(up, Vector3.forward)) < 0.9f ? Vector3.forward : Vector3.right, up).normalized;
                    Vector3 t2 = Vector3.Cross(up, t1);
                    scatter = t1 * offset.x + t2 * offset.y;
                }
                AbilityRPCManager.SpawnFX(volley.Point + scatter, "vfx_arrowhit");
                return;
            }
            Character struck = inside[Random.Range(0, inside.Count)];
            AbilityRPCManager.SpawnFX(struck.transform.position, "vfx_arrowhit");
            HitData hit = HitFrom(volley.Caster, struck);
            hit.m_damage.m_pierce = volley.Pierce;
            struck.Damage(hit);
        }

        // "a 6 m smoke cloud for 8 s. Enemies inside lose their target; you and allies leaving it gain Stealth for {stealth}."
        private static bool SmokeBomb(ClassTreeNode node, Character caster, CastTarget target)
        {
            float stealth = node.Value("stealth", DesignKit.RankOf(node)) * UltimateScale(node);
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : 6f;
            float duration = node.EffectDuration > 0f ? node.EffectDuration : 8f;
            Vector3 point = target.Resolved ? target.Point : caster.transform.position;
            var wasInside = new System.Collections.Generic.HashSet<Character>();

            ClassZone smoke = ClassZones.ZoneAt(caster, point, radius, duration, ZoneKind.Harmful,
                enemy => AbilityRPCManager.ApplySingleEffect(caster, enemy, StatusEffectManager.EFFECT_CLASS_LOSE_TARGET, LingerSeconds),
                TickSeconds, "vfx_odin_despawn", 1.5f);
            if (smoke == null) return false;
            // The party side: whoever was inside and is now out gets Stealth once; the cloud ending counts as leaving it.
            ClassZones.ZoneAt(caster, point, radius, duration + TickSeconds, ZoneKind.Helpful, ally => wasInside.Add(ally), TickSeconds * 0.5f);
            SmokeWatch.Add(new SmokeLeaving { Caster = caster, Point = point, Radius = radius, Until = Time.time + duration + TickSeconds, Stealth = stealth, Inside = wasInside });
            TargetingController.ShowCastRing(node, point, duration);
            return true;
        }

        private static readonly System.Collections.Generic.List<Character> SmokeBuffer = new System.Collections.Generic.List<Character>();

        private sealed class SmokeLeaving
        {
            public Character Caster;
            public Vector3 Point;
            public float Radius;
            public float Until;
            public float Stealth;
            public System.Collections.Generic.HashSet<Character> Inside;
        }

        private static readonly System.Collections.Generic.List<SmokeLeaving> SmokeWatch = new System.Collections.Generic.List<SmokeLeaving>();

        // Called from Tick: an ally seen inside the smoke who is now out of it (or the smoke ended) gains Stealth.
        private static void WatchSmoke()
        {
            float now = Time.time;
            for (int i = SmokeWatch.Count - 1; i >= 0; i--)
            {
                SmokeLeaving smoke = SmokeWatch[i];
                bool over = smoke.Caster == null || now >= smoke.Until;
                // A reused buffer: Inside.ToList() would allocate a list every frame while a cloud is up.
                SmokeBuffer.Clear(); SmokeBuffer.AddRange(smoke.Inside);
                foreach (Character ally in SmokeBuffer)
                {
                    if (ally == null || ally.IsDead()) { smoke.Inside.Remove(ally); continue; }
                    if (!over && Vector3.Distance(ally.transform.position, smoke.Point) <= smoke.Radius) continue;
                    smoke.Inside.Remove(ally);
                    if (smoke.Caster != null) AbilityRPCManager.ApplySingleEffect(smoke.Caster, ally, StatusEffectManager.EFFECT_STEALTH, smoke.Stealth);
                }
                if (over) SmokeWatch.RemoveAt(i);
            }
        }

        // "a 5 m cloud for 8 s deals {dps} poison per second and slows enemies 20%."
        private static bool ToxicCloud(ClassTreeNode node, Character caster, CastTarget target)
        {
            float dps = node.Value("dps", DesignKit.RankOf(node));
            return Harmful(node, caster, target, 5f, 8f, "vfx_BombBlob_explode_poison", 2f, enemy =>
            {
                HitData hit = HitFrom(caster, enemy);
                hit.m_damage.m_poison = dps;
                enemy.Damage(hit);
                AbilityRPCManager.ApplySingleEffect(caster, enemy, StatusEffectManager.EFFECT_CLASS_SLOW20, LingerSeconds);
            });
        }

        // "an 8 m storm within 30 m for 8 s: {dps} frost damage per second and a 30% slow."
        private static bool Blizzard(ClassTreeNode node, Character caster, CastTarget target)
        {
            float dps = node.Value("dps", DesignKit.RankOf(node));
            return Harmful(node, caster, target, 8f, 8f, "vfx_ice_destroyed", 1f, enemy =>
            {
                HitData hit = HitFrom(caster, enemy);
                hit.m_damage.m_frost = dps;
                enemy.Damage(hit);
                AbilityRPCManager.ApplySingleEffect(caster, enemy, StatusEffectManager.EFFECT_CLASS_SLOW30, LingerSeconds);
            });
        }

        // "a well within 20 m for 12 s: allies within 6 m of it regenerate eitr {regen} faster."
        private static bool EitrWell(ClassTreeNode node, Character caster, CastTarget target) =>
            Helpful(node, caster, target, 6f, 12f, "fx_DvergerMage_Nova_ring", 2f, ByRank(EitrWellByRank, node));

        // "Plant a war banner within 15 m. Allies within 8 m of it take {dr} less damage and regain stamina {regen} faster."
        // (15 s, the node's duration.)
        private static bool Banner(ClassTreeNode node, Character caster, CastTarget target) =>
            Helpful(node, caster, target, 8f, 15f, "vfx_MeadBzerker", 2.5f, ByRank(BannerByRank, node));

        // "a totem within 20 m for 15 s: allies within 6 m of it take {dr} less damage."
        private static bool WardingTotem(ClassTreeNode node, Character caster, CastTarget target) =>
            Helpful(node, caster, target, 6f, 15f, "fx_shaman_protect", 3f, ByRank(WardByRank, node));

        // "up to 5 enemies within 6 m of that point take 80 lightning and 40 spirit damage each": the 5 nearest the point.
        private static bool Wrath(ClassTreeNode node, Character caster, CastTarget target)
        {
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : 6f;
            Vector3 point = target.Point;
            AbilityRPCManager.SpawnFX(point, "fx_chainlightning_hit");
            TargetingController.ShowCastRing(node, point, 1f);
            var struck = Character.GetAllCharacters()
                .Where(enemy => enemy != null && !enemy.IsDead() && Vector3.Distance(enemy.transform.position, point) <= radius
                    && ClassTargeting.IsEnemyTarget(caster, enemy))
                .OrderBy(enemy => Vector3.Distance(enemy.transform.position, point))
                .Take(WrathTargets)
                .ToList();
            foreach (Character enemy in struck)
            {
                AbilityRPCManager.SpawnFX(enemy.transform.position, "fx_Lightning");
                HitData hit = HitFrom(caster, enemy);
                hit.m_damage.m_lightning = WrathLightning;
                AddHoly(hit, WrathHoly);   // 40 holy = 40 blunt + 20 spirit (spirit alone does nothing to non-undead)
                enemy.Damage(hit);
            }
            return true;
        }

        // ---- Druid / Necromancer talents ---------------------------------------------------------------------------------------
        // Core has 20 / 30 / 60% slows; a rank's slow takes the strongest preset not above it (45% -> 30%, 25% -> 20%).
        private const string RootsId = "druid_entangling_roots";
        private const string ThornfieldId = "druid_thornfield";
        private const string GroveId = "druid_heart_of_the_grove";
        private const string GraveChillId = "necromancer_grave_chill";
        private const float GroveHealShare = 0.04f, GroveRootSeconds = 2f, GraveChillFrost = 2f;

        private static string SlowFor(float share) =>
            share >= 0.6f - 0.001f ? StatusEffectManager.EFFECT_CLASS_SLOW60
            : share >= 0.3f - 0.001f ? StatusEffectManager.EFFECT_CLASS_SLOW30
            : StatusEffectManager.EFFECT_CLASS_SLOW20;

        // "enemies in a 5 m circle are slowed {slow} for 6 s."
        private static bool EntanglingRoots(ClassTreeNode node, Character caster, CastTarget target)
        {
            string slow = SlowFor(node.Value("slow", DesignKit.RankOf(node)));
            return Harmful(node, caster, target, 5f, 6f, "fx_gdking_rootspawn", 2f,
                enemy => AbilityRPCManager.ApplySingleEffect(caster, enemy, slow, LingerSeconds));
        }

        // "enemies inside take {dmg} pierce damage a second for 8 s."
        private static bool Thornfield(ClassTreeNode node, Character caster, CastTarget target)
        {
            float dmg = node.Value("dmg", DesignKit.RankOf(node));
            return Harmful(node, caster, target, 6f, 8f, "fx_gdking_rootspawn", 2f, enemy =>
            {
                HitData hit = HitFrom(caster, enemy);
                hit.m_damage.m_pierce = dmg;
                enemy.Damage(hit);
            });
        }

        // "allies inside heal 4% a second and enemies inside are rooted for 2 s when they enter." Two zones on one spot: the helpful
        // one heals, the harmful one roots each enemy the first tick it is inside.
        private static bool HeartOfTheGrove(ClassTreeNode node, Character caster, CastTarget target)
        {
            bool healing = Zone(node, caster, target, 8f, 12f, ZoneKind.Helpful, "fx_greenroots_projectile_hit", 2f,
                ally => ally.Heal(ally.GetMaxHealth() * GroveHealShare, true));
            var rooted = new HashSet<ZDOID>();
            bool rooting = Zone(node, caster, target, 8f, 12f, ZoneKind.Harmful, null, 0f, enemy =>
            {
                if (rooted.Add(enemy.GetZDOID())) AbilityRPCManager.ApplySingleEffect(caster, enemy, StatusEffectManager.EFFECT_ROOTED, GroveRootSeconds);
            });
            return healing || rooting;
        }

        // "enemies in a 5 m circle are slowed {slow} and frosted for 6 s."
        private static bool GraveChill(ClassTreeNode node, Character caster, CastTarget target)
        {
            string slow = SlowFor(node.Value("slow", DesignKit.RankOf(node)));
            return Harmful(node, caster, target, 5f, 6f, "fx_fenring_frost", 1f, enemy =>
            {
                HitData hit = HitFrom(caster, enemy);
                hit.m_damage.m_frost = GraveChillFrost;   // a little frost each tick: vanilla's Frost on the target
                enemy.Damage(hit);
                AbilityRPCManager.ApplySingleEffect(caster, enemy, slow, LingerSeconds);
            });
        }

        // ---- helpers ---------------------------------------------------------------------------------------------

        private static bool Harmful(ClassTreeNode node, Character caster, CastTarget target, float defaultRadius, float defaultDuration,
            string fx, float fxSeconds, System.Action<Character> onTick) =>
            Zone(node, caster, target, defaultRadius, defaultDuration, ZoneKind.Harmful, fx, fxSeconds, onTick);

        private static bool Helpful(ClassTreeNode node, Character caster, CastTarget target, float defaultRadius, float defaultDuration,
            string fx, float fxSeconds, string effect) =>
            Zone(node, caster, target, defaultRadius, defaultDuration, ZoneKind.Helpful, fx, fxSeconds,
                ally => AbilityRPCManager.ApplySingleEffect(caster, ally, effect, LingerSeconds));

        private static bool Zone(ClassTreeNode node, Character caster, CastTarget target, float defaultRadius, float defaultDuration,
            ZoneKind kind, string fx, float fxSeconds, System.Action<Character> onTick)
        {
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : defaultRadius;
            float duration = node.EffectDuration > 0f ? node.EffectDuration : defaultDuration;
            Vector3 point = target.Resolved ? target.Point : caster.transform.position;
            ClassZone zone = ClassZones.ZoneAt(caster, point, radius, duration, kind, onTick, TickSeconds, fx, fxSeconds);
            if (zone != null) TargetingController.ShowCastRing(node, point, duration);
            return zone != null;
        }

        private static string ByRank(string[] presets, ClassTreeNode node) =>
            presets[Mathf.Clamp(DesignKit.RankOf(node), 1, presets.Length) - 1];

        private static HitData HitFrom(Character caster, Character enemy)
        {
            Vector3 away = enemy.transform.position - caster.transform.position;
            away = Ring.Flat(away, enemy.transform.position);   // off the halo ring: away.y = 0
            var hit = new HitData
            {
                m_point = enemy.GetCenterPoint(),
                m_dir = away.sqrMagnitude > 0.01f ? away.normalized : caster.transform.forward,
            };
            hit.SetAttacker(caster);
            return hit;
        }
    }
}
