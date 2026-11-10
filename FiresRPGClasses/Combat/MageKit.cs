using System.Linq;
using FiresCore.Npc.Archetypes;
using FiresCore.Npc.Archetypes.Effects;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresRPGClasses.Data;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// Mage aimed skills: Ice Lance and Arcane Torrent. Both are instant ray hits along the caster's aim (the camera for the
    /// local player, the eye toward the cast point otherwise), not spawned projectiles; damage goes through Character.Damage,
    /// which vanilla routes to the target's owner. Numbers are the node's yaml values at the caster's rank.
    /// </summary>
    public static class MageKit
    {
        private const string IceLanceId = "mage_ice_lance";
        private const string TorrentId = "mage_arcane_torrent";

        private const float IceLanceRange = 40f;
        // "a 40% slow for 3 s": Core has no 40% preset, so ClassSlow30 (listed under "Defaults used").
        private const float IceLanceSlowSeconds = 3f;
        private const int IceLanceSilenceRank = 3;
        private const float IceLanceSilenceSeconds = 2f;
        private const float TorrentRange = 25f;
        private const float TorrentSeconds = 4f;
        private const float TorrentTickSeconds = 0.5f;
        private const float TorrentTickDamage = 30f;
        private const float TorrentEitrPerSecond = 15f;
        private const float TorrentSilenceSeconds = 1.5f;

        private static bool _registered;
        private static readonly FiresCore.Logging.FiresLog DebugLog = new FiresCore.Logging.FiresLog("FiresRPGClasses", () => false);

        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;
            ClassSkillKit.Register(IceLanceId, IceLance);
            ClassSkillKit.Register(TorrentId, Torrent);
        }

        // "A piercing frost bolt: {frost} frost damage and a 40% slow for 3 s." Rank 3: "Silences what it hits for 2 s."
        private static bool IceLance(ClassTreeNode node, Character caster, CastTarget target)
        {
            int rank = DesignKit.RankOf(node);
            float range = node.Targeting.Range > 0f ? node.Targeting.Range : IceLanceRange;
            Character enemy = AimedEnemy(caster, target, range, out Vector3 point);
            AbilityRPCManager.SpawnFX(point, "vfx_ice_destroyed");
            if (enemy == null) return false;
            HitData hit = HitFrom(caster, enemy);
            hit.m_damage.m_frost = node.Value("frost", rank, 40f);
            enemy.Damage(hit);
            AbilityRPCManager.ApplySingleEffect(caster, enemy, StatusEffectManager.EFFECT_CLASS_SLOW30, IceLanceSlowSeconds);
            bool silenced = rank >= IceLanceSilenceRank
                && CcKit.SilenceSoft(enemy, caster, node.Value("silence", rank, IceLanceSilenceSeconds));
            DebugLog.Debug($"[MageKit] Ice Lance hit {enemy.m_name}{(silenced ? ", silenced" : "")}");
            return true;
        }

        // "Channel a beam for up to 4 s: 30 lightning damage every 0.5 s to the first enemy it hits." Rank 1: "The beam's first
        // tick Silences its target for 1.5 s." Ticks on the combat driver, re-aiming each tick; ends on death, stagger or eitr.
        private static bool Torrent(ClassTreeNode node, Character caster, CastTarget target)
        {
            if (ClassCombatDriver.Instance == null) return false;
            ClassCombatDriver.Instance.StartCoroutine(TorrentRoutine(node, caster, target));
            return true;
        }

        private static System.Collections.IEnumerator TorrentRoutine(ClassTreeNode node, Character caster, CastTarget target)
        {
            int rank = DesignKit.RankOf(node);
            float range = node.Targeting.Range > 0f ? node.Targeting.Range : TorrentRange;
            float duration = node.Value("duration", rank, TorrentSeconds);
            float tick = node.Value("tick", rank, TorrentTickSeconds);
            float damage = node.Value("dmg", rank, TorrentTickDamage);
            float silence = node.Value("silence", rank, TorrentSilenceSeconds);
            bool silenceSpent = false;
            int ticks = Mathf.Max(1, Mathf.RoundToInt(duration / tick));
            for (int i = 0; i < ticks; i++)
            {
                if (caster == null || caster.IsDead() || caster.IsStaggering()) yield break;
                // "15 eitr per second": the cast paid the first; each later tick pays its share or the beam ends.
                if (i > 0 && caster is Player player)
                {
                    float share = TorrentEitrPerSecond * tick;
                    if (!player.HaveEitr(share)) yield break;
                    player.UseEitr(share);
                }
                Character enemy = AimedEnemy(caster, target, range, out Vector3 point);
                if (enemy != null)
                {
                    HitData hit = HitFrom(caster, enemy);
                    hit.m_damage.m_lightning = damage;
                    enemy.Damage(hit);
                    AbilityRPCManager.SpawnFX(point, "vfx_HitSparks");
                    if (!silenceSpent)
                    {
                        silenceSpent = true;
                        bool silenced = CcKit.SilenceSoft(enemy, caster, silence);
                        DebugLog.Debug($"[MageKit] Arcane Torrent first hit {enemy.m_name}{(silenced ? ", silenced" : "")}");
                    }
                }
                yield return new WaitForSeconds(tick);
            }
        }

        // ---- helpers ---------------------------------------------------------------------------------------------

        // The first enemy along the aim within range; a wall or the ground in front stops the ray. point = where it stopped.
        private static Character AimedEnemy(Character caster, CastTarget target, float range, out Vector3 point)
        {
            Vector3 origin;
            Vector3 dir;
            if (caster == Player.m_localPlayer && GameCamera.instance != null)
            {
                origin = GameCamera.instance.transform.position;
                dir = GameCamera.instance.transform.forward;
                // The camera sits behind the player: measure range from the caster, not the lens.
                range += Vector3.Distance(origin, caster.GetEyePoint());
            }
            else
            {
                origin = caster.GetEyePoint();
                Vector3 to = target.Resolved ? target.Point - origin : caster.transform.forward;
                dir = to.sqrMagnitude > 0.01f ? to.normalized : caster.transform.forward;
            }
            point = origin + dir * range;
            int mask = GroundHeightSampler.SolidMask | LayerMask.GetMask("character", "character_net", "character_ghost");
            foreach (RaycastHit hit in Physics.RaycastAll(origin, dir, range, mask, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                Character character = hit.collider.GetComponentInParent<Character>();
                if (character == caster) continue;
                point = hit.point;
                if (character == null) return null;
                if (character.IsDead() || !ClassTargeting.IsEnemyTarget(caster, character)) continue;
                return character;
            }
            return null;
        }

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
