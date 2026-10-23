using System;
using System.Collections.Generic;
using System.Reflection;

namespace FiresRPGClasses.Paragon
{
    /// <summary>
    /// Marks a static class that implements Paragon star effects outside
    /// ParagonEffects.Stat. The class must have <c>public static readonly string[] Formats</c>: the yaml effectFormat texts it makes live,
    /// copied EXACTLY. Its hooks read their size with <c>ParagonEffects.TX(format)</c> (a fraction: 5% = 0.05, a flat amount = value / 100).
    /// One file per owner/tree (Paragon\Effects\Warfare*.cs, Fitness*.cs, Craft*.cs), so two people never edit the same file.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ParagonEffectSetAttribute : Attribute { }

    /// <summary>Finds every [ParagonEffectSet] class in this assembly once and answers "is this effect text implemented".</summary>
    public static class ParagonEffectSets
    {
        private static HashSet<string> _formats;

        public static bool Has(string format)
        {
            if (string.IsNullOrEmpty(format)) return false;
            if (_formats == null) Load();
            return _formats.Contains(format);
        }

        private static void Load()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            int sets = 0;
            foreach (Type t in typeof(ParagonEffectSets).Assembly.GetTypes())
            {
                if (t.GetCustomAttribute<ParagonEffectSetAttribute>() == null) continue;
                FieldInfo f = t.GetField("Formats", BindingFlags.Public | BindingFlags.Static);
                if (!(f?.GetValue(null) is string[] formats))
                {
                    FiresRPGClassesPlugin.Log?.LogWarning($"[Paragon] {t.Name} is a [ParagonEffectSet] without a public static string[] Formats; ignored");
                    continue;
                }
                sets++;
                foreach (string s in formats) if (!string.IsNullOrEmpty(s) && !set.Add(s)) FiresRPGClassesPlugin.Log?.LogWarning($"[Paragon] effect text implemented twice: '{s}' (also in {t.Name})");
            }
            _formats = set;
            FiresRPGClassesPlugin.Log?.LogDebug($"[Paragon] {sets} extra effect set(s), {set.Count} effect text(s) live");
        }
    }
}
