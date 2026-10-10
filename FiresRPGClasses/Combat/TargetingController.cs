using System.Collections.Generic;
using BepInEx.Configuration;
using FiresRPGClasses.Data;
using FiresCore.Npc.Archetypes.Effects;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    public enum CastStyle
    {
        Normal,
        Quick,
        QuickWithIndicator
    }

    public struct CastTarget
    {
        public bool Resolved;
        public Vector3 Point;
        public ZDOID TargetZdo;
        public Character TargetCharacter;
        public string Refusal;

        public static CastTarget No(string refusal) => new CastTarget { Resolved = false, Refusal = refusal };

        public static CastTarget At(Vector3 point) => new CastTarget { Resolved = true, Point = point };

        public static CastTarget On(Character character) => new CastTarget
        {
            Resolved = true,
            Point = character != null ? character.transform.position : Vector3.zero,
            TargetCharacter = character,
            TargetZdo = character != null ? character.GetZDOID() : ZDOID.None,
        };
    }

    /// <summary>
    /// Doc 14 section 5: placed, ally, aimed, dash and the instant modes. Placed and aimed preview through
    /// Core's ground rings. Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public static class TargetingController
    {
        public const string Section = "Targeting";

        internal const float AimRayLength = 100f;
        private const float PreviewRingSeconds = 0.2f;
        private const float CrosshairAllyRadius = 1.5f;

        public static ConfigEntry<CastStyle> Style { get; private set; }
        public static ConfigEntry<bool> SmartHeal { get; private set; }

        private static GroundRing _preview;
        private static string _aimingNodeId;

        public static bool IsAiming => _aimingNodeId != null;

        public static string AimingNodeId => _aimingNodeId;

        public static void Bind(ConfigFile config)
        {
            Style = config.Bind(Section, "Cast style", CastStyle.QuickWithIndicator,
                "How placed and aimed skills are committed: hold to aim and release, press then click, or place at the crosshair at once.");
            SmartHeal = config.Bind(Section, "Smart heal", true,
                "An ally skill with nobody under the crosshair goes to the most injured ally in range, else you.");
        }

        public static void BeginAiming(string nodeId)
        {
            _aimingNodeId = nodeId;
        }

        public static void CancelAiming()
        {
            _aimingNodeId = null;
            ReleasePreview();
        }

        /// <summary>Draws the reticle while a placed or aimed skill is being aimed. Called once a frame.</summary>
        public static void UpdatePreview(ClassTreeNode node)
        {
            if (node == null) { ReleasePreview(); return; }
            Player player = Player.m_localPlayer;
            if (player == null) { ReleasePreview(); return; }

            CastTarget target = ResolvePoint(player, node.Targeting);
            if (!target.Resolved) { ReleasePreview(); return; }

            GroundRingKind kind = RingKindFor(node);
            float radius = node.Targeting.Radius > 0f ? node.Targeting.Radius : 1f;
            ReleasePreview();
            _preview = GroundRingManager.Show(kind, target.Point, radius, PreviewRingSeconds, player, true);
        }

        public static void ShowCastRing(ClassTreeNode node, Vector3 point, float seconds)
        {
            Player player = Player.m_localPlayer;
            if (node == null || player == null || node.Targeting.Radius <= 0f) return;
            GroundRingManager.Show(RingKindFor(node), point, node.Targeting.Radius, seconds, player, false);
        }

        public static CastTarget Resolve(ClassTreeNode node)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return CastTarget.No("no player");
            TargetingSpec spec = node.Targeting;

            switch (spec.Mode)
            {
                case TargetingMode.Self:
                case TargetingMode.AroundYou:
                case TargetingMode.NextAttack:
                case TargetingMode.Melee:
                case TargetingMode.Automatic:
                case TargetingMode.Dodge:
                    return CastTarget.At(player.transform.position);

                case TargetingMode.Ally:
                    return ResolveAlly(player, spec);

                case TargetingMode.Placed:
                case TargetingMode.Aimed:
                case TargetingMode.Dash:
                    return ResolvePoint(player, spec);

                default:
                    return CastTarget.No("unknown targeting mode");
            }
        }

        private static CastTarget ResolvePoint(Player player, TargetingSpec spec)
        {
            Transform eye = GameCamera.instance != null ? GameCamera.instance.transform : player.m_eye;
            if (eye == null) return CastTarget.No("no camera");

            float range = spec.Range > 0f ? spec.Range : AimRayLength;
            if (!Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, AimRayLength,
                    GroundHeightSampler.SolidMask))
                return CastTarget.No("nothing in sight");

            Vector3 point = hit.point;
            point.y = GroundHeightSampler.Sample(point, point.y);

            Vector3 origin = player.transform.position;
            Vector3 flat = new Vector3(point.x - origin.x, 0f, point.z - origin.z);
            if (flat.magnitude > range) return CastTarget.No("out of range");

            return CastTarget.At(point);
        }

        /// <summary>Doc 14: the ally under the crosshair, else with smart heal the most injured in range, else you.</summary>
        private static CastTarget ResolveAlly(Player player, TargetingSpec spec)
        {
            float range = spec.Range > 0f ? spec.Range : AimRayLength;
            Character underCrosshair = AllyUnderCrosshair(player, range);
            if (underCrosshair != null) return CastTarget.On(underCrosshair);
            if (!SmartHeal.Value) return CastTarget.On(player);

            Character injured = MostInjuredAlly(player, range);
            return CastTarget.On(injured ?? player);
        }

        private static Character AllyUnderCrosshair(Player player, float range)
        {
            Transform eye = GameCamera.instance != null ? GameCamera.instance.transform : player.m_eye;
            if (eye == null) return null;

            RaycastHit[] hits = Physics.SphereCastAll(eye.position, CrosshairAllyRadius, eye.forward, range,
                LayerMask.GetMask("character", "character_net", "character_ghost"));
            Character best = null;
            float bestDistance = float.MaxValue;
            foreach (RaycastHit hit in hits)
            {
                Character character = hit.collider.GetComponentInParent<Character>();
                if (!IsAlly(player, character)) continue;
                if (hit.distance >= bestDistance) continue;
                bestDistance = hit.distance;
                best = character;
            }
            return best;
        }

        private static Character MostInjuredAlly(Player player, float range)
        {
            Character best = null;
            float bestFraction = 1f;
            List<Character> nearby = new List<Character>();
            Character.GetCharactersInRange(player.transform.position, range, nearby);
            foreach (Character character in nearby)
            {
                if (!IsAlly(player, character)) continue;
                float fraction = character.GetHealthPercentage();
                if (fraction >= bestFraction) continue;
                bestFraction = fraction;
                best = character;
            }
            return best;
        }

        public static bool IsAlly(Player player, Character character)
        {
            if (character == null || character.IsDead()) return false;
            if (character == player) return true;
            if (character.IsPlayer()) return true;
            return character.IsTamed();
        }

        private static GroundRingKind RingKindFor(ClassTreeNode node)
        {
            if (node.Archetype == FiresCore.Npc.Archetypes.ArchetypeClass.Healer) return GroundRingKind.Heal;
            if (node.Targeting.Mode == TargetingMode.Ally) return GroundRingKind.Heal;
            if (node.Targeting.Mode == TargetingMode.AroundYou) return GroundRingKind.Buff;
            return GroundRingKind.Damage;
        }

        private static void ReleasePreview()
        {
            if (_preview == null) return;
            GroundRingManager.Release(_preview);
            _preview = null;
        }
    }
}
