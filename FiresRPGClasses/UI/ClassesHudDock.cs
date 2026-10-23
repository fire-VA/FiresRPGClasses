using System.Linq;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace FiresRPGClasses.UI
{
    /// <summary>
    /// One reflection wrapper over
    /// Core's FiresShell.RegisterHudElement / UnregisterHudElement, so RPGClasses' HUD pieces join Core's dock engine on any Core
    /// without a hard reference (an older Core, or the override OFF, just says why and the caller keeps placing itself). Override flips
    /// arrive through FiresShell.OverrideChanged as a flag the callers read on the main thread. (ClassesHud's bars keep their own
    /// copy of this; same calls.)
    /// </summary>
    internal static class ClassesHudDock
    {
        private const string ShellType = "FiresCore.UI.Shell.FiresShell, FiresUnifiedCore";

        /// <summary>Bumped on every override flip; a caller compares it with the value it last saw.</summary>
        internal static int OverrideFlips { get; private set; }
        private static bool s_hooked;

        internal static void HookOverride()
        {
            if (s_hooked) return;
            s_hooked = true;
            try
            {
                var ev = Type.GetType(ShellType, false)?.GetEvent("OverrideChanged");
                if (ev != null) ev.AddEventHandler(null, new Action<bool>(_ => OverrideFlips++));
            }
            catch (Exception e) { FiresRPGClassesPlugin.Log?.LogWarning($"[ClassesHud] dock: OverrideChanged subscribe failed ({e.GetType().Name})"); }
        }

        /// <summary>Registers; null = registered, else why not (the caller keeps its own placement).</summary>
        internal static string Register(string id, string label, RectTransform rect, Func<bool> visible, string dockTo = null, string edge = null, bool showInLayout = false,
            Action<Vector2> sizeChanged = null, Vector2 minSize = default(Vector2))
        {
            try { return RegisterCore(id, label, rect, visible, dockTo, edge, showInLayout, sizeChanged, minSize); }
            catch (Exception e) { return "failed: " + e.GetBaseException().Message; }
        }

        internal static void Unregister(string id)
        {
            try { UnregisterCore(id); }
            catch (Exception e) { FiresRPGClassesPlugin.Log?.LogWarning($"[ClassesHud] dock: UnregisterHudElement('{id}') failed: {e.GetBaseException().Message}"); }
        }

        internal static bool OverrideOn()
        {
            try { return Type.GetType(ShellType, false)?.GetProperty("OverrideOn")?.GetValue(null) is bool on && on; }
            catch { return false; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string RegisterCore(string id, string label, RectTransform rect, Func<bool> visible, string dockTo, string edge, bool showInLayout,
            Action<Vector2> sizeChanged, Vector2 minSize)
        {
            // HudElementOptions' namespace is FiresCore.UI.Shell, not the file's folder (UI\Dock): take the type from RegisterHudElement itself
            Type shell = Type.GetType(ShellType, false);
            var reg = shell?.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "RegisterHudElement" && m.GetParameters().Length == 3 && m.GetParameters()[1].ParameterType == typeof(RectTransform));
            Type optT = reg?.GetParameters()[2].ParameterType;
            if (reg == null) return "Core has no RegisterHudElement (older than 0.2.522)";
            if (!OverrideOn()) return "the Fires UI override is off";
            if (rect == null) return "no rect";
            object opts = Activator.CreateInstance(optT);
            optT.GetField("Owner")?.SetValue(opts, "FiresRPGClasses");
            optT.GetField("Label")?.SetValue(opts, label);
            optT.GetField("Resizable")?.SetValue(opts, true);
            if (visible != null) optT.GetField("Visible")?.SetValue(opts, visible);
            // Shown in layout mode even while hidden in play (an older Core lacks the field: skipped)
            if (showInLayout) { var sil = optT.GetField("ShowInLayout"); if (sil != null && sil.FieldType == typeof(bool)) sil.SetValue(opts, true); }
            if (!string.IsNullOrEmpty(dockTo))
            {
                optT.GetField("DefaultDockTo")?.SetValue(opts, dockTo);
                var edgeF = optT.GetField("DefaultEdge");
                if (edgeF != null && !string.IsNullOrEmpty(edge)) edgeF.SetValue(opts, Enum.Parse(edgeF.FieldType, edge));
            }
            SetSizeOptions(optT, opts, sizeChanged, minSize);
            // Reset restores the size too (HudElementOptions.SizeReset): the rect's size at register
            // is the element's default (the saved size is applied after registering), so a reset puts that back, re-lays the content at
            // it, and drops the saved entry. An older Core lacks the field: skipped.
            if (sizeChanged != null)
            {
                Vector2 defaultSize = rect.sizeDelta;
                Action reset = () =>
                {
                    ClassesHudSizes.ResizeKeepTopLeft(rect, defaultSize);
                    sizeChanged(defaultSize);
                    ClassesHudSizes.Clear(id);
                    FiresRPGClassesPlugin.Log?.LogDebug($"[HUD] {id} reset to its default size {defaultSize.x:0}x{defaultSize.y:0} (saved size cleared)");
                };
                var fr = optT.GetField("SizeReset");
                if (fr != null && fr.FieldType == typeof(Action)) fr.SetValue(opts, reset);
                else { var pr = optT.GetProperty("SizeReset"); if (pr != null && pr.PropertyType == typeof(Action)) pr.SetValue(opts, reset); }
            }
            reg.Invoke(null, new object[] { id, rect, opts });
            return null;
        }

        /// <summary>HudElementOptions.SizeChanged / MinSize (VAInventory's pattern): the grips
        /// change the rect's size per axis and Core calls back; the owner re-lays its content. An older Core lacks the fields: skipped (scale grip).</summary>
        internal static bool SetSizeOptions(Type optT, object opts, Action<Vector2> sizeChanged, Vector2 minSize)
        {
            if (sizeChanged == null || optT == null || opts == null) return false;
            var fs = optT.GetField("SizeChanged");
            if (fs != null && fs.FieldType == typeof(Action<Vector2>)) fs.SetValue(opts, sizeChanged);
            else { var ps = optT.GetProperty("SizeChanged"); if (ps == null || ps.PropertyType != typeof(Action<Vector2>)) return false; ps.SetValue(opts, sizeChanged); }
            if (minSize != default(Vector2))
            {
                var fm = optT.GetField("MinSize");
                if (fm != null && fm.FieldType == typeof(Vector2)) fm.SetValue(opts, minSize); else optT.GetProperty("MinSize")?.SetValue(opts, minSize);
            }
            return true;
        }

        /// <summary>HudElementOptions.LiveEdit + DragHandle (as FiresAdminPrefabs'
        /// CoreUiBridge.RegisterDock: set DragHandle, then LiveEdit): the element moves / resizes at any time, no editor. An older
        /// Core lacks the fields: skipped silently (false). The handle must already be a raycast target (the caller's own grab area).</summary>
        internal static bool SetLiveEdit(Type optT, object opts, RectTransform handle)
        {
            if (optT == null || opts == null || handle == null) return false;
            var fh = optT.GetField("DragHandle");
            var fl = optT.GetField("LiveEdit");
            if (fh == null || fl == null || fh.FieldType != typeof(RectTransform) || fl.FieldType != typeof(bool)) return false;
            fh.SetValue(opts, handle);
            fl.SetValue(opts, true);
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void UnregisterCore(string id)
        {
            Type.GetType(ShellType, false)?.GetMethod("UnregisterHudElement", new[] { typeof(string) })?.Invoke(null, new object[] { id });
        }
    }

    /// <summary>
    /// A diagonal grip changes both axes, a straight one one axis (Core's SizeChanged). The size the
    /// player gave each resizable RPGClasses HUD element. Core saves position only ("the size is the owner's to save and re-apply",
    /// DockElementOptions.SizeChanged), so, as in VAInventory, the sizes live in BepInEx\cache\FiresRPGClasses\hud_sizes.txt
    /// ("id|w|h"). No saved size = today's layout exactly. Writes and the "[HUD] id resized" Debug line happen once a resize has
    /// settled (0.5 s with no new size), so a drag is one save and one line, not one per frame. Tick() allocates nothing while idle.
    /// </summary>
    internal static class ClassesHudSizes
    {
        private const float SettleSeconds = 0.5f;
        private static readonly System.Collections.Generic.Dictionary<string, Vector2> s_size = new System.Collections.Generic.Dictionary<string, Vector2>();
        private static readonly System.Collections.Generic.Dictionary<string, Vector2Int> s_grid = new System.Collections.Generic.Dictionary<string, Vector2Int>();
        private static readonly System.Collections.Generic.List<string> s_pending = new System.Collections.Generic.List<string>();
        private static bool s_loaded, s_dirty;
        private static float s_changedAt;

        private static string FilePath => System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "cache"), "FiresRPGClasses"), "hud_sizes.txt");

        internal static bool TryGet(string id, out Vector2 size)
        {
            Load();
            return s_size.TryGetValue(id, out size);
        }

        /// <summary>From an element's SizeChanged: remember the size (saved once the drag settles).</summary>
        internal static void Set(string id, Vector2 size)
        {
            Load();
            s_size[id] = size;
            if (!s_pending.Contains(id)) s_pending.Add(id);
            s_dirty = true;
            s_changedAt = Time.unscaledTime;
        }

        /// <summary>Core reset the element: forget its saved size (written now, not at the next settle).</summary>
        internal static void Clear(string id)
        {
            Load();
            bool had = s_size.Remove(id);
            s_pending.Remove(id);
            if (had) Save();
        }

        /// <summary>The grid an element last laid out (for the settled log line).</summary>
        internal static void NoteGrid(string id, int cols, int rows) => s_grid[id] = new Vector2Int(cols, rows);

        internal static void Tick()
        {
            if (!s_dirty || Time.unscaledTime - s_changedAt < SettleSeconds) return;
            s_dirty = false;
            Save();
            for (int i = 0; i < s_pending.Count; i++)
            {
                string id = s_pending[i];
                Vector2 sz = s_size[id];
                s_grid.TryGetValue(id, out var g);
                FiresRPGClassesPlugin.Log?.LogDebug($"[HUD] {id} resized to {sz.x:0}x{sz.y:0}: {g.x}x{g.y}");
            }
            s_pending.Clear();
        }

        /// <summary>Sets the rect's size with its top-left corner kept where it is (Core's resize rule).</summary>
        internal static void ResizeKeepTopLeft(RectTransform rt, Vector2 size)
        {
            if (rt == null) return;
            Vector2 cur = rt.rect.size;
            float dw = size.x - cur.x, dh = size.y - cur.y;
            if (Mathf.Abs(dw) < 0.01f && Mathf.Abs(dh) < 0.01f) return;
            rt.sizeDelta += new Vector2(dw, dh);
            Vector3 s = rt.localScale;
            rt.anchoredPosition += new Vector2(dw * rt.pivot.x * s.x, -dh * (1f - rt.pivot.y) * s.y);
        }

        /// <summary>Grows the rect's height (top kept) when the re-laid content needs more than the player gave it; never shrinks.</summary>
        internal static void GrowHeightKeepTop(RectTransform rt, float needed)
        {
            if (rt == null || rt.rect.height >= needed - 0.01f) return;
            ResizeKeepTopLeft(rt, new Vector2(rt.rect.width, needed));
        }

        /// <summary>cols for n items: as many as fit the width (pitch per item), fewer when the height fits more rows.</summary>
        internal static int Cols(int n, float width, float pitch, float gap, int minCols, int rowsThatFit)
        {
            if (n <= 0) return 1;
            int colsW = Mathf.Clamp(Mathf.FloorToInt((width + gap + 0.5f) / pitch), 1, n);
            int fit = Mathf.Max(1, rowsThatFit);
            int colsH = (n + fit - 1) / fit;
            return Mathf.Clamp(Mathf.Min(colsW, colsH), Mathf.Min(minCols, n), n);
        }

        private static void Load()
        {
            if (s_loaded) return;
            s_loaded = true;
            try
            {
                if (!System.IO.File.Exists(FilePath)) return;
                foreach (string line in System.IO.File.ReadAllLines(FilePath))
                {
                    string[] p = line.Split('|');
                    if (p.Length == 3
                        && float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float w)
                        && float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float h)
                        && w > 0f && h > 0f)
                        s_size[p[0]] = new Vector2(w, h);
                }
                FiresRPGClassesPlugin.Log?.LogInfo($"[HUD] {s_size.Count} saved HUD size(s) read from {FilePath}");
            }
            catch (Exception e) { FiresRPGClassesPlugin.Log?.LogWarning($"[HUD] HUD sizes not read: {e.Message}"); }
        }

        private static void Save()
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath));
                var sb = new System.Text.StringBuilder();
                foreach (var kv in s_size)
                    sb.Append(kv.Key).Append('|').Append(kv.Value.x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
                      .Append('|').Append(kv.Value.y.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                System.IO.File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception e) { FiresRPGClassesPlugin.Log?.LogWarning($"[HUD] HUD sizes not saved: {e.Message}"); }
        }
    }
}