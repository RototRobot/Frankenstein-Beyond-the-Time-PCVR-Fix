// Controller fixes for the game's SteamVR (non-Oculus) path.
//
// The game reads controllers through SteamVR's legacy input (SteamVR_Controller) and its SteamVR controls were built
// for Vive wands: movement, teleport and menus use the touchpad on legacy axis 0. Three things break thumbstick
// controllers:
//
//  1. Where the stick lands. The Oasis driver's legacy binding for the HP Reverb G2 feeds axis 0 from an
//     "emulated_trackpad" that SteamVR rejects ("Invalid input type trackpad::position"), and puts the real stick
//     on axis 2. Axis 0 stays at (0,0), so the game sees no touchpad at all.
//
//  2. The game's own rule. Its modified VRTK_TouchpadControl.ValidPrimaryButton allows smooth movement on Oculus
//     while the stick is touched, but on anything else only while a right-hand button is held - a Vive-wand scheme.
//
//  3. No turning. The only turn the game has, HandController.ToggleRotation, is switched off off Oculus.

using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;
using Valve.VR;
using VRTK;

namespace FrankensteinPCVRFix
{
    internal static class ControlPatches
    {
        private const ulong TouchpadBit = 1UL << 32;   // k_EButton_SteamVR_Touchpad: legacy axis 0
        private const ulong StickBit = 1UL << 34;      // k_EButton_Axis2: where the G2 stick lands

        private static FieldInfo objectControlEvents;  // VRTK_ObjectControl.controllerEvents
        private static FieldInfo currentDevice;        // SettingsManager.currentDevice (0 HTC, 1 Oculus, 2 Unknown)
        private static bool remapLogged, locomotionLogged;

        public static void Apply(Harmony harmony)
        {
            Type self = typeof(ControlPatches);
            Type settings = AccessTools.TypeByName("SettingsManager");
            objectControlEvents = AccessTools.Field(typeof(VRTK_ObjectControl), "controllerEvents");
            currentDevice = settings == null ? null : AccessTools.Field(settings, "currentDevice");

            PatchUtil.Method(harmony, self, typeof(SteamVR_Controller.Device), "Update", null, postfix: "DeviceUpdatePostfix");
            PatchUtil.Method(harmony, self, typeof(VRTK_TouchpadControl), "ValidPrimaryButton", null, postfix: "ValidPrimaryButtonPostfix");
            PatchUtil.Method(harmony, self, AccessTools.TypeByName("MenuController"), "RefreshTutorialBoard", null, postfix: "RefreshTutorialBoardPostfix");
            PatchUtil.Method(harmony, self, typeof(SteamVR_Events), "System", new[] { typeof(EVREventType) }, prefix: "DeviceEventPrefix");
            if (Plugin.LogStatus.Value)
                PatchUtil.Method(harmony, self, settings, "ApplyControlScheme", null, postfix: "ApplyControlSchemePostfix");
        }

        // SteamVR_Render forwards OpenVR events through SteamVR_Events.System. A controller switching on, off or
        // hands invalidates the cached controller types.
        private static void DeviceEventPrefix(EVREventType __0)
        {
            if (__0 == EVREventType.VREvent_TrackedDeviceActivated || __0 == EVREventType.VREvent_TrackedDeviceDeactivated ||
                __0 == EVREventType.VREvent_TrackedDeviceRoleChanged)
                ControllerKinds.Reset();
        }

        // Thumbstick -> touchpad. Runs right after SteamVR_Controller.Device refreshes its per-frame state, so every
        // reader (VRTK, the SteamVR InteractionSystem) sees the same remapped state, press/touch edges included.
        private static void DeviceUpdatePostfix(SteamVR_Controller.Device __instance, ref VRControllerState_t ___state)
        {
            if (!Plugin.ThumbstickAsTouchpad.Value) return;

            // Leave axis 0 alone whenever something already drives it: a real trackpad, a binding that feeds it,
            // or this remap on an earlier call in the same frame.
            if (((___state.ulButtonPressed | ___state.ulButtonTouched) & TouchpadBit) != 0) return;
            if (___state.rAxis0.x != 0f || ___state.rAxis0.y != 0f) return;

            float x = ___state.rAxis2.x;
            float y = ___state.rAxis2.y;
            float deadzone = Plugin.ThumbstickDeadzone.Value;
            bool clicked = (___state.ulButtonPressed & StickBit) != 0;
            if (!clicked && x * x + y * y < deadzone * deadzone) return;
            if (ControllerKinds.IsTouchpadOnly(__instance.index)) return;

            // A deflected stick is a touched touchpad; a clicked stick is a pressed one.
            ___state.rAxis0.x = x;
            ___state.rAxis0.y = y;
            ___state.ulButtonTouched |= TouchpadBit;
            if (clicked) ___state.ulButtonPressed |= TouchpadBit;

            if (!remapLogged)
            {
                remapLogged = true;
                Diag.Info(string.Format("[controls] thumbstick -> touchpad remap active (device #{0} '{1}')",
                    __instance.index, ControllerKinds.TypeOf(__instance.index)));
            }
        }

        // For thumbstick controllers apply the game's Oculus rule (the configured activation button, i.e. touching
        // the stick) instead of its Vive rule (a right-hand button held). Vive wands keep the original behaviour.
        private static void ValidPrimaryButtonPostfix(VRTK_TouchpadControl __instance, ref bool __result)
        {
            if (__result || !Plugin.ThumbstickLocomotion.Value) return;
            if (XRSettings.loadedDeviceName == "Oculus" || !ControllerKinds.AnyThumbstick()) return;

            var events = objectControlEvents == null ? null : objectControlEvents.GetValue(__instance) as VRTK_ControllerEvents;
            if (events == null) return;

            VRTK_ControllerEvents.ButtonAlias alias = __instance.primaryActivationButton;
            __result = alias == VRTK_ControllerEvents.ButtonAlias.Undefined || events.IsButtonPressed(alias);

            if (__result && !locomotionLogged)
            {
                locomotionLogged = true;
                Diag.Info(string.Format("[controls] thumbstick locomotion: '{0}' moving on {1} (Oculus rule)",
                    __instance.name, alias));
            }
        }

        // With the headset Unknown to the game, the menu falls through to the Vive wand control hints. A thumbstick
        // controller is laid out like Oculus Touch, so show those. Only the hint objects change: currentDevice itself
        // must stay as it is, because GameController.Update stops early for Oculus without OVR input focus.
        private static void RefreshTutorialBoardPostfix(MonoBehaviour __instance)
        {
            if (!Plugin.ThumbstickControlHints.Value || currentDevice == null) return;
            if (Convert.ToInt32(currentDevice.GetValue(null)) == 1 || !ControllerKinds.AnyThumbstick()) return;

            Traverse menu = Traverse.Create(__instance);
            int swapped = ShowInstead(menu, "objectTutorialOculus", "objectTutorialHTC")
                        + ShowInstead(menu, "imageControlTeleportOculus", "imageControlTeleportHTC")
                        + ShowInstead(menu, "imageControlMoveInPlaceOculus", "imageControlMoveInPlaceHTC")
                        + ShowInstead(menu, "imageControlFreeLocomotionOculus", "imageControlFreeLocomotionHTC");
            Diag.Info("[controls] menu control hints switched to the Oculus Touch set (" + swapped + " of 4)");
        }

        private static int ShowInstead(Traverse menu, string show, string hide)
        {
            GameObject shown = menu.Field(show).GetValue<GameObject>();
            GameObject hidden = menu.Field(hide).GetValue<GameObject>();
            if (shown == null || hidden == null) return 0;
            hidden.SetActive(false);
            shown.SetActive(true);
            return 1;
        }

        // Diagnostics: which components the chosen control scheme enabled, and which buttons drive them.
        private static void ApplyControlSchemePostfix(GameObject __0, bool __1)
        {
            if (__0 == null) return;

            object scheme = Traverse.Create(AccessTools.TypeByName("SettingsManager")).Property("controlScheme").GetValue();
            Diag.Info(string.Format("[controls] ApplyControlScheme on '{0}': scheme={1} movement={2} {3}",
                __0.name, scheme, __1, ControllerKinds.Describe()));

            foreach (VRTK_TouchpadControl c in __0.GetComponentsInChildren<VRTK_TouchpadControl>(true))
            {
                Traverse t = Traverse.Create(c);
                Diag.Info(string.Format(
                    "[controls]   TouchpadControl '{0}' enabled={1} controller='{2}' primary={3} modifier={4} deadzone={5} " +
                    "rightHand='{6}' rightHandButton={7}",
                    PathOf(c.transform), c.enabled, NameOf(objectControlEvents == null ? null : objectControlEvents.GetValue(c)),
                    t.Field("primaryActivationButton").GetValue(), t.Field("actionModifierButton").GetValue(),
                    t.Field("axisDeadzone").GetValue(), NameOf(t.Field("rightHand").GetValue()), t.Field("rightHandButton").GetValue()));
            }
            foreach (VRTK_BaseObjectControlAction a in __0.GetComponentsInChildren<VRTK_BaseObjectControlAction>(true))
            {
                Traverse t = Traverse.Create(a);
                Diag.Info(string.Format(
                    "[controls]   {0} '{1}' enabled={2} axis={3} control='{4}' speed={5} run={6} runButton={7} runButtonOculus={8} runHand='{9}'",
                    a.GetType().Name, PathOf(a.transform), a.enabled, t.Field("listenOnAxisChange").GetValue(),
                    NameOf(t.Field("objectControlScript").GetValue()), t.Field("maximumSpeed").GetValue(),
                    t.Field("maximumSpeedRun").GetValue(), t.Field("rightHandButton").GetValue(),
                    t.Field("rightHandButtonOculus").GetValue(), NameOf(t.Field("controllerEventsRightHand").GetValue())));
            }
            foreach (VRTK_Pointer p in __0.GetComponentsInChildren<VRTK_Pointer>(true))
            {
                Traverse t = Traverse.Create(p);
                Diag.Info(string.Format("[controls]   Pointer '{0}' enabled={1} activation={2} hold={3} selection={4} selectOnPress={5}",
                    PathOf(p.transform), p.enabled, t.Field("activationButton").GetValue(), t.Field("holdButtonToActivate").GetValue(),
                    t.Field("selectionButton").GetValue(), t.Field("selectOnPress").GetValue()));
            }
            foreach (VRTK_MoveInPlace m in __0.GetComponentsInChildren<VRTK_MoveInPlace>(true))
            {
                Traverse t = Traverse.Create(m);
                Diag.Info(string.Format("[controls]   MoveInPlace '{0}' enabled={1} engage={2} controlOptions={3}",
                    PathOf(m.transform), m.enabled, t.Field("engageButton").GetValue(), t.Field("controlOptions").GetValue()));
            }
        }

        private static string NameOf(object o)
        {
            var component = o as Component;
            if (component != null) return component.name;
            var go = o as GameObject;
            return go != null ? go.name : "none";
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null && path.Length < 120; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }

    /// <summary>
    /// Right-stick turning. The game's SteamVR path has none: its only turn, HandController.ToggleRotation
    /// (15 degrees per stick click, left hand turns left, right hand right), is switched off by HandController.Start
    /// unless the runtime is Oculus - and on SteamVR the stick click is already taken by the teleport pointer.
    /// </summary>
    internal sealed class StickTurn : MonoBehaviour
    {
        private const float Engage = 0.7f;   // sideways deflection that fires a snap turn
        private const float Rearm = 0.35f;   // deflection to fall back under before the next one

        private FieldInfo movementEnabled;   // SettingsManager.movementEnabled: false while the game holds the player
        private bool armed = true;
        private bool logged;

        private void Awake()
        {
            Type settings = AccessTools.TypeByName("SettingsManager");
            if (settings != null) movementEnabled = AccessTools.Field(settings, "movementEnabled");
        }

        private void Update()
        {
            float snap = Plugin.SnapTurnDegrees.Value;
            float smooth = Plugin.SmoothTurnSpeed.Value;
            if (snap <= 0f && smooth <= 0f) return;
            if (Time.timeScale <= 0f) return;
            if (movementEnabled != null && !(bool)movementEnabled.GetValue(null)) return;

            CVRSystem system = OpenVR.System;
            if (system == null) return;
            uint right = system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
            if (right >= OpenVR.k_unMaxTrackedDeviceCount) return;
            string type = ControllerKinds.TypeOf(right);
            if (type.Length == 0 || type == "vive_controller") return;

            Transform playArea = VRTK_DeviceFinder.PlayAreaTransform();
            Transform headset = VRTK_DeviceFinder.HeadsetTransform();
            if (playArea == null || headset == null) return;

            // Read through SteamVR_Controller so the thumbstick remap applies: the stick is on axis 0 either way.
            Vector2 stick = SteamVR_Controller.Input((int)right).GetAxis(EVRButtonId.k_EButton_SteamVR_Touchpad);
            float x = Mathf.Abs(stick.x) > Mathf.Abs(stick.y) ? stick.x : 0f;

            float angle = 0f;
            if (smooth > 0f)
            {
                if (Mathf.Abs(x) > Plugin.ThumbstickDeadzone.Value) angle = x * smooth * Time.deltaTime;
            }
            else if (armed && Mathf.Abs(x) >= Engage)
            {
                angle = Mathf.Sign(x) * snap;
                armed = false;
            }
            if (Mathf.Abs(x) < Rearm) armed = true;
            if (angle == 0f) return;

            // Pivot on the head, not the play-area origin, so the view turns in place.
            playArea.RotateAround(headset.position, Vector3.up, angle);

            if (!logged)
            {
                logged = true;
                Diag.Info(smooth > 0f
                    ? string.Format("[controls] right-stick smooth turn active ({0} deg/s)", smooth)
                    : string.Format("[controls] right-stick snap turn active ({0} deg per step)", snap));
            }
        }
    }

    /// <summary>What physical controller sits behind each OpenVR device index.</summary>
    internal static class ControllerKinds
    {
        private const ETrackedDeviceProperty ControllerTypeProperty = (ETrackedDeviceProperty)7000;   // Prop_ControllerType_String
        private static readonly string[] Types = new string[OpenVR.k_unMaxTrackedDeviceCount];
        private static readonly float[] RetryAt = new float[OpenVR.k_unMaxTrackedDeviceCount];

        public static void Reset()
        {
            Array.Clear(Types, 0, Types.Length);
            Array.Clear(RetryAt, 0, RetryAt.Length);
        }

        public static string TypeOf(uint index)
        {
            if (index >= OpenVR.k_unMaxTrackedDeviceCount) return "";
            string cached = Types[index];
            if (cached != null && (cached.Length > 0 || Time.realtimeSinceStartup < RetryAt[index])) return cached;

            // An empty answer is cached briefly so a not-yet-ready device is re-asked, but not every frame.
            Types[index] = "";
            RetryAt[index] = Time.realtimeSinceStartup + 2f;
            CVRSystem system = OpenVR.System;
            if (system == null) return "";
            var value = new StringBuilder(64);
            ETrackedPropertyError error = ETrackedPropertyError.TrackedProp_Success;
            system.GetStringTrackedDeviceProperty(index, ControllerTypeProperty, value, (uint)value.Capacity, ref error);
            if (error != ETrackedPropertyError.TrackedProp_Success || value.Length == 0) return "";
            return Types[index] = value.ToString();
        }

        /// <summary>A Vive wand: touchpad only, which is what the game was built for.</summary>
        public static bool IsTouchpadOnly(uint index)
        {
            return TypeOf(index) == "vive_controller";
        }

        public static bool AnyThumbstick()
        {
            CVRSystem system = OpenVR.System;
            if (system == null) return false;
            return IsThumbstick(system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand))
                || IsThumbstick(system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand));
        }

        public static string Describe()
        {
            CVRSystem system = OpenVR.System;
            if (system == null) return "controllers=none";
            uint left = system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand);
            uint right = system.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
            return string.Format("left=#{0} '{1}' right=#{2} '{3}' thumbstick={4}",
                left, TypeOf(left), right, TypeOf(right), AnyThumbstick());
        }

        private static bool IsThumbstick(uint index)
        {
            string type = TypeOf(index);
            return type.Length > 0 && type != "vive_controller";
        }
    }
}
