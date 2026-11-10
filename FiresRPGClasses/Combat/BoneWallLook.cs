using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// So the Bone Wall reads as bones rather than a single line of stone blocks, it keeps its three
    /// stone_wall_2x1 pieces for the collider and the health (unchanged behaviour), but their stone meshes are hidden and each
    /// piece is dressed in vanilla bone props - bone_stack, skull_pile, BonePileSpawner's pile and BoneFragments - staggered in
    /// depth, turned and scaled so the wall reads as heaped bones of varied heights.
    /// Cheap: mesh copies only (MeshFilter + MeshRenderer, no colliders, no scripts, nothing per frame), built once when a piece
    /// wakes. Every peer dresses it the same way: the caster marks each piece's ZDO, any client whose WearNTear wakes with the
    /// mark dresses it, and the layout is seeded by the ZDO id.
    /// </summary>
    public static class BoneWallLook
    {
        internal static readonly int MarkKey = "rpg_bonewall".GetStableHashCode();

        // (prefab, how many per 2 m piece, scale range) - all vanilla, looked up by name; a missing one is skipped and logged once.
        private static readonly (string prefab, int count, float min, float max)[] Props =
        {
            ("bone_stack", 3, 0.9f, 1.5f),
            ("skull_pile", 2, 0.8f, 1.3f),
            ("BonePileSpawner", 1, 1.0f, 1.4f),
            ("BoneFragments", 3, 1.5f, 2.5f),
        };

        private static readonly Dictionary<string, List<(Mesh mesh, Material[] mats, Matrix4x4 local)>> Cache =
            new Dictionary<string, List<(Mesh, Material[], Matrix4x4)>>();
        private static readonly HashSet<string> Missing = new HashSet<string>();

        /// <summary>The caster's side: mark the piece for every peer and dress it here.</summary>
        internal static void MarkAndDress(GameObject piece)
        {
            ZNetView nview = piece != null ? piece.GetComponent<ZNetView>() : null;
            if (nview != null && nview.IsValid()) nview.GetZDO().Set(MarkKey, true);
            Dress(piece);
        }

        internal static void Dress(GameObject piece)
        {
            if (piece == null || piece.transform.Find("FiresBoneWall") != null) return;
            ZNetView nview = piece.GetComponent<ZNetView>();
            int seed = nview != null && nview.IsValid() ? (int)(nview.GetZDO().m_uid.ID & 0x7fffffff) : piece.GetInstanceID();
            var rng = new System.Random(seed);

            // the stone's look goes; its collider and WearNTear stay
            foreach (MeshRenderer r in piece.GetComponentsInChildren<MeshRenderer>(true)) r.enabled = false;

            var root = new GameObject("FiresBoneWall");
            root.transform.SetParent(piece.transform, false);
            int made = 0;
            foreach (var p in Props)
            {
                List<(Mesh mesh, Material[] mats, Matrix4x4 local)> parts = Parts(p.prefab);
                if (parts == null) continue;
                for (int i = 0; i < p.count; i++)
                {
                    // along the 2 m piece (x), a little in front/behind (z), sitting on the ground (y 0), random yaw and size
                    float x = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.95f;
                    float z = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.35f;
                    float yLift = p.prefab == "BoneFragments" ? (float)rng.NextDouble() * 1.4f : 0f;   // scattered up the heap
                    float s = p.min + (float)rng.NextDouble() * (p.max - p.min);
                    var holder = new GameObject(p.prefab);
                    holder.transform.SetParent(root.transform, false);
                    holder.transform.localPosition = new Vector3(x, yLift, z);
                    holder.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    holder.transform.localScale = Vector3.one * s;
                    foreach (var part in parts)
                    {
                        var go = new GameObject("mesh");
                        go.transform.SetParent(holder.transform, false);
                        go.transform.localPosition = part.local.GetColumn(3);
                        go.transform.localRotation = part.local.rotation;
                        go.transform.localScale = part.local.lossyScale;
                        go.AddComponent<MeshFilter>().sharedMesh = part.mesh;
                        var mr = go.AddComponent<MeshRenderer>();
                        mr.sharedMaterials = part.mats;
                    }
                    made++;
                }
            }
            FiresRPGClassesPlugin.Log?.LogDebug($"[BoneWall] dressed {piece.name} ({seed}): {made} bone prop(s){(Missing.Count > 0 ? ", missing: " + string.Join(", ", Missing) : "")}");
        }

        // The prefab's visible meshes, relative to its root, read once.
        private static List<(Mesh, Material[], Matrix4x4)> Parts(string prefab)
        {
            if (Cache.TryGetValue(prefab, out var parts)) return parts;
            GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
            if (go == null && ObjectDB.instance != null) go = ObjectDB.instance.GetItemPrefab(prefab);
            if (go == null) { Missing.Add(prefab); Cache[prefab] = null; return null; }
            parts = new List<(Mesh, Material[], Matrix4x4)>();
            Matrix4x4 toRoot = go.transform.worldToLocalMatrix;
            foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshRenderer mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null) continue;
                if (mf.GetComponentInParent<LODGroup>() is LODGroup lod && !IsFirstLod(lod, mr)) continue;   // the best LOD only
                parts.Add((mf.sharedMesh, mr.sharedMaterials, toRoot * mf.transform.localToWorldMatrix));
            }
            Cache[prefab] = parts;
            return parts;
        }

        private static bool IsFirstLod(LODGroup group, Renderer r)
        {
            LOD[] lods = group.GetLODs();
            if (lods.Length == 0) return true;
            foreach (Renderer x in lods[0].renderers) if (x == r) return true;
            return false;
        }

        /// <summary>Remote peers: a marked wall piece is dressed when it wakes. One ZDO bool lookup per WearNTear.</summary>
        [HarmonyPatch(typeof(WearNTear), "Awake")]
        private static class DressOnWake
        {
            [HarmonyPostfix]
            private static void After(WearNTear __instance)
            {
                // 0.1.220 ([wishbone], audit row 9): RPGClasses' ONE WearNTear.Awake postfix - Sturdy Builder's load raise (was its own
                // CraftBatch4.Loaded patch, a second Harmony call + GetComponent<Piece> per piece) runs here, its value tested first.
                Paragon.CraftBatch4.OnWake(__instance);
                ZNetView nview = __instance.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid() || !nview.GetZDO().GetBool(MarkKey)) return;
                Dress(__instance.gameObject);
            }
        }
    }
}
