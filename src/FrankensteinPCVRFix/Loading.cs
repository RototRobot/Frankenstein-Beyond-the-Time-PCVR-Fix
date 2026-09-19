// Level-loading fix.
//
// Off Oculus, the game loads every scene through SteamVR_LoadLevel, which ends each load with
// Shader.WarmupAllShaders(): one blocking call that froze the game for 33 s before the menu and 21 s before
// the game on the test machine (GTX 1080 Ti), with the compositor's "Please wait" overlay up the whole time.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace FrankensteinPCVRFix
{
    /// <summary>Replacements for calls inside the game's level-loading coroutines.</summary>
    public static class LoadingHooks
    {
        public static void WarmupAllShaders()
        {
            if (Plugin.SkipShaderWarmup.Value)
            {
                Diag.Info("[loading] Shader.WarmupAllShaders() skipped");
                return;
            }

            Diag.Info("[loading] Shader.WarmupAllShaders() begin");
            string previous = Watchdog.Phase;
            Watchdog.Phase = "Shader.WarmupAllShaders()";
            var timer = Stopwatch.StartNew();
            Shader.WarmupAllShaders();
            Watchdog.Phase = previous;
            Diag.Info(string.Format("[loading] Shader.WarmupAllShaders() took {0:F1}s", timer.Elapsed.TotalSeconds));
        }

        public static void SetBackgroundLoadingPriority(UnityEngine.ThreadPriority requested)
        {
            UnityEngine.ThreadPriority applied = Plugin.FastSceneLoading.Value ? UnityEngine.ThreadPriority.High : requested;
            Diag.Info(string.Format("[loading] backgroundLoadingPriority requested {0}, applied {1}", requested, applied));
            Application.backgroundLoadingPriority = applied;
        }
    }

    internal static class LoadingPatches
    {
        public static void Apply(Harmony harmony)
        {
            Type self = typeof(LoadingPatches);
            Type sceneLoader = AccessTools.TypeByName("SceneLoader");

            // Both loaders' coroutines: route WarmupAllShaders and backgroundLoadingPriority through LoadingHooks.
            PatchUtil.Coroutine(harmony, self, typeof(SteamVR_LoadLevel), "LoadLevel", "HookLoadingCalls");
            PatchUtil.Coroutine(harmony, self, sceneLoader, "StartOculusLoader", "HookLoadingCalls");

            // Diagnostics only.
            PatchUtil.Method(harmony, self, typeof(SteamVR_LoadLevel), "Trigger", null, prefix: "TriggerPrefix");
            PatchUtil.Method(harmony, self, sceneLoader, "LoadNextScene", new[] { typeof(Transform) }, prefix: "LoadNextScenePrefix");
        }

        private static IEnumerable<CodeInstruction> HookLoadingCalls(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            MethodInfo warmup = AccessTools.Method(typeof(Shader), "WarmupAllShaders");
            MethodInfo setPriority = AccessTools.PropertySetter(typeof(Application), "backgroundLoadingPriority");
            int hooked = 0;

            foreach (CodeInstruction ci in instructions)
            {
                if (ci.opcode == OpCodes.Call && Equals(ci.operand, warmup))
                {
                    ci.operand = AccessTools.Method(typeof(LoadingHooks), "WarmupAllShaders");
                    hooked++;
                }
                else if (ci.opcode == OpCodes.Call && Equals(ci.operand, setPriority))
                {
                    ci.operand = AccessTools.Method(typeof(LoadingHooks), "SetBackgroundLoadingPriority");
                    hooked++;
                }
                yield return ci;
            }

            Type t = original.DeclaringType;
            Diag.Info(string.Format("[patch] {0}/{1}.{2}: {3} loading call(s) hooked",
                t.DeclaringType != null ? t.DeclaringType.Name : "?", t.Name, original.Name, hooked));
        }

        private static void TriggerPrefix(SteamVR_LoadLevel __instance)
        {
            SteamVR_LoadLevel l = __instance;
            Diag.Info(string.Format(
                "[loading] SteamVR_LoadLevel.Trigger on '{0}': level='{1}' busy={2} async={3} additive={4} autoTrigger={5} " +
                "loadingScreen={6} progressBar={7} dist={8} fadeOut={9} fadeIn={10} settle={11} grid={12} skybox={13} | {14}",
                l.name, l.levelName, SteamVR_LoadLevel.loading, l.loadAsync, l.loadAdditive, l.autoTriggerOnEnable,
                Diag.Describe(l.loadingScreen), Diag.Describe(l.progressBarEmpty), l.loadingScreenDistance,
                l.fadeOutTime, l.fadeInTime, l.postLoadSettleTime, l.showGrid, l.front != null, Diag.Callers(1)));
        }

        private static void LoadNextScenePrefix(MonoBehaviour __instance)
        {
            object scene = Traverse.Create(__instance).Field("sceneName").GetValue();
            string device = XRSettings.loadedDeviceName;
            Diag.Info(string.Format("[loading] SceneLoader.LoadNextScene('{0}') on device '{1}' -> {2} | {3}",
                scene, device, device == "Oculus" ? "game's Oculus loader" : "SteamVR_LoadLevel", Diag.Callers(1)));
        }
    }
}
