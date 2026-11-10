using System;
using System.Collections.Generic;
using FiresRPGClasses.Data;
using FiresCore.Classes;
using FiresCore.Npc.Archetypes;
using Newtonsoft.Json;

namespace FiresRPGClasses.State
{
    /// <summary>
    /// Doc 19's schema 3 record. Core's ClassProgressState is the progression half; the bars, saved bars and
    /// ultimate levels are this mod's half. One document, one store.
    /// </summary>
    public class ClassPlayerData
    {
        public int Schema = ClassPlayerStore.CurrentSchema;

        /// <summary>
        /// The character this record belongs to (PlayerProfile id, Player.GetPlayerID), so class levels never show up on
        /// another character. Stamped on save; a record stamped for a different character is never used. 0 = not stamped yet
        /// (older records), adopted by the character that loads it. Optional field: no schema bump, older builds ignore it.
        /// </summary>
        public long OwnerId;

        public ClassProgressState Progress = new ClassProgressState();

        public BarLayout Front = new BarLayout();
        public BarLayout Back = new BarLayout();
        public BarSide ActiveBar = BarSide.Front;

        /// <summary>0.1.211 (Fire 10-30: "There are no limits ... We are a god"): set by any Studio God edit (GodApi). While set, the load-time
        /// reconcile leaves the record alone (an overspent god record is the point, not an error). GodApi.SetGod(false) hands the record
        /// back to the rules: the next load reconciles it. Optional field: older builds ignore it, a missing one reads false.</summary>
        public bool God;

        public readonly List<SavedBar> SavedBars = new List<SavedBar>();

        /// <summary>BarMemory: each bar's layout per weapon type, key "Front|BloodMagic.2H".</summary>
        public readonly Dictionary<string, BarLayout> WeaponBars = new Dictionary<string, BarLayout>(StringComparer.Ordinal);

        public readonly Dictionary<string, UltimateProgress> Ultimates =
            new Dictionary<string, UltimateProgress>();

        /// <summary>
        /// One-time class XP already paid that vanilla does not track itself: "creature:&lt;prefab&gt;" and "boss:&lt;prefab&gt;" first
        /// kills (ClassXp). A record without it loads with an empty set.
        /// </summary>
        public readonly HashSet<string> Firsts = new HashSet<string>(StringComparer.Ordinal);

        [JsonIgnore]
        public BarLayout Active => ActiveBar == BarSide.Front ? Front : Back;

        [JsonIgnore]
        public BarLayout Inactive => ActiveBar == BarSide.Front ? Back : Front;

        public BarLayout Bar(BarSide side) => side == BarSide.Front ? Front : Back;

        public static BarSide Other(BarSide side) => side == BarSide.Front ? BarSide.Back : BarSide.Front;

        /// <summary>Doc 14: the hybrid is the ordered pair (front archetype, back archetype).</summary>
        [JsonIgnore]
        public bool HasHybridPair =>
            Front.Archetype != ArchetypeClass.None
            && Back.Archetype != ArchetypeClass.None
            && Front.Archetype != Back.Archetype;

        public UltimateProgress UltimateState(string ultimateId)
        {
            if (string.IsNullOrEmpty(ultimateId)) return null;
            if (!Ultimates.TryGetValue(ultimateId, out UltimateProgress progress))
            {
                progress = new UltimateProgress();
                Ultimates[ultimateId] = progress;
            }
            return progress;
        }

        public int UltimateLevel(string ultimateId)
        {
            UltimateProgress progress = UltimateState(ultimateId);
            return progress?.Level ?? 1;
        }

        public void Normalize()
        {
            Schema = ClassPlayerStore.CurrentSchema;
            if (Progress == null) Progress = new ClassProgressState();
            if (Front == null) Front = new BarLayout();
            if (Back == null) Back = new BarLayout();
            Front.EnsureSlotCount();
            Back.EnsureSlotCount();
            for (int i = SavedBars.Count - 1; i >= 0; i--)
            {
                if (SavedBars[i] == null || SavedBars[i].Layout == null) SavedBars.RemoveAt(i);
                else SavedBars[i].Layout.EnsureSlotCount();
            }
        }

        /// <summary>Total point cost of every loaded tree, for the pace preview.</summary>
        public static int TotalTreeCost() => ClassTrees.TotalPointCostOfEveryTree();

        public bool SaveBar(string name, BarSide side)
        {
            if (string.IsNullOrEmpty(name)) return false;
            BarLayout clone = Bar(side).Clone();
            int existing = SavedBars.FindIndex(bar =>
                string.Equals(bar.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0) SavedBars[existing].Layout = clone;
            else SavedBars.Add(new SavedBar { Name = name, Layout = clone });
            return true;
        }

        public bool DeleteSavedBar(string name)
        {
            int index = SavedBars.FindIndex(bar =>
                string.Equals(bar.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return false;
            SavedBars.RemoveAt(index);
            return true;
        }

        public SavedBar FindSavedBar(string name) =>
            SavedBars.Find(bar => string.Equals(bar.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
