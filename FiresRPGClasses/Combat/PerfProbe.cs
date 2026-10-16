using System.Collections.Generic;
using System.Diagnostics;

namespace FiresRPGClasses.Combat
{
    /// <summary>0.1.119 (perf audit): time and call counts of a few hot hooks, one Debug line per hook every 60 s.</summary>
    internal static class PerfProbe
    {
        private sealed class Slot { public long Ticks; public int Calls; }
        private static readonly Dictionary<string, Slot> Slots = new Dictionary<string, Slot>();
        private static float _next = -1f;

        internal static void Add(string name, long ticks)
        {
            if (!Slots.TryGetValue(name, out Slot s)) { s = new Slot(); Slots[name] = s; }
            s.Ticks += ticks; s.Calls++;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (_next < 0f) { _next = now + 60f; return; }
            if (now < _next) return;
            float secs = 60f + (now - _next);
            _next = now + 60f;
            foreach (var kv in Slots)
            {
                double ms = kv.Value.Ticks * 1000.0 / Stopwatch.Frequency;
                FiresRPGClassesPlugin.Log?.LogDebug($"[Perf] {kv.Key}: {kv.Value.Calls / secs:0} calls/s, {ms / secs:0.000} ms/s, {(kv.Value.Calls > 0 ? ms * 1000.0 / kv.Value.Calls : 0):0.00} us a call");
                kv.Value.Ticks = 0; kv.Value.Calls = 0;
            }
        }
    }
}