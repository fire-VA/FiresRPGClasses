using System;
using FiresCore.Npc.Archetypes.StatusEffects;
using FiresCore.Possession;
using FiresCore.SkillSystem;
using FiresRPGClasses.Data;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The server's half of Bat Form (Tools\VAMPIRE_CLASS_DESIGN.md §3 "MP"): Bat Form is a Core possession (Druid Wildshape's path), so
    /// the server check is a PossessionApi validator for the "rpg-vampire-bat" tag, as PossessionKit registers Wildshape's: the
    /// requester's player ZDO must carry the vampire race, and the creature must be a vanilla Bat. Like Wildshape's check it cannot see
    /// the talent ranks (they live in the client's own record), so "has the Bat Form node" is the client's check.
    /// Registered on every machine (clients from VampireKit.RegisterAll, the dedicated server from Ascend's server branch); only the
    /// server's copy runs.
    /// </summary>
    public static class VampireServer
    {
        internal const string BatTag = "rpg-vampire-bat";
        internal const string BatPrefab = "Bat";
        private static readonly int BatPrefabHash = BatPrefab.GetStableHashCode();

        private static bool _registered;

        public static void RegisterValidators()
        {
            if (_registered) return;
            _registered = true;
            PossessionApi.RegisterValidator(BatTag, ValidateBat);
        }

        private static string ValidateBat(long playerId, ZDO target, Character c)
        {
            string why = Check(playerId, target, c);
            FiresRPGClassesPlugin.Log?.LogDebug($"[Vampire] server: Bat Form grant for player {playerId}: {(why == null ? "allowed" : "refused, " + why)}");
            return why;
        }

        private static string Check(long playerId, ZDO target, Character c)
        {
            if (target == null || target.GetPrefab() != BatPrefabHash) return "only a bat can carry a vampire";
            ZDO player = PlayerZdo(playerId);
            if (player == null) return "the server can't see your character";
            if (!VampireRace.Has(player, out string raceWhy)) return "not of the vampire race (" + raceWhy + ")";
            return null;
        }

        // The requester's player ZDO by player id: a peer's character on a server, or the host's own player.
        private static ZDO PlayerZdo(long playerId)
        {
            Player local = Player.m_localPlayer;
            if (local != null && local.GetPlayerID() == playerId && local.m_nview != null && local.m_nview.IsValid()) return local.m_nview.GetZDO();
            if (ZNet.instance == null || ZDOMan.instance == null) return null;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if (peer == null || peer.m_characterID.IsNone()) continue;
                ZDO zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                if (zdo != null && zdo.GetLong(ZDOVars.s_playerID, 0L) == playerId) return zdo;
            }
            return null;
        }
    }

    /// <summary>
    /// The "Bat Form" skill (design §11): a custom skill through Core's FiresSkillRegistry.Register (a contract: called, never changed),
    /// levels 0-100, raised by seconds flown, with vanilla's death drain. Registered on both peers (the registry's rule); the icon is
    /// filled in once ObjectDB wakes (Register is idempotent by name and refreshes the icon).
    /// </summary>
    internal static class VampireSkill
    {
        internal const string Name = "Bat Form";
        private const string Description = "Flying as a bat. Longer flights and a slower-growing cooldown as it rises.";
        private static FiresSkill _skill;
        private static bool _hasIcon;

        internal static Skills.SkillType Type => _skill != null ? _skill.SkillType : (Skills.SkillType)Math.Abs(Name.GetStableHashCode());

        internal static void Register(bool withIcon)
        {
            if (_skill != null && (!withIcon || _hasIcon)) return;
            try
            {
                Sprite icon = null;
                if (withIcon && !Application.isBatchMode)
                {
                    icon = CompanionStatusEffectBase.CreateFallbackIcon(new Color(0.55f, 0.08f, 0.14f));
                    _hasIcon = icon != null;
                }
                _skill = FiresSkillRegistry.Register(Name, Description, icon);
                FiresRPGClassesPlugin.Log?.LogDebug($"[Vampire] skill '{Name}' registered (type {(int)_skill.SkillType}, icon {(icon != null ? "set" : "none")})");
            }
            catch (Exception e) { FiresRPGClassesPlugin.Log?.LogWarning($"[Vampire] Bat Form skill not registered: {e.GetType().Name}: {e.Message}"); }
        }

        /// <summary>The local player's Bat Form skill level, 0-100.</summary>
        internal static float Level(Player p) => p != null ? p.GetFiresSkillLevel(Type) : 0f;
    }
}
