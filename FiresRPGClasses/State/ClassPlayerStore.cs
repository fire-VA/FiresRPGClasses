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
                if (data == null) return NewRecord();
                if (data.Schema != ClassProgressState.CurrentSchema)
                {
                    Debug.LogWarning(LogPrefix + " stored schema " + data.Schema + " is not "
                        + ClassProgressState.CurrentSchema + "; starting a fresh record.");
                    return NewRecord();
                }
                data.Normalize();
                return data;
            }
            catch (Exception ex)
            {
                Debug.LogError(LogPrefix + " could not parse the stored class record: " + ex.Message);
                return NewRecord();
            }
        }

        public static bool Save(Player player, ClassPlayerData data)
        {
            if (player == null || data == null) return false;
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
