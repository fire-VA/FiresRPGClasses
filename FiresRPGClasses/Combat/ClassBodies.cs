using System.Collections.Generic;
using FiresRPGClasses.State;
using Newtonsoft.Json;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// 0.1.214 ([lead] / [ghost], Fire 10-30: the Studio god clone fights as its class - step 3, its stats): the non-player bodies that
    /// carry a class record (the server-side god clone, its record in GodProfile.Shadow.m_customData). Registered by their owner code;
    /// AttributeEffects reads a registered body's record the way it reads the local player's (same rows, same caps), so outgoing damage,
    /// elemental damage taken, health / stamina / eitr regen and attack speed follow that body's attributes. The record is re-read when the
    /// dictionary's string changes (GodApi.EditFor writes a new one). The local player never goes through here.
    /// </summary>
    public static class ClassBodies
    {
        private sealed class Entry { public IDictionary<string, string> Store; public string Raw; public ClassPlayerData Data; }
        private static readonly Dictionary<Character, Entry> s_bodies = new Dictionary<Character, Entry>();
        private static readonly JsonSerializerSettings ReadJson = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, ObjectCreationHandling = ObjectCreationHandling.Auto };

        /// <summary>A body (owned here) that carries the class record in <paramref name="customData"/>["FiresRPGClasses_v3"].</summary>
        public static void Register(Character body, IDictionary<string, string> customData)
        {
            if (body == null || customData == null) return;
            s_bodies[body] = new Entry { Store = customData };
            FiresRPGClassesPlugin.Log?.LogDebug($"[ClassStats] {body.m_name}: class body registered ({s_bodies.Count} now)");
        }

        public static void Unregister(Character body)
        {
            if (body != null && s_bodies.Remove(body)) FiresRPGClassesPlugin.Log?.LogDebug($"[ClassStats] {body.m_name}: class body unregistered");
        }

        /// <summary>The registered body's class record, or null (not registered, gone, or not owned on this peer).</summary>
        internal static ClassPlayerData Record(Character body)
        {
            if (body == null || s_bodies.Count == 0 || !s_bodies.TryGetValue(body, out Entry e)) return null;
            var view = body.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner()) return null;
            e.Store.TryGetValue(ClassPlayerStore.CustomDataKey, out string raw);
            if (!ReferenceEquals(raw, e.Raw) || e.Data == null)
            {
                e.Raw = raw;
                try { e.Data = string.IsNullOrEmpty(raw) ? new ClassPlayerData() : JsonConvert.DeserializeObject<ClassPlayerData>(raw, ReadJson) ?? new ClassPlayerData(); }
                catch { e.Data = new ClassPlayerData(); }
                AttributeEffects.LogBody(body, e.Data);
            }
            return e.Data;
        }

        public static bool IsRegistered(Character body) => Record(body) != null;

        /// <summary>The registered bodies (for the passive kits' per-body ticks); dead / destroyed entries are dropped.</summary>
        internal static List<Character> Bodies()
        {
            var list = new List<Character>();
            foreach (var kv in new List<KeyValuePair<Character, Entry>>(s_bodies))
            {
                if (kv.Key == null) { s_bodies.Remove(kv.Key); continue; }
                list.Add(kv.Key);
            }
            return list;
        }

        /// <summary>For [ghost]'s CompanionStats (one call, no second formula): the class's flat pool / armour bonuses for this body -
        /// max health, max stamina, max eitr, armour, carry weight - which the player gets through Player-only methods a companion never calls.</summary>
        public static void PoolBonus(Character body, out float health, out float stamina, out float eitr, out float armour, out float carry)
        {
            health = AttributeEffects.Current(AttributeEffects.Stat.MaxHealth, body);
            stamina = AttributeEffects.Current(AttributeEffects.Stat.MaxStamina, body);
            eitr = AttributeEffects.Current(AttributeEffects.Stat.MaxEitr, body);
            armour = AttributeEffects.Current(AttributeEffects.Stat.Armour, body);
            carry = AttributeEffects.Current(AttributeEffects.Stat.CarryWeight, body);
        }
    }
}
