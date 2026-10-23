using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FiresCore.Storage;
using UnityEngine;

namespace FiresRPGClasses.Data
{
    /// <summary>
    /// Brings the trees up: the yaml on disk is the data of record, a bundled copy seeds it on first
    /// run, and the registry stand-in covers the case where the yaml cannot be read at all. Reloads on
    /// edit. Plan: Docs/PLAN_ClassTreeLoader.md
    /// </summary>
    public static class ClassTreeLoader
    {
        public const string FileName = "classes-trees.yaml";
        private const string BundledResourceName = "FiresRPGClasses.Data.classes-trees.yaml";
        private const float ReloadDebounceSeconds = 0.5f;

        private static FileSystemWatcher _watcher;
        private static DateTime _reloadAtUtc;
        private static bool _reloadPending;

        public static string FilePath => Path.Combine(FiresConfigPaths.Classes, FileName);

        /// <summary>True when the trees came from the yaml rather than the registry stand-in.</summary>
        public static bool LoadedFromYaml { get; private set; }

        /// <summary>Raised after a successful reload, so views and bars can rebuild.</summary>
        public static event Action TreesReloaded;

        public static void Initialize()
        {
            SeedOrUpgradeFile();
            Load();
            StartWatching();
        }

        /// <summary>
        /// Reads the trees. Falls back to the registry stand-in rather than leaving the game with no
        /// trees at all, and says loudly which one is in force.
        /// </summary>
        public static void Load()
        {
            if (TryLoadYaml()) return;

            ClassTrees.Load(new RegistryTreeSource());
            LoadedFromYaml = false;
            Debug.LogWarning($"[ClassTrees] Falling back to the registry stand-in: {ClassTrees.AllNodes.Count()} "
                + "node(s) derived from Core's abilities. The real trees, the level-10 ultimates and the "
                + $"talent canvas need {FileName}.");
        }

        private static bool TryLoadYaml()
        {
            string path = FilePath;
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[ClassTrees] {path} not found.");
                return false;
            }

            try
            {
                var source = new YamlClassTreeSource(path);
                ClassTrees.Load(source);

                foreach (string warning in source.Warnings)
                    Debug.LogWarning($"[ClassTrees] {FileName}: {warning}");

                LoadedFromYaml = true;
                Debug.Log($"[ClassTrees] Loaded schema {source.Schema} from {FileName}: "
                    + $"{CountTrees()} tree(s), {ClassTrees.AllNodes.Count()} node(s), "
                    + $"{ClassTrees.TotalPointCostOfEveryTree()} point(s) to fill every tree"
                    + (source.Warnings.Count > 0 ? $", {source.Warnings.Count} warning(s)." : "."));
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClassTrees] {path} could not be read ({ex.GetType().Name}: {ex.Message}). "
                    + "Fix the file and save it; it reloads without a restart.");
                return false;
            }
        }

        // The yaml's top-level "revision: N" (0 when it has none), raised whenever the shipped trees change.
        private static readonly Regex RevisionLine = new Regex(@"^revision:\s*(\d+)\s*$", RegexOptions.Multiline);

        internal static int RevisionOf(string yaml)
        {
            Match match = RevisionLine.Match(yaml ?? "");
            return match.Success && int.TryParse(match.Groups[1].Value, out int revision) ? revision : 0;
        }

        /// <summary>
        /// Writes the bundled copy on first run so a fresh install has trees and an admin has a file to edit. A file on disk
        /// is the admin's and stays, unless the trees this version ships are a newer revision (so trees added later
        /// still reach a file seeded before them). Then the bundled trees replace it
        /// (no .bak copy is kept), said once at Warning.
        /// </summary>
        private static void SeedOrUpgradeFile()
        {
            string path = FilePath;
            try
            {
                string bundled = ReadBundled();
                if (bundled == null) return;
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, bundled);
                    Debug.Log($"[ClassTrees] Wrote the bundled {FileName} to {path}.");
                    return;
                }
                int onDisk = RevisionOf(File.ReadAllText(path));
                int shipped = RevisionOf(bundled);
                if (onDisk >= shipped) return;
                // No .r<old>.bak copy is kept
                File.WriteAllText(path, bundled);
                Debug.LogWarning($"[ClassTrees] {FileName} on disk was revision {onDisk}, older than this version's {shipped}: "
                    + "replaced with the bundled trees (no backup copy kept).");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ClassTrees] Could not write {path} ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        private static string ReadBundled()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BundledResourceName))
            {
                if (stream == null)
                {
                    Debug.LogError($"[ClassTrees] The bundled {FileName} is missing from the assembly - "
                        + $"it must be an EmbeddedResource named {BundledResourceName}.");
                    return null;
                }
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }

        private static void StartWatching()
        {
            if (_watcher != null) return;

            try
            {
                _watcher = new FileSystemWatcher(FiresConfigPaths.Classes, FileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += (_, __) => QueueReload();
                _watcher.Created += (_, __) => QueueReload();
                _watcher.Renamed += (_, __) => QueueReload();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClassTrees] Hot reload unavailable ({ex.GetType().Name}: {ex.Message}). "
                    + $"Edits to {FileName} will need a restart.");
            }
        }

        /// <summary>
        /// An editor writes a file in several bursts, so the reload waits for the writes to stop. This
        /// runs on a watcher thread, so it touches no Unity API - DateTime only, and the reload itself
        /// happens on the main thread in <see cref="Tick"/>.
        /// </summary>
        private static void QueueReload()
        {
            _reloadAtUtc = DateTime.UtcNow.AddSeconds(ReloadDebounceSeconds);
            _reloadPending = true;
        }

        /// <summary>Call once per frame from the mod's driver.</summary>
        public static void Tick()
        {
            if (!_reloadPending || DateTime.UtcNow < _reloadAtUtc) return;
            _reloadPending = false;

            Load();
            try { TreesReloaded?.Invoke(); }
            catch (Exception ex)
            {
                Debug.LogError($"[ClassTrees] A TreesReloaded subscriber threw ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        public static void Shutdown()
        {
            if (_watcher == null) return;
            try { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); } catch { }
            _watcher = null;
        }

        private static int CountTrees()
        {
            int count = 0;
            foreach (var unused in ClassTrees.Archetypes) count++;
            return count;
        }
    }
}
