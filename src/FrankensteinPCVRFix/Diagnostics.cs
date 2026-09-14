// Diagnostics: the instrumentation used to find the fixes. Everything here observes; nothing changes behaviour.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using Valve.VR;

namespace FrankensteinPCVRFix
{
    internal static class DiagnosticPatches
    {
        private static readonly Dictionary<EVREventType, int> EventCounts = new Dictionary<EVREventType, int>();
        private static FieldInfo dashPauseMode;
        private static int lastDashPauseMode = int.MinValue;

        public static void Apply(Harmony harmony)
        {
            Type self = typeof(DiagnosticPatches);
            Type menu = AccessTools.TypeByName("MenuController");
            Type dash = AccessTools.TypeByName("DashPauseHandler");
            dashPauseMode = dash == null ? null : AccessTools.Field(dash, "pauseMode");

            PatchUtil.Method(harmony, self, menu, "StartGame", null, prefix: "MenuPrefix");
            PatchUtil.Method(harmony, self, menu, "OnClickContinue", null, prefix: "MenuPrefix");
            PatchUtil.Method(harmony, self, menu, "OnClickBackToMenu", null, prefix: "MenuPrefix");
            PatchUtil.Method(harmony, self, typeof(SteamVR_Render), "OnInputFocus", null, prefix: "OnInputFocusPrefix");
            PatchUtil.Method(harmony, self, typeof(SteamVR_Events), "System", new[] { typeof(EVREventType) }, prefix: "SystemEventPrefix");
            PatchUtil.Method(harmony, self, dash, "Update", null, postfix: "DashPauseUpdatePostfix");
        }

        private static void MenuPrefix(MethodBase __originalMethod)
        {
            Diag.Info("[menu] MenuController." + __originalMethod.Name + "() | " + Diag.Callers(1));
        }

        private static void OnInputFocusPrefix(bool __0)
        {
            Diag.Info(string.Format("[pause] SteamVR_Render.OnInputFocus({0}), timeScale={1}", __0, Time.timeScale));
        }

        private static void SystemEventPrefix(EVREventType __0)
        {
            int n;
            EventCounts.TryGetValue(__0, out n);
            EventCounts[__0] = ++n;
            if (n <= 3 || n % 500 == 0)
                Diag.Info(string.Format("[vrevent] {0} (#{1})", __0, n));
        }

        private static void DashPauseUpdatePostfix(MonoBehaviour __instance)
        {
            if (dashPauseMode == null) return;
            int mode = (int)dashPauseMode.GetValue(__instance);
            if (mode == lastDashPauseMode) return;
            Diag.Info(string.Format("[pause] DashPauseHandler.pauseMode {0} -> {1}, timeScale={2}", lastDashPauseMode, mode, Time.timeScale));
            lastDashPauseMode = mode;
        }
    }

    /// <summary>Per-frame observer: scene changes, loader phases, stalls, controllers and a periodic status line.</summary>
    internal sealed class Monitor : MonoBehaviour
    {
        private static readonly FieldInfo LoaderActive = AccessTools.Field(typeof(SteamVR_LoadLevel), "_active");
        private static readonly FieldInfo LoaderAlpha = AccessTools.Field(typeof(SteamVR_LoadLevel), "alpha");
        private static readonly FieldInfo LoaderAsync = AccessTools.Field(typeof(SteamVR_LoadLevel), "async");

        private FieldInfo dashInstance, dashPauseMode, dashDelay, currentDevice;
        private PropertyInfo ovrInputFocus;
        private readonly ControllerLog controllers = new ControllerLog();

        private float lastFrame, windowStart, lastForced, nextControllerCheck;
        private int frames, renders;
        private string lastStatus, lastControllers;
        private string focus = "?";

        private SteamVR_LoadLevel tracked;
        private float loaderStart;
        private bool asyncSeen, asyncDone, resumed;

        private void Awake()
        {
            Type dash = AccessTools.TypeByName("DashPauseHandler");
            if (dash != null)
            {
                dashInstance = AccessTools.Field(dash, "instance");
                dashPauseMode = AccessTools.Field(dash, "pauseMode");
                dashDelay = AccessTools.Field(dash, "initialDelay");
            }
            Type settings = AccessTools.TypeByName("SettingsManager");
            if (settings != null) currentDevice = AccessTools.Field(settings, "currentDevice");
            Type ovrManager = AccessTools.TypeByName("OVRManager");
            if (ovrManager != null) ovrInputFocus = AccessTools.Property(ovrManager, "hasInputFocus");

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            Camera.onPostRender += OnCameraPostRender;
            lastFrame = windowStart = Time.realtimeSinceStartup;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Diag.Info(string.Format("[scene] loaded '{0}' (index {1}, {2})", scene.name, scene.buildIndex, mode));
        }

        private void OnSceneUnloaded(Scene scene)
        {
            Diag.Info(string.Format("[scene] unloaded '{0}'", scene.name));
        }

        private void OnActiveSceneChanged(Scene from, Scene to)
        {
            Diag.Info(string.Format("[scene] active '{0}' -> '{1}'", from.name, to.name));
        }

        private void OnCameraPostRender(Camera cam)
        {
            renders++;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            focus = hasFocus.ToString();
            Diag.Info("[app] focus " + hasFocus);
        }

        private void OnApplicationPause(bool paused)
        {
            Diag.Info("[app] pause " + paused);
        }

        private void Update()
        {
            Watchdog.Tick();
            float now = Time.realtimeSinceStartup;
            float gap = now - lastFrame;
            lastFrame = now;
            frames++;

            if (Plugin.LogControllerInput.Value)
            {
                controllers.Poll();
                if (now >= nextControllerCheck)
                {
                    nextControllerCheck = now + 5f;
                    string described = ControllerKinds.Describe();
                    if (described != lastControllers) Diag.Info("[input] controllers: " + described);
                    lastControllers = described;
                }
            }
            if (!Plugin.LogStatus.Value) return;

            if (gap > 1f)
                Diag.Warn(string.Format("[stall] main thread blocked {0:F1}s before frame {1} | {2}", gap, Time.frameCount, Status()));

            TrackLoader(now);

            if (now - windowStart >= 2f)
            {
                float span = now - windowStart;
                string status = Status();
                if (status != lastStatus || now - lastForced >= 10f)
                {
                    Diag.Info(string.Format("[status] fps={0:F0} renders/s={1:F0} {2}", frames / span, renders / span, status));
                    lastStatus = status;
                    lastForced = now;
                }
                windowStart = now;
                frames = 0;
                renders = 0;
            }
        }

        private void TrackLoader(float now)
        {
            // ReferenceEquals throughout: Unity's == treats a destroyed loader as null, which would hide the "finished" edge.
            SteamVR_LoadLevel active = LoaderActive.GetValue(null) as SteamVR_LoadLevel;
            if (!ReferenceEquals(active, tracked))
            {
                if (!ReferenceEquals(tracked, null))
                    Diag.Info(string.Format("[loading] SteamVR_LoadLevel finished after {0:F1}s", now - loaderStart));
                if (!ReferenceEquals(active, null))
                {
                    Diag.Info("[loading] SteamVR_LoadLevel started for '" + active.levelName + "'");
                    Watchdog.Phase = "SteamVR_LoadLevel '" + active.levelName + "'";
                    loaderStart = now;
                    asyncSeen = asyncDone = resumed = false;
                }
                else
                {
                    Watchdog.Phase = "idle";
                }
                tracked = active;
            }
            if (ReferenceEquals(active, null)) return;

            AsyncOperation op = LoaderAsync.GetValue(active) as AsyncOperation;
            if (op != null && !asyncSeen)
            {
                asyncSeen = true;
                Diag.Info(string.Format("[loading] async scene load started at +{0:F1}s", now - loaderStart));
            }
            if (op != null && op.isDone && !asyncDone)
            {
                asyncDone = true;
                Diag.Info(string.Format("[loading] async scene load done at +{0:F1}s", now - loaderStart));
            }
            if (asyncDone && !resumed && !SteamVR_Render.pauseRendering)
            {
                resumed = true;
                Diag.Info(string.Format("[loading] compositor rendering resumed at +{0:F1}s", now - loaderStart));
            }
        }

        private string Status()
        {
            var sb = new StringBuilder(320);
            sb.Append("scene=").Append(SceneManager.GetActiveScene().name);
            sb.Append(" timeScale=").Append(Time.timeScale.ToString("0.##"));
            sb.Append(" xr=").Append(XRSettings.enabled ? XRSettings.loadedDeviceName : "off");
            sb.Append(" deviceActive=").Append(XRSettings.isDeviceActive);
            sb.Append(" user=").Append(XRDevice.userPresence);
            sb.Append(" pauseRendering=").Append(SteamVR_Render.pauseRendering);
            sb.Append(" bgPrio=").Append(Application.backgroundLoadingPriority);

            SteamVR_LoadLevel loader = LoaderActive.GetValue(null) as SteamVR_LoadLevel;
            if (!ReferenceEquals(loader, null))
            {
                AsyncOperation op = LoaderAsync.GetValue(loader) as AsyncOperation;
                sb.Append(" loader='").Append(loader.levelName).Append('\'');
                sb.Append(" alpha=").Append(((float)LoaderAlpha.GetValue(loader)).ToString("0.00"));
                sb.Append(" progress=").Append(op == null ? "-" : op.progress.ToString("0.00"));
            }

            sb.Append(" focus=").Append(focus).Append(" runInBg=").Append(Application.runInBackground);
            if (ovrInputFocus != null) sb.Append(" ovrInputFocus=").Append(Safe(() => ovrInputFocus.GetValue(null, null)));
            object dash = dashInstance == null ? null : dashInstance.GetValue(null);
            if (dash != null) sb.Append(" dash=").Append(dashPauseMode.GetValue(dash)).Append('/').Append(dashDelay.GetValue(dash));
            if (currentDevice != null) sb.Append(" device=").Append(currentDevice.GetValue(null));
            return sb.ToString();
        }

        private static string Safe(Func<object> get)
        {
            try { return Convert.ToString(get()); }
            catch (Exception e) { return e.GetType().Name; }
        }
    }

    /// <summary>
    /// Logs raw OpenVR controller state: what SteamVR's legacy binding delivers before any remap.
    /// Button changes are logged immediately; stick/pad movement at most every 0.25 s per device.
    /// </summary>
    internal sealed class ControllerLog
    {
        private const float AxisStep = 0.3f;
        private const float AxisInterval = 0.25f;

        private readonly ulong[] pressed = new ulong[OpenVR.k_unMaxTrackedDeviceCount];
        private readonly ulong[] touched = new ulong[OpenVR.k_unMaxTrackedDeviceCount];
        private readonly Vector2[] axis0 = new Vector2[OpenVR.k_unMaxTrackedDeviceCount];
        private readonly Vector2[] axis2 = new Vector2[OpenVR.k_unMaxTrackedDeviceCount];
        private readonly float[] axisTime = new float[OpenVR.k_unMaxTrackedDeviceCount];
        private readonly uint size = (uint)Marshal.SizeOf(typeof(VRControllerState_t));
        private VRControllerState_t state;

        public void Poll()
        {
            CVRSystem system = OpenVR.System;
            if (system == null) return;
            float now = Time.realtimeSinceStartup;

            for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
            {
                if (system.GetTrackedDeviceClass(i) != ETrackedDeviceClass.Controller) continue;
                if (!system.GetControllerState(i, ref state, size)) continue;

                var a0 = new Vector2(state.rAxis0.x, state.rAxis0.y);
                var a2 = new Vector2(state.rAxis2.x, state.rAxis2.y);
                bool buttons = state.ulButtonPressed != pressed[i] || state.ulButtonTouched != touched[i];
                bool moved = now - axisTime[i] >= AxisInterval &&
                             (Vector2.Distance(a0, axis0[i]) >= AxisStep || Vector2.Distance(a2, axis2[i]) >= AxisStep);
                if (!buttons && !moved) continue;

                pressed[i] = state.ulButtonPressed;
                touched[i] = state.ulButtonTouched;
                axis0[i] = a0;
                axis2[i] = a2;
                axisTime[i] = now;
                Diag.Info(string.Format(
                    "[input] #{0} {1} pressed=[{2}] touched=[{3}] axis0=({4:F2},{5:F2}) axis1={6:F2} axis2=({7:F2},{8:F2}) axis3={9:F2}",
                    i, system.GetControllerRoleForTrackedDeviceIndex(i), Names(state.ulButtonPressed), Names(state.ulButtonTouched),
                    a0.x, a0.y, state.rAxis1.x, a2.x, a2.y, state.rAxis3.x));
            }
        }

        private static string Names(ulong mask)
        {
            var sb = new StringBuilder();
            for (int bit = 0; bit < 64; bit++)
            {
                if ((mask & (1UL << bit)) == 0) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(Enum.IsDefined(typeof(EVRButtonId), bit) ? ((EVRButtonId)bit).ToString() : "b" + bit);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Background thread that notices when the main thread stops finishing frames. A blocked main thread
    /// cannot log anything itself, and the user usually kills the game before it unblocks.
    /// </summary>
    internal static class Watchdog
    {
        private static long lastTick = DateTime.UtcNow.Ticks;
        public static volatile string Phase = "startup";

        public static void Tick()
        {
            Interlocked.Exchange(ref lastTick, DateTime.UtcNow.Ticks);
        }

        public static void Start()
        {
            var thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "FrankensteinPCVRFix watchdog";
            thread.Start();
        }

        private static void Run()
        {
            double nextReport = 5;
            while (true)
            {
                Thread.Sleep(1000);
                double blocked = new TimeSpan(DateTime.UtcNow.Ticks - Interlocked.Read(ref lastTick)).TotalSeconds;
                if (blocked < 5)
                {
                    nextReport = 5;
                    continue;
                }
                if (blocked >= nextReport)
                {
                    Diag.ToFile(LogLevel.Warning, string.Format(
                        "[watchdog] main thread has not finished a frame for {0:F0}s (phase: {1})", blocked, Phase));
                    nextReport = blocked + 10;
                }
            }
        }
    }

    internal static class Diag
    {
        private static readonly object Gate = new object();
        private static StreamWriter file;

        public static void Open(string path)
        {
            try
            {
                file = new StreamWriter(path, false, new UTF8Encoding(false));
                file.AutoFlush = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not open " + path + ": " + e.Message);
            }
        }

        public static void Info(string message) { Write(LogLevel.Info, message); }
        public static void Warn(string message) { Write(LogLevel.Warning, message); }
        public static void Error(string message) { Write(LogLevel.Error, message); }

        private static void Write(LogLevel level, string message)
        {
            Plugin.Log.Log(level, Stamp() + message);
            ToFile(level, message);
        }

        /// <summary>Writes to the plugin's own log file only. Safe to call from any thread.</summary>
        public static void ToFile(LogLevel level, string message)
        {
            lock (Gate)
            {
                if (file != null) file.WriteLine(Stamp() + "[" + level + "] " + message);
            }
        }

        public static string Callers(int skip)
        {
            var trace = new StackTrace(skip + 1, false);
            var sb = new StringBuilder();
            for (int i = 0; i < trace.FrameCount && i < 10; i++)
            {
                MethodBase m = trace.GetFrame(i).GetMethod();
                if (m == null) continue;
                if (sb.Length > 0) sb.Append(" <- ");
                if (m.DeclaringType != null) sb.Append(m.DeclaringType.Name).Append('.');
                sb.Append(m.Name);
            }
            return sb.ToString();
        }

        public static string Describe(Texture t)
        {
            return t == null ? "none" : string.Format("'{0}' {1}x{2}", t.name, t.width, t.height);
        }

        private static string Stamp()
        {
            return DateTime.Now.ToString("HH:mm:ss.fff") + " ";
        }
    }
}
