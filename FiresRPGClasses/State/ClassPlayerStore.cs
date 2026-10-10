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

        /// <summary>The local player's record, loaded on first use and cached until the player object changes.</summary>
        public static ClassPlayerData Local
        {
            get
            {
                Player player = Player.m_localPlayer;
                if (player == null) return null;
                if (_local != null && ReferenceEquals(_localOwner, player)) return _local;
                _local = Load(player);
                _localOwner = player;
                return _local;
            }
        }

        public static void Forget()
        {
            _local = null;
            _localOwner = null;
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
            if (!Backup(player, name, from, raw))
                return Protect(player, "class record " + name + ": could not back up the schema " + from + " record, so it is not upgraded");

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

        // Before an upgrade changes a record: the untouched JSON under a second key in the same character (saved with it) and
        // as a file under BepInEx\config\FiresRPGClasses\record-backups. Neither is ever overwritten: when one already holds a
        // DIFFERENT record (a rollback and re-upgrade, [worldgen]), this one goes to a time-stamped sibling instead.
        private static bool Backup(Player player, string name, int schema, string raw)
        {
            if (string.IsNullOrEmpty(raw) || player.m_customData == null) return false;
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string key = CustomDataKey + "_schema" + schema;
            if (player.m_customData.TryGetValue(key, out string held) && held != raw) key += "_" + stamp;
            if (!player.m_customData.ContainsKey(key)) player.m_customData[key] = raw;
            try
            {
                string folder = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "FiresRPGClasses", "record-backups");
                System.IO.Directory.CreateDirectory(folder);
                string safe = string.Concat(name.Split(System.IO.Path.GetInvalidFileNameChars()));
                string file = System.IO.Path.Combine(folder, safe + ".schema" + schema + ".json");
                if (System.IO.File.Exists(file) && System.IO.File.ReadAllText(file) != raw)
                    file = System.IO.Path.Combine(folder, safe + ".schema" + schema + "." + stamp + ".json");
                if (!System.IO.File.Exists(file)) System.IO.File.WriteAllText(file, raw);
                Debug.Log(LogPrefix + " class record " + name + ": schema " + schema + " backed up to " + file + " and key " + key);
            }
            catch (Exception ex)
            {
                // The in-character copy is enough to proceed; the file is the extra.
                Debug.LogWarning(LogPrefix + " class record " + name + ": backup file failed (" + ex.Message + "); the copy under key " + key + " stands");
            }
            return player.m_customData.TryGetValue(key, out string kept) && kept == raw;
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
