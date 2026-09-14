// Frankenstein: Beyond the Time - PCVR Fix
// BepInEx 5 plugin for the game's SteamVR path: removes the level-load freeze and makes thumbstick
// controllers work with a game whose SteamVR controls were built for Vive wands.

using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace FrankensteinPCVRFix
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("Frankenstein.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dave.frankenstein.pcvrfix";
        public const string PluginName = "Frankenstein PCVR Fix";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> SkipShaderWarmup;
        internal static ConfigEntry<bool> FastSceneLoading;
        internal static ConfigEntry<bool> ThumbstickAsTouchpad;
        internal static ConfigEntry<float> ThumbstickDeadzone;
        internal static ConfigEntry<bool> ThumbstickLocomotion;
        internal static ConfigEntry<bool> ThumbstickControlHints;
        internal static ConfigEntry<float> SnapTurnDegrees;
        internal static ConfigEntry<float> SmoothTurnSpeed;
        internal static ConfigEntry<bool> LogStatus;
        internal static ConfigEntry<bool> LogControllerInput;

        private void Awake()
        {
            Log = Logger;
            Diag.Open(Path.Combine(Paths.BepInExRootPath, "FrankensteinPCVRFix.log"));

            SkipShaderWarmup = Config.Bind("Loading", "SkipShaderWarmup", true,
                "Skip the Shader.WarmupAllShaders() call the SteamVR level loader makes after every scene load. " +
                "It blocks the game for 20-60 s behind the 'Please wait' screen; shaders compile on first use instead.");
            FastSceneLoading = Config.Bind("Loading", "FastSceneLoading", false,
                "Load scenes at Application.backgroundLoadingPriority High instead of the SteamVR loader's Low. " +
                "Small gain: the async loads themselves measured 1-5 s.");
            ThumbstickAsTouchpad = Config.Bind("Controls", "ThumbstickAsTouchpad", true,
                "Feed the thumbstick (legacy axis 2) into the Vive touchpad slot the game reads (axis 0) when that slot is idle.");
            ThumbstickDeadzone = Config.Bind("Controls", "ThumbstickDeadzone", 0.15f,
                new ConfigDescription("Stick deflection below which the emulated touchpad counts as untouched.",
                    new AcceptableValueRange<float>(0f, 0.9f)));
            ThumbstickLocomotion = Config.Bind("Controls", "ThumbstickLocomotion", true,
                "Smooth locomotion on thumbstick controllers uses the game's Oculus rule (push the stick to move) " +
                "instead of its Vive-wand rule (hold a right-hand button).");
            ThumbstickControlHints = Config.Bind("Controls", "ThumbstickControlHints", true,
                "Show the menu's Oculus Touch control hints for thumbstick controllers instead of the Vive wand ones.");
            SnapTurnDegrees = Config.Bind("Controls", "SnapTurnDegrees", 30f,
                new ConfigDescription("Right-stick snap turn step, in degrees. 0 turns right-stick turning off.",
                    new AcceptableValueRange<float>(0f, 90f)));
            SmoothTurnSpeed = Config.Bind("Controls", "SmoothTurnSpeed", 0f,
                new ConfigDescription("Right-stick smooth turning, in degrees per second. Above 0 it replaces snap turning.",
                    new AcceptableValueRange<float>(0f, 360f)));
            LogStatus = Config.Bind("Diagnostics", "LogStatus", false,
                "For bug reports: log loader phases, frozen frames, pause state, scene changes and the control wiring " +
                "to BepInEx\\FrankensteinPCVRFix.log.");
            LogControllerInput = Config.Bind("Diagnostics", "LogControllerInput", false,
                "For bug reports: log raw OpenVR controller button and axis changes.");

            Diag.Info(string.Format("{0} {1}, Unity {2}, VR SDKs [{3}]",
                PluginName, Version, Application.unityVersion, string.Join(", ", XRSettings.supportedDevices)));
            Diag.Info(string.Format(
                "config: SkipShaderWarmup={0} FastSceneLoading={1} ThumbstickAsTouchpad={2} ThumbstickDeadzone={3} " +
                "ThumbstickLocomotion={4} ThumbstickControlHints={5} SnapTurnDegrees={6} SmoothTurnSpeed={7}",
                SkipShaderWarmup.Value, FastSceneLoading.Value, ThumbstickAsTouchpad.Value, ThumbstickDeadzone.Value,
                ThumbstickLocomotion.Value, ThumbstickControlHints.Value, SnapTurnDegrees.Value, SmoothTurnSpeed.Value));

            var harmony = new Harmony(Guid);
            LoadingPatches.Apply(harmony);
            ControlPatches.Apply(harmony);
            if (LogStatus.Value) DiagnosticPatches.Apply(harmony);

            var host = new GameObject("FrankensteinPCVRFix");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            host.AddComponent<StickTurn>();
            if (LogStatus.Value || LogControllerInput.Value) host.AddComponent<Monitor>();
            if (LogStatus.Value) Watchdog.Start();
        }
    }

    /// <summary>Harmony registration helpers. A failed patch is logged and skipped, never fatal.</summary>
    internal static class PatchUtil
    {
        public static void Method(Harmony harmony, Type patches, Type type, string method, Type[] args,
            string prefix = null, string postfix = null)
        {
            string label = Label(type) + "." + method;
            try
            {
                MethodBase target = null;
                if (type != null)
                    target = args == null ? AccessTools.Method(type, method) : AccessTools.Method(type, method, args);
                Apply(harmony, patches, target, label, prefix, postfix, null);
            }
            catch (Exception e)
            {
                Diag.Error("[patch] " + label + " failed: " + e);
            }
        }

        public static void Coroutine(Harmony harmony, Type patches, Type owner, string method, string transpiler)
        {
            string label = Label(owner) + "." + method + " (coroutine)";
            try
            {
                MethodBase target = null;
                if (owner != null)
                    foreach (Type nested in owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                        if (nested.Name.StartsWith("<" + method + ">", StringComparison.Ordinal))
                            target = AccessTools.Method(nested, "MoveNext");
                Apply(harmony, patches, target, label, null, null, transpiler);
            }
            catch (Exception e)
            {
                Diag.Error("[patch] " + label + " failed: " + e);
            }
        }

        private static void Apply(Harmony harmony, Type patches, MethodBase target, string label,
            string prefix, string postfix, string transpiler)
        {
            if (target == null)
            {
                Diag.Warn("[patch] target not found: " + label);
                return;
            }
            harmony.Patch(target,
                prefix == null ? null : new HarmonyMethod(patches, prefix),
                postfix == null ? null : new HarmonyMethod(patches, postfix),
                transpiler == null ? null : new HarmonyMethod(patches, transpiler));
            Diag.Info("[patch] " + label);
        }

        private static string Label(Type t)
        {
            if (t == null) return "?";
            return t.DeclaringType != null ? t.DeclaringType.Name + "." + t.Name : t.Name;
        }
    }
}
