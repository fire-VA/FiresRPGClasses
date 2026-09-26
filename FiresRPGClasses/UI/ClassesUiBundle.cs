using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// The embedded firesrpgclassesui bundle and the one prefab in it.
    ///
    /// <para>Built by <c>FiresClassesUIBundle.Build</c> in the Unity project
    /// (<c>NewProject2026\ValheimRip1.0</c>), authored at
    /// <c>Assets/Custom/FiresClassesUI/FiresClassesPanel.prefab</c>. The loop after any layout edit is:
    /// build the bundle, then rebuild this mod, which re-embeds it. Never re-run the baseline generator -
    /// it rebuilds the prefab from scratch and discards the layout.</para>
    ///
    /// <para><c>LoadFromMemory</c> rather than <c>LoadFromStream</c> - the streaming overload has crashed
    /// the game on this family's bundles before.</para>
    /// </summary>
    internal static class ClassesUiBundle
    {
        private const string BundleName = "firesrpgclassesui";
        private const string PanelAsset = "FiresClassesPanel_Runtime";

        private static AssetBundle _bundle;
        private static GameObject _panel;
        private static bool _tried;

        /// <summary>The panel prefab, or null when the bundle is missing or does not carry it. Logged once
        /// either way; callers fall back to the standalone window rather than throwing.</summary>
        internal static GameObject PanelPrefab
        {
            get
            {
                if (_tried) return _panel;
                _tried = true;
                try
                {
                    _bundle = Load();
                    if (_bundle == null) return null;
                    _panel = _bundle.LoadAsset<GameObject>(PanelAsset);
                    if (_panel == null)
                        FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] '{BundleName}' loaded but carries no '{PanelAsset}'. "
                            + $"Assets present: {string.Join(", ", _bundle.GetAllAssetNames())}");
                }
                catch (Exception ex)
                {
                    FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] could not load the UI bundle ({ex.GetType().Name}: {ex.Message}). "
                        + "The inventory tab will be skipped; the standalone window still works.");
                }
                return _panel;
            }
        }

        private static AssetBundle Load()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string resource = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith(BundleName, StringComparison.OrdinalIgnoreCase));
            if (resource == null)
            {
                FiresRPGClassesPlugin.Log?.LogWarning($"[Classes] no embedded resource ending with '{BundleName}' - the bundle "
                    + "was not built into this DLL.");
                return null;
            }

            using (Stream stream = assembly.GetManifestResourceStream(resource))
            {
                if (stream == null) return null;
                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    return AssetBundle.LoadFromMemory(copy.ToArray());
                }
            }
        }
    }
}
