using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// VCS's form contract (Tools\WEREWOLF_CLASS_DESIGN.md §1; ValheimCustomizationStudio.VcsApi, add-only from VCS 0.8.5), reached by
    /// reflection so RPGClasses neither references VCS nor needs it installed:
    /// - bool SetForm(Player, string prefab, bool active): false (and nothing done) when model swaps are off, the prefab isn't on the
    ///   synced allowlist or the player isn't local; active = false restores the saved look;
    /// - bool IsInForm(Player).
    /// Looked up once, with the exact parameter types. Without VCS (or before 0.8.5) both answer false and Wolf Form keeps its stats
    /// without the look (logged once by the caller).
    /// </summary>
    internal static class VcsBridge
    {
        // 0.1.138 (Fire 10-14, Tools\VCS_TO_FCC_REBRAND_PLAN.md §3/§5): VCS becomes Fires Character Creator. FccApi has the same
        // members; it's looked up first, VcsApi stays as the fallback so either mod (or a rollback) keeps working.
        private const string FccTypeName = "FiresCharacterCreator.FccApi";
        private const string TypeName = "ValheimCustomizationStudio.VcsApi";
        private static bool _apiTried;
        private static float _apiRetryAt;
        private static Type _api;

        /// <summary>FccApi when Fires Character Creator is loaded, else VCS's VcsApi, else null.</summary>
        internal static Type Api
        {
            get
            {
                // 10-14 ([classes-ui]): a miss is no longer cached for the session. Asked before Fires Character Creator had loaded,
                // the old code kept null forever, so the Character page button and the Appearance tab never showed. A miss
                // retries at most every 5 s; a hit is kept.
                if (_api != null || (_apiTried && Time.unscaledTime < _apiRetryAt)) return _api;
                _apiTried = true;
                _apiRetryAt = Time.unscaledTime + 5f;
                _api = AccessTools.TypeByName(FccTypeName) ?? AccessTools.TypeByName(TypeName);
                FiresRPGClassesPlugin.Log?.LogDebug($"[Races] character API: {(_api == null ? "none yet (neither Fires Character Creator nor VCS; retrying)" : _api.FullName)}");
                return _api;
            }
        }

        /// <summary>True when the loaded look mod is Fires Character Creator (keys fcc_*); VCS's are vcs_*.</summary>
        internal static bool IsFcc => Api != null && Api.FullName == FccTypeName;

        /// <summary>The look mod's key name: "fcc_model" with FCC, "vcs_model" with VCS (or neither).</summary>
        internal static string Key(string suffix) => (IsFcc ? "fcc_" : "vcs_") + suffix;

        private static bool _resolved;
        private static MethodInfo _setForm;
        private static MethodInfo _isInForm;
        private static MethodInfo _setArmorHidden;   // VCS 0.8.8+: bool SetArmorHidden(Player, bool) - synced fcc_armor_hidden, visuals only

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                Type api = Api;
                if (api != null)
                {
                    _setForm = api.GetMethod("SetForm", BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(Player), typeof(string), typeof(bool) }, null);
                    _isInForm = api.GetMethod("IsInForm", BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(Player) }, null);
                    _setArmorHidden = api.GetMethod("SetArmorHidden", BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(Player), typeof(bool) }, null);
                }
                FiresRPGClassesPlugin.Log?.LogDebug($"[Werewolf] VCS form API: {(api == null ? "VCS not installed" : $"SetForm {(_setForm != null ? "found" : "missing")}, IsInForm {(_isInForm != null ? "found" : "missing")}")}");
            }
            catch (Exception e)
            {
                FiresRPGClassesPlugin.Log?.LogDebug($"[Werewolf] VCS form API lookup failed: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>True when VCS has the 0.8.5 form calls.</summary>
        internal static bool Available { get { Resolve(); return _setForm != null; } }

        /// <summary>VcsApi.SetForm; false without VCS 0.8.5, or when VCS refused (see the contract above).</summary>
        internal static bool SetForm(Player player, string prefab, bool active)
        {
            Resolve();
            if (_setForm == null || player == null) return false;
            try { return (bool)_setForm.Invoke(null, new object[] { player, prefab, active }); }
            catch (Exception e)
            {
                FiresRPGClassesPlugin.Log?.LogDebug($"[Werewolf] VcsApi.SetForm({prefab}, {active}) threw {e.GetType().Name}: {(e.InnerException ?? e).Message}");
                return false;
            }
        }

        /// <summary>VcsApi.IsInForm; false without VCS 0.8.5.</summary>
        internal static bool IsInForm(Player player)
        {
            Resolve();
            if (_isInForm == null || player == null) return false;
            try { return (bool)_isInForm.Invoke(null, new object[] { player }); }
            catch { return false; }
        }
    
        // VCS 0.8.6+ race contract (Tools\VAMPIRE_CLASS_DESIGN.md §9): bool SetRace(Player, string race) applies the look, saves the
        // pre-turn look once, publishes ZDO rpg_race; "Werewolf" answers false when model swaps are off / Fenring isn't allowlisted
        // (the caller keeps the disease and retries at the next login); "Human" always works. string GetRace(Player).
        private static bool _raceResolved;
        private static MethodInfo _setRace, _getRace;

        private static void ResolveRace()
        {
            if (_raceResolved) return;
            _raceResolved = true;
            try
            {
                Type api = Api;
                if (api == null) return;
                _setRace = api.GetMethod("SetRace", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Player), typeof(string) }, null);
                _getRace = api.GetMethod("GetRace", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Player) }, null);
                FiresRPGClassesPlugin.Log?.LogDebug($"[Races] VCS race API: SetRace {(_setRace != null ? "found" : "missing")}, GetRace {(_getRace != null ? "found" : "missing")}");
            }
            catch (Exception e) { FiresRPGClassesPlugin.Log?.LogDebug($"[Races] VCS race API lookup failed: {e.GetType().Name}: {e.Message}"); }
        }

        internal static bool RaceAvailable { get { ResolveRace(); return _setRace != null; } }

        /// <summary>VcsApi.SetRace; false without VCS 0.8.6+, or when VCS refused.</summary>
        internal static bool SetRace(Player player, string race)
        {
            ResolveRace();
            if (_setRace == null || player == null) return false;
            try { return (bool)_setRace.Invoke(null, new object[] { player, race }); }
            catch (Exception e)
            {
                FiresRPGClassesPlugin.Log?.LogDebug($"[Races] VcsApi.SetRace({race}) threw {e.GetType().Name}: {(e.InnerException ?? e).Message}");
                return false;
            }
        }

        /// <summary>VcsApi.GetRace, or "" without it.</summary>
        internal static string GetRace(Player player)
        {
            ResolveRace();
            if (_getRace == null || player == null) return "";
            try { return _getRace.Invoke(null, new object[] { player }) as string ?? ""; } catch { return ""; }
        }

        /// <summary>VcsApi.SetArmorHidden (VCS 0.8.8+, Fire: armour hidden in wolf form, stats kept); false without it.</summary>
        internal static bool SetArmorHidden(Player player, bool hidden)
        {
            Resolve();
            if (_setArmorHidden == null || player == null) return false;
            try { return (bool)_setArmorHidden.Invoke(null, new object[] { player, hidden }); }
            catch { return false; }
        }
    }
}