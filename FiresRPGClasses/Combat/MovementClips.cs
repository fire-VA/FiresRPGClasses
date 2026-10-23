using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FiresCore.Animation;
using UnityEngine;

namespace FiresRPGClasses.Combat
{
    /// <summary>
    /// The "firesmovementclips" bundle (26 CC0 Quaternius UAL clips from the
    /// full sandbox library, imported Humanoid; Valheim's player avatar is Humanoid, so they play on it as they are) is embedded in this
    /// DLL and registered with Core's ClipSlots (FiresCore.Animation) once at client start-up, under the ids the movement
    /// styles and Core's own moves play. Registering is what turns Core's [Movement] "Clip slots" Auto on (a consumer).
    /// Not on a dedicated server (no animator there). A missing bundle or an older Core leaves every move on its vanilla fallback.
    /// </summary>
    public static class MovementClips
    {
        private const string BundleResource = "firesmovementclips";
        private static AssetBundle _bundle;
        private static bool _done;

        // id -> clip name + its default mode
        private static readonly (string id, string clip, ClipMode mode)[] Map =
        {
            // Core's own moves (Core plays these ids when they're registered; vanilla jump / crouch otherwise)
            ("core.double_jump", "DoubleJump", ClipMode.OneShot),
            ("core.run_slide", "Slide_Start", ClipMode.OneShot),
            ("core.run_slide.loop", "Slide_Loop", ClipMode.Loop),
            ("core.run_slide.exit", "Slide_Exit", ClipMode.OneShot),
            // class / race second jumps and dodges (ClassMovement)
            ("rpgclasses.flip.front", "JogToFlip", ClipMode.OneShot),
            ("rpgclasses.flip.back", "BackFlip", ClipMode.OneShot),
            ("rpgclasses.vault", "SafetyVault", ClipMode.OneShot),
            ("rpgclasses.leap", "NinjaJump_Start", ClipMode.OneShot),
            ("rpgclasses.pounce", "NinjaJump_Double", ClipMode.OneShot),
            ("rpgclasses.float", "DoubleJump", ClipMode.OneShot),
            ("rpgclasses.bathop", "NinjaJump_Double", ClipMode.OneShot),
            ("rpgclasses.mage.blink", "Spell_Simple_Shoot", ClipMode.OneShot),
            ("rpgclasses.vampire.miststep", "Dodge_Left", ClipMode.OneShot),
            ("rpgclasses.werewolf.pounce", "Sword_Aerial_A", ClipMode.OneShot),
            ("rpgclasses.dodge.left", "Dodge_Left", ClipMode.OneShot),
            ("rpgclasses.dodge.right", "Dodge_Right", ClipMode.OneShot),
            ("rpgclasses.roll", "Roll", ClipMode.OneShot),
            ("rpgclasses.shield_dash", "Shield_Dash", ClipMode.OneShot),
            ("rpgclasses.dash", "Sword_Dash", ClipMode.OneShot),
            ("rpgclasses.werewolf.turn", "MonsterTransformation", ClipMode.OneShot),
            // staff attacks (VABackpacks plays these ids when present; the vanilla staff anims otherwise)
            ("fires.staff.channel.enter", "Spell_Double_Enter", ClipMode.OneShot),
            ("fires.staff.channel.hold", "Spell_Double_Idle_Loop", ClipMode.Hold),
            ("fires.staff.channel.release", "Spell_Double_Shoot_Loop", ClipMode.OneShot),
            ("fires.staff.channel.exit", "Spell_Double_Exit", ClipMode.OneShot),
            ("fires.staff.cast.enter", "Spell_Simple_Enter", ClipMode.OneShot),
            ("fires.staff.cast.hold", "Spell_Simple_Idle_Loop", ClipMode.Hold),
            ("fires.staff.cast", "Spell_Simple_Shoot", ClipMode.OneShot),
            ("fires.staff.lob", "OverhandThrow", ClipMode.OneShot),
            ("fires.staff.groundslam", "Sword_GroundPound", ClipMode.OneShot),
        };

        /// <summary>Client start-up: load the embedded bundle and register every id with Core's ClipSlots. Safe to call more than once.</summary>
        public static void RegisterAll()
        {
            if (_done) return;
            _done = true;
            try
            {
                Assembly asm = typeof(MovementClips).Assembly;
                string res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + BundleResource, StringComparison.OrdinalIgnoreCase) || n == BundleResource);
                if (res == null) { FiresRPGClassesPlugin.Log?.LogDebug("[Movement] clips: no firesmovementclips bundle in this build; every move keeps its vanilla animation"); return; }
                using (Stream s = asm.GetManifestResourceStream(res))
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    _bundle = AssetBundle.LoadFromMemory(ms.ToArray());
                }
                if (_bundle == null) { FiresRPGClassesPlugin.Log?.LogWarning("[Movement] clips: the firesmovementclips bundle didn't load"); return; }
                var clips = _bundle.LoadAllAssets<AnimationClip>().Where(c => c != null).ToDictionary(c => c.name, c => c);
                int ok = 0;
                var missing = new List<string>();
                foreach (var (id, clip, mode) in Map)
                {
                    if (!clips.TryGetValue(clip, out var c)) { missing.Add(clip); continue; }
                    if (ClipSlots.Register(id, c, new ClipOptions { Mode = mode })) ok++;
                }
                FiresRPGClassesPlugin.Log?.LogDebug($"[Movement] clips: {clips.Count} clip(s) in firesmovementclips (humanoid {clips.Values.Count(c => c.humanMotion)}), {ok}/{Map.Length} id(s) registered with Core's ClipSlots{(missing.Count > 0 ? "; missing clips: " + string.Join(", ", missing.Distinct().ToArray()) : "")}");
            }
            catch (Exception e)
            {
                // TypeLoad / MissingMethod here = an older Core (no ClipSlots): the moves keep their fallbacks
                FiresRPGClassesPlugin.Log?.LogWarning("[Movement] clips: not registered (Core without ClipSlots?): " + e.GetBaseException().Message);
            }
        }
    }
}
