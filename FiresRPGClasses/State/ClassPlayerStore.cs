using System;
using FiresRPGClasses.Data;
using FiresCore.Classes;
using Newtonsoft.Json;
using UnityEngine;

namespace FiresRPGClasses.State
{
    /// <summary>
    /// The only code that touches the FiresRPGClasses key in Player.m_customData. Main thread only.
    /// Plan: Docs/PLAN_ClassesCombatAndUI.md
    /// </summary>
    public static class ClassPlayerStore
    {
        public const string CustomDataKey = "FiresRPGClasses_v3";

        private const string LogPrefix = "[FiresRPGClasses]";

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Auto,
        };

        private static ClassPlayerData _local;
        private static Player _localOwner;
        private static long _localId;

        /// <summary>
        /// The local player's record, loaded on first use and cached for that player object AND character id. Fire 10-12:
        /// levels leaked between characters. Vanilla's Player.Load merges a profile's keys into m_customData without clearing
        /// it, and the id is only set by PlayerProfile.LoadPlayerData, so the cache never forms before the id is known
        /// (id 0: a read-only stand-in, not cached) and drops itself when the id changes under the same object.
        /// </summary>
        public static ClassPlayerData Local
        {
            get
            {
                Player player = Player.m_localPlayer;
                if (player == null) return null;
                long id = player.GetPlayerID();
                if (_local != null && ReferenceEquals(_localOwner, player) && _localId == id) return _local;
                if (id == 0L) return NewRecord();   // the profile isn't loaded into this player yet
                if (_local != null)
                    Debug.Log(LogPrefix + " class record cache reset: player " + (ReferenceEquals(_localOwner, player) ? "same object" : "new object")
                        + ", character id " + _localId + " -> " + id);
                _local = Load(player);
                _localOwner = player;
                _localId = id;
                return _local;
            }
        }

        public static void Forget()
        {
            _local = null;
            _localOwner = null;
            _localId = 0L;
        }

        public static ClassPlayerData Load(Player player)
        {
            if (player == null) return null;
            if (!ReferenceEquals(player, _protectedFor)) _protectedFor = null;
            string raw = null;
            try
            {
                if (player.m_customData != null)
                    player.m_customData.TryGetValue(CustomDataKey, out raw);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LogPrefix + " m_customData read failed: " + ex.Message);
            }

            if (string.IsNullOrEmpty(raw)) return NewRecord();

            try
            {
                ClassPlayerData data = JsonConvert.DeserializeObject<ClassPlayerData>(raw, Settings);
                if (data == null) return Protect(player, "the stored class record reads as empty JSON");
                if (data.Schema < UpgradableSchema || data.Schema > CurrentSchema
                    || (data.Progress != null && data.Progress.Schema > CurrentSchema))
                    return Protect(player, "the stored class record is schema " + data.Schema + "/" + data.Progress?.Schema
                        + ", this build reads " + UpgradableSchema + ".." + CurrentSchema);
                long id = player.GetPlayerID();
                if (data.OwnerId != 0L && id != 0L && data.OwnerId != id)
                {
                    // Another character's record in this character's save (the 10-12 leak): never used; this character starts
                    // blank, and its first save writes its own record over the foreign one.
                    Debug.LogWarning(LogPrefix + " " + player.GetPlayerName() + " (character " + id + ") carried the class record of character "
                        + data.OwnerId + "; it is ignored and this character starts a fresh class record.");
                    return NewRecord();
                }
                bool upgrade = data.Progress != null && data.Progress.Schema < CurrentSchema;
                data.Normalize();
                return upgrade ? Upgrade(player, data, raw) : data;
            }
            catch (Exception ex)
            {
                return Protect(player, "could not parse the stored class record: " + ex.Message);
            }
        }

        // A stored record this build can't read (a newer schema, or damaged) is never overwritten: the player plays on a
        // blank record in memory, and Save refuses to write over the stored one until a build that reads it loads it.
        private static Player _protectedFor;

        /// <summary>The local character's stored record is protected (unreadable, newer, or its backup failed): no spending.</summary>
        public static bool LocalIsProtected
        {
            get
            {
                _ = Local;
                return Player.m_localPlayer != null && ReferenceEquals(Player.m_localPlayer, _protectedFor);
            }
        }

        private static ClassPlayerData Protect(Player player, string why)
        {
            _protectedFor = player;
            Debug.LogError(LogPrefix + " " + why + ". It is kept untouched and NOT saved over this session; "
                + "update FiresRPGClasses to read it. Class progress shown now is a blank stand-in.");
            return NewRecord();
        }

        public static bool Save(Player player, ClassPlayerData data)
        {
            if (player == null || data == null) return false;
            if (ReferenceEquals(player, _protectedFor)) return false;
            long id = player.GetPlayerID();
            if (id == 0L) return false;   // the profile isn't loaded yet: writing now would land on the wrong character
            if (data.OwnerId != 0L && data.OwnerId != id)
            {
                Debug.LogWarning(LogPrefix + " refused to save character " + data.OwnerId + "'s class record onto " + player.GetPlayerName()
                    + " (character " + id + ")");
                return false;
            }
            data.OwnerId = id;
            data.Normalize();
            try
            {
                if (player.m_customData == null) return false;
                player.m_customData[CustomDataKey] = JsonConvert.SerializeObject(data, Settings);
            }
            catch (Exception ex)
            {
                Debug.LogError(LogPrefix + " m_customData write failed: " + ex.Message);
                return false;
            }
            return true;
        }

        public static bool SaveLocal()
        {
            ClassPlayerData data = Local;
            return data != null && Save(Player.m_localPlayer, data);
        }

        /// <summary>
        /// Core 0.2.156 (schema 4, Fire 2026-09-29): talent ranks cost 1/2/3 points, not 1 each. A schema 3 record that no
        /// longer fits its earned points is refunded once to re-spend, before any reconcile could wipe it at every load.
        /// </summary>
        private static ClassPlayerData Upgrade(Player player, ClassPlayerData data, string raw)
        {
            int from = data.Progress.Schema;
            string name = player.GetPlayerName();
            // 0.1.161 (Fire 10-15 via the lead: no backup data on the rig): the upgrade no longer keeps a copy of the old record
            // (it was a 2nd key in the character + a file under BepInEx\config, which also broke the no-files-in-config rule)
            Debug.Log(LogPrefix + " class record " + name + ": upgrading from schema " + from + " (no backup copy kept)");

            ProgressionRates rates = ProgressionSettings.IsInitialized
                ? ProgressionSettings.CurrentRates
                : ProgressionSettings.DefaultRates;
            int oldSpent = 0;
            foreach (var tree in data.Progress.Talents.Values)
                foreach (int rank in tree.Values)
                    oldSpent += rank;
            int repriced = data.Progress.SpentSkillPoints;
            int refunded = data.Progress.UpgradeSchema(rates);
            if (refunded > 0) DropUnlearnedFromBars(data);
            Debug.Log(LogPrefix + " class record " + name + ": schema " + from + " -> " + data.Progress.Schema + ", spent "
                + oldSpent + " at 1 per rank = " + repriced + " at 1/2/3, refunded " + refunded + ", now spent "
                + data.Progress.SpentSkillPoints + " of " + data.Progress.EarnedSkillPoints(rates)
                + (refunded > 0 ? " (over its earned points: talents handed back once to re-spend)" : ""));
            Save(player, data);
            return data;
        }


        /// <summary>
        /// Core's record schema as loaded at RUNTIME ([generator]): a const would be baked into this build and go stale at
        /// the next Core bump.
        /// </summary>
        public static readonly int CurrentSchema =
            (int)(typeof(ClassProgressState).GetField("CurrentSchema")?.GetValue(null) ?? ClassProgressState.CurrentSchema);

        // The oldest record Load upgrades in place; anything outside UpgradableSchema..CurrentSchema is kept untouched.
        private const int UpgradableSchema = 3;

        private static ClassPlayerData NewRecord()
        {
            ClassPlayerData data = new ClassPlayerData();
            data.Normalize();
            return data;
        }

        /// <summary>
        /// Doc 19: lowering a rate never deletes anything learned. Under Refund the trees are handed back so
        /// players re-spend inside the new budget; under Keep they simply cannot spend again until they catch up.
        /// </summary>
        public static int ReconcileWithSettings(ClassPlayerData data)
        {
            if (data == null) return 0;
            ProgressionRates rates = ProgressionSettings.IsInitialized
                ? ProgressionSettings.CurrentRates
                : ProgressionSettings.DefaultRates;
            int refunded = data.Progress.ReconcileOverspend(rates);
            int trimmed = data.Progress.TrimPassivesToSlots();
            if (refunded > 0) DropUnlearnedFromBars(data);
            return refunded + trimmed;
        }

        private static void DropUnlearnedFromBars(ClassPlayerData data)
        {
            DropUnlearnedFromBar(data, data.Front);
            DropUnlearnedFromBar(data, data.Back);
        }

        private static void DropUnlearnedFromBar(ClassPlayerData data, BarLayout bar)
        {
            bar.EnsureSlotCount();
            for (int slot = BarLayout.FirstSkillSlot; slot <= BarLayout.LastSkillSlot; slot++)
            {
                string nodeId = bar.SkillAt(slot);
                if (string.IsNullOrEmpty(nodeId)) continue;
                ClassTreeNode node = ClassTrees.Node(nodeId);
                if (node == null || data.Progress.TalentRank(node.Archetype, nodeId) <= 0)
                    bar.SetSkill(slot, "");
            }
        }
    }
}
