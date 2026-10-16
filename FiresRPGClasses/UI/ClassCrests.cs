using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FiresRPGClasses.Data;
using FiresCore.Npc.Archetypes;
using FiresCore.UI.Minigames;
using UnityEngine;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// The eight class crests (Fire, 2026-09-29: game-icons.net, the node icons' family). Embedded as
    /// Icons\Crests\crest_&lt;archetype&gt;.png, white on transparent and tinted by the caller like the node icons.
    /// Until a crest is embedded, the archetype's starter node icon stands in, so no crest slot is ever empty.
    /// </summary>
    public static class ClassCrests
    {
        private static readonly Dictionary<ArchetypeClass, Sprite> Resolved = new Dictionary<ArchetypeClass, Sprite>();

        public static Sprite For(ArchetypeClass archetype)
        {
            if (Resolved.TryGetValue(archetype, out Sprite cached)) return cached;

            // Core's loader logs an error for a name that isn't embedded, so ask only for a crest that is.
            Assembly asm = Assembly.GetExecutingAssembly();
            string suffix = "Crests.crest_" + ClassNames.Of(archetype).ToLowerInvariant() + ".png";
            bool embedded = asm.GetManifestResourceNames().Any(name => name.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase));
            Sprite sprite = embedded ? FiresMinigameSprites.Load(asm, suffix) : null;
            if (sprite == null)
            {
                ClassTreeNode starter = ClassTrees.Tree(archetype)?.Nodes
                    .Where(node => node.Tier == NodeTier.Starter)
                    .OrderBy(node => node.Slot == NodeSlot.Evasion ? 1 : 0)
                    .FirstOrDefault();
                sprite = starter != null ? ClassIcons.ForOrNull(starter.Id) : null;
            }

            Resolved[archetype] = sprite;
            return sprite;
        }

        public static void Forget() => Resolved.Clear();
    }
}
