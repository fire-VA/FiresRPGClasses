using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using FiresCore.UI.Minigames;
using UnityEngine;

namespace FiresRPGClasses.UI
{
    public enum IconSet
    {
        /// <summary>game-icons.net silhouettes - the default, and what an ability icon should look like.</summary>
        Fantasy,

        /// <summary>Tabler line art - the same 182 names in a flat UI style.</summary>
        Line
    }

    /// <summary>
    /// The ability icons, embedded as PNGs named after the node id they belong to. Both sets carry the
    /// same 182 names, so switching set never leaves a slot without art.
    /// Attribution for both is in THIRDPARTY-NOTICES.md. Plan: Docs/PLAN_AbilityIcons.md
    ///
    /// The PNGs are white on transparent; the caller tints them, which is why one file serves an ability
    /// whatever colour its node declares.
    ///
    /// Decoding is Core's job. <see cref="FiresMinigameSprites"/> already owns embedded-PNG-to-Sprite for
    /// the family - including the reflection call into UnityEngine.ImageConversionModule, which a net48
    /// build cannot reference directly - so this file only decides WHICH resource to ask for.
    /// </summary>
    public static class ClassIcons
    {
        private const string Section = "6 - UI";
        private const string FilePrefix = "cls_";

        /// <summary>
        /// Resolved sprites by set and node. Core caches the decode; this avoids rebuilding the suffix
        /// string for sixteen slots every frame.
        /// </summary>
        private static readonly Dictionary<string, Sprite> Resolved =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        private static ConfigEntry<IconSet> _set;

        public static IconSet Set => _set == null ? IconSet.Fantasy : _set.Value;

        public static void BindConfig(ConfigFile config)
        {
            if (_set != null) return;
            _set = config.Bind(Section, "IconSet", IconSet.Fantasy,
                "Which ability icon art to use. Fantasy is game-icons.net; Line is Tabler line art.");
            _set.SettingChanged += (s, e) => Forget();
        }

        /// <summary>The icon for a node id, or null when nothing is embedded under that name.</summary>
        public static Sprite For(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return null;

            // 0.1.119 (perf audit: Set + "/" + nodeId boxed the enum and built a string per slot per frame, twice a frame): one map per set.
            if (!ResolvedBySet.TryGetValue(Set, out Dictionary<string, Sprite> resolved)) { resolved = new Dictionary<string, Sprite>(); ResolvedBySet[Set] = resolved; }
            // A miss is cached too: the bars ask every frame, and an uncached miss asked Core again each time
            // (0.1.72: ~135,000 "[FiresMinigame] sprite ... not embedded" Errors for the Druid nodes).
            if (resolved.TryGetValue(nodeId, out Sprite cached)) return cached;

            IconSet other = Set == IconSet.Fantasy ? IconSet.Line : IconSet.Fantasy;
            Sprite sprite = LoadEmbedded(Set, nodeId) ?? LoadEmbedded(other, nodeId);
            if (sprite == null)
            {
                FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] no icon embedded for '{nodeId}' in either set; showing a placeholder.");
                sprite = Placeholder();
            }
            resolved[nodeId] = sprite;
            return sprite;
        }

        /// <summary>As <see cref="For"/>, but null instead of the placeholder, for callers with art of their own to fall back on.</summary>
        public static Sprite ForOrNull(string nodeId)
        {
            Sprite sprite = For(nodeId);
            return sprite == _placeholder ? null : sprite;
        }

        private static HashSet<string> _embedded;

        /// <summary>
        /// Resources are named &lt;RootNamespace&gt;.Icons.&lt;Set&gt;.cls_&lt;nodeId&gt;.png, and Core matches on the suffix - so
        /// naming the set in the suffix is what keeps the two sets apart. Checked here first, because Core logs an Error
        /// for every name it can't find.
        /// </summary>
        private static Sprite LoadEmbedded(IconSet set, string nodeId)
        {
            Assembly asm = EnsureEmbedded();
            string suffix = set + "." + FilePrefix + nodeId + ".png";
            return _embedded.Contains(suffix) ? FiresMinigameSprites.Load(asm, suffix) : null;
        }

        private static Assembly EnsureEmbedded()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            if (_embedded == null)
            {
                _embedded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string name in asm.GetManifestResourceNames())
                {
                    int at = name.LastIndexOf(".Icons.", StringComparison.Ordinal);
                    if (at >= 0) _embedded.Add(name.Substring(at + ".Icons.".Length));
                }
            }
            return asm;
        }

        // ── status-effect icons (Fire 10-14: a proper icon for EVERY custom status effect; Tools\SE_ICON_INVENTORY.md) ──
        // [classes-ui] draws one icon per effect FAMILY as Icons\Effects\se_<Family>.png (64x64, vanilla status style). The
        // family is the effect name without its trailing tier number: ClassHaste15 / 20 / 25 -> se_ClassHaste.png. An exact
        // se_<EffectName>.png wins over the family one. Null when neither is embedded (the caller keeps its old icon).
        private static readonly Dictionary<string, Sprite> EffectResolved = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        internal static Sprite ForEffect(string effectName)
        {
            if (string.IsNullOrEmpty(effectName)) return null;
            if (EffectResolved.TryGetValue(effectName, out Sprite cached)) return cached;
            Assembly asm = EnsureEmbedded();
            Sprite sprite = null;
            foreach (string key in new[] { effectName, EffectFamily(effectName) })
            {
                string suffix = "Effects.se_" + key + ".png";
                if (_embedded.Contains(suffix)) { sprite = FiresMinigameSprites.Load(asm, suffix); break; }
            }
            EffectResolved[effectName] = sprite;   // a miss is cached too (the status bar asks often)
            return sprite;
        }

        /// <summary>An item's own icon, Icons\Items\item_&lt;Prefab&gt;.png ([classes-ui]'s race potions, 10-14), or null.</summary>
        internal static Sprite ForItem(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            Assembly asm = EnsureEmbedded();
            string suffix = "Items.item_" + prefab + ".png";
            return _embedded.Contains(suffix) ? FiresMinigameSprites.Load(asm, suffix) : null;
        }

        /// <summary>The effect's family: its name without the trailing tier digits (ClassEitrWell150 -> ClassEitrWell).</summary>
        internal static string EffectFamily(string effectName)
        {
            int end = effectName.Length;
            while (end > 0 && char.IsDigit(effectName[end - 1])) end--;
            return end > 0 ? effectName.Substring(0, end) : effectName;
        }

        private static Sprite _placeholder;

        /// <summary>A plain white ring (tinted by the caller like every icon) for a node nobody drew yet.</summary>
        private static Sprite Placeholder()
        {
            if (_placeholder != null) return _placeholder;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            float c = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - 24f) / 3f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(pixels);
            tex.Apply();
            _placeholder = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _placeholder;
        }

        /// <summary>
        /// Drops this mod's resolved lookups so a set change is picked up. The sprites themselves belong
        /// to Core's cache and are deliberately NOT destroyed here - both sets together are 276 small
        /// textures, and destroying one out from under Core would hand the next caller a dead reference.
        /// </summary>
        public static void Forget() => ResolvedBySet.Clear();

        private static readonly Dictionary<IconSet, Dictionary<string, Sprite>> ResolvedBySet = new Dictionary<IconSet, Dictionary<string, Sprite>>();
    }
}
