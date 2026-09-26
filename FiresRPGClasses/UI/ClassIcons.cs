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

        /// <summary>Tabler line art - the same 138 names in a flat UI style.</summary>
        Line
    }

    /// <summary>
    /// The ability icons, embedded as PNGs named after the node id they belong to. Both sets carry the
    /// same 138 names, so switching set never leaves a slot without art.
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
        private const string Section = "UI";
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

            string key = Set + "/" + nodeId;
            if (Resolved.TryGetValue(key, out Sprite cached) && cached != null) return cached;

            // Resources are named <RootNamespace>.Icons.<Set>.cls_<nodeId>.png, and Core matches on the
            // suffix - so naming the set in the suffix is what keeps the two sets apart.
            Sprite sprite = FiresMinigameSprites.Load(
                Assembly.GetExecutingAssembly(), Set + "." + FilePrefix + nodeId + ".png");

            Resolved[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// Drops this mod's resolved lookups so a set change is picked up. The sprites themselves belong
        /// to Core's cache and are deliberately NOT destroyed here - both sets together are 276 small
        /// textures, and destroying one out from under Core would hand the next caller a dead reference.
        /// </summary>
        public static void Forget() => Resolved.Clear();
    }
}
