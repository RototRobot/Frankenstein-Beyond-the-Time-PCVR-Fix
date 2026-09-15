<div align="center">

# Frankenstein: Beyond the Time PCVR Fix

**Makes the Steam build of [Frankenstein: Beyond the Time](https://store.steampowered.com/app/863380/) load in seconds and play properly on thumbstick controllers.**

![License](https://img.shields.io/badge/license-Apache--2.0-blue)
![Tested](https://img.shields.io/badge/tested-HP%20Reverb%20G2%20%2B%20SteamVR-brightgreen)
![BepInEx](https://img.shields.io/badge/BepInEx-5.4.23.5-orange)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-lightgrey)

</div>

Frankenstein: Beyond the Time is a 2018 VR adventure game. Its store page says *"SteamVR or Oculus
PC"*, and that is true It was built for **HTC Vive wands** and has signifiant issues on modern headsets. In 2018 the
developer [said](https://steamcommunity.com/app/863380/discussions/0/1727575977574512204/): Vive
and Oculus were supported, other headsets maybe later.

however as of now on anything else the game is pretty broken. Launch it and the headset shows *"Please wait"* while the monitor
sits on a frozen menu, for long enough that most people close it. Wait it out and you reach a game where
the thumbstick does not move you and the right stick does not turn you.

None of that is a crash. It is one blocking call in the level loader, one binding gap in SteamVR, one
Vive-only rule in the game's code, and turning that was only ever wired up for Oculus. This fixes all
four. No game file is modified.

> [!NOTE]
> This mod was made with heavy AI (Claude) assistance. I want to be upfront about that.

---

## Contents

- [Symptoms this fixes](#symptoms-this-fixes)
- [Download](#download)
- [Install](#install)
- [Controls](#controls)
- [What was actually wrong](#what-was-actually-wrong)
- [Build from source](#build-from-source)
- [Configuration](#configuration)
- [Known limitations](#known-limitations)
- [Uninstall](#uninstall)
- [Legal](#legal)
- [Thanks](#thanks)

---

## Symptoms this fixes

| What you see | Actual cause |
| --- | --- |
| *"Please wait"* in the headset for half a minute or more, the monitor frozen on the menu, nothing responding | SteamVR's level loader ends every load with `Shader.WarmupAllShaders()`, one blocking call: **33.3 s** before the menu |
| The thumbstick does nothing in Free Locomotion (HP Reverb G2, WMR) | The game reads a Vive touchpad on SteamVR's legacy axis 0. The G2's legacy binding puts the stick on axis 2 and nothing on axis 0 |
| With the stick fixed, movement still needs a right-hand button held | The game's modified VRTK only lets you move off Oculus while the right touchpad is touched — a two-handed Vive-wand scheme |
| The right stick does not turn you | The game's only turning (15° per stick click) is switched on for the Oculus runtime and off for SteamVR |
| The menu shows Vive wand control hints | The G2 is an unknown headset to the game, which falls back to the Vive set |

---

## Download

Two archives, same fix.

| Archive | Contains | For |
| --- | --- | --- |
| **`FrankensteinPCVRFix-1.0.0-with-BepInEx.zip`** | Plugin, docs **+ BepInEx 5.4.23.5 unmodified**, pre-laid-out | Extract into the game folder, play |
| **`FrankensteinPCVRFix-1.0.0.zip`** | Plugin and docs only | You install BepInEx yourself |

> [!TIP]
> The bundle ships all four third-party licenses (`THIRD-PARTY.md` inside it). If you would rather get
> BepInEx from its own source, take the plain archive — it contains no third-party code.

Neither archive contains a game file, and nothing in your install gets patched. The fix is a BepInEx
plugin that works on the running game in memory.

**Verified on:** HP Reverb G2 on the Oasis Driver for Windows Mixed Reality + SteamVR 2.17.9,
Windows 11, GTX 1080 Ti.
**Should also work on:** any controller with a thumbstick — Valve Index, Vive Cosmos, the original WMR
controllers, Oculus Touch through SteamVR. Untested; reports welcome.
Vive wands are left exactly as the game shipped.

---

## Install

<details open>
<summary><b>Option A — bundled archive (recommended)</b></summary>

<br>

Extract everything into the game folder:

```
Steam\steamapps\common\Frankenstein Beyond the Time\
├── Frankenstein.exe           <- already there
├── winhttp.dll                <- from the archive
├── doorstop_config.ini        <- from the archive
├── .doorstop_version          <- from the archive
└── BepInEx\
    ├── core\                  <- from the archive
    └── plugins\
        └── FrankensteinPCVRFix.dll
```

Then launch the game from Steam as usual.

</details>

<details>
<summary><b>Option B — plugin only (bring your own BepInEx)</b></summary>

<br>

**1. Install BepInEx**

Download [`BepInEx_win_x64_5.4.23.5.zip`](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5).

> [!WARNING]
> It must be the **win_x64** build. The game is 64-bit Unity 2017.3 on Mono; the x86 build will not load.

Extract it into the game folder so `winhttp.dll` sits beside `Frankenstein.exe`.

**2. Copy the plugin**

Put `FrankensteinPCVRFix.dll` into `Frankenstein Beyond the Time\BepInEx\plugins\`. Create the `plugins`
folder if it is not there yet; BepInEx also makes it the first time the game starts.

</details>

<br>

The default install path is `C:\Program Files (x86)\Steam\steamapps\common\Frankenstein Beyond the Time`.

To check it loaded, `BepInEx\LogOutput.log` should contain `Loading [Frankenstein PCVR Fix 1.0.0]`.

No SteamVR binding changes are needed. If you already made a custom binding that feeds the stick into
the trackpad, the plugin sees the busy slot and stays out of the way.

---

## Controls

On an HP Reverb G2 or any other thumbstick controller, with the fix installed. Pick the movement style
in the game's menu.

| Input | Action |
| --- | --- |
| Left stick | Move, in **Free Locomotion** |
| Right stick, left / right | Turn — 30° snaps by default, smooth turning optional |
| Trigger | Select in menus |
| Grip | Grab |
| Right stick click, held while moving | Run † |
| Stick click, held | **Teleport:** aim, release to jump. **Move in place:** hold and swing your arms † |
| Menu (≡) button | Pause † |

† Taken from the game's own button wiring, logged from the running game, rather than tried by hand.

> [!NOTE]
> The Windows button on WMR/Reverb controllers always opens the SteamVR dashboard; SteamVR reserves it
> and the game never sees it. The game's *main* pause button maps to exactly that button on SteamVR,
> which is why the menu button — the game's second pause button — is the one that works.

---

## What was actually wrong

The game is Unity 2017.3 on the legacy Mono runtime, built on VRTK 3 with the SteamVR Unity plugin 1.2
and Oculus Utilities 1.26. Its build lists three VR modes — `None`, `Oculus`, `OpenVR` — and VRTK picks
one at runtime. On a SteamVR headset it loads OpenVR, and everything below follows from the game then
taking its SteamVR branch.

### 1. The "Please wait" freeze — one blocking call

`SceneLoader.LoadNextScene` checks which VR device is loaded. On `"Oculus"` it runs the game's own
loader; on anything else it hands the load to SteamVR's stock `SteamVR_LoadLevel` component, which:

1. suspends the compositor and shows its overlay (here a 1024×512 progress bar,
   `Progress_Bar_Empty_steamVR`),
2. loads the scene asynchronously,
3. lets two frames render — which is why the **monitor** shows the new menu while the headset still says
   "Please wait",
4. calls `Shader.WarmupAllShaders()`.

Step 4 compiles every variant of every loaded shader in one call on the main thread. The plugin's
watchdog thread caught it:

```
[loading] async scene load done at +2.0s
[loading] Shader.WarmupAllShaders() begin
[watchdog] main thread has not finished a frame for 5s (phase: Shader.WarmupAllShaders())
[watchdog] main thread has not finished a frame for 15s (phase: Shader.WarmupAllShaders())
[watchdog] main thread has not finished a frame for 25s (phase: Shader.WarmupAllShaders())
[loading] Shader.WarmupAllShaders() took 33.3s
```

| Load | Scene load | Shader warmup | Total, stock | Total, fixed |
| --- | --- | --- | --- | --- |
| Start-up → menu | 1.0 s | 33.3 s | 36.8 s | **4.1 s** |
| Menu → game | 4.1 s | 21.5 s | 28.1 s | **6.8 s** |

Back in 2018, players on the Steam forum [worked out](https://steamcommunity.com/app/863380/discussions/0/2727382174633100364/)
that it was not frozen, just slow — around four minutes for some, even from a RAM disk — and the
developer called load times one of the game's major problems. They were right that it was not stuck.
It was not loading data either: the scene loads themselves took one to four seconds.

**Fix** — a Harmony transpiler swaps that call inside the loader's coroutine for a hook that skips it.
Shaders compile on first use instead. The same call in the game's Oculus loader is hooked too.

> [!NOTE]
> The loader also drops Unity's background-loading priority to `Low`, which looks like a suspect. It
> measured out as harmless — the scene loads were already the fast part. `FastSceneLoading` is there if
> your disk disagrees.

### 2. Where the stick lands — SteamVR's legacy binding

The game reads controllers through SteamVR's *legacy* input API (`SteamVR_Controller`), and VRTK takes
the touchpad from legacy **axis 0**. The Reverb G2's default legacy binding, from the Oasis driver, feeds
axis 0 from an emulated trackpad and puts the real stick on axis 2. SteamVR rejects the emulated
trackpad on this device:

```
Invalid input type trackpad::position for path (/user/hand/left/input/emulated_trackpad)
```

So axis 0 stays at zero. The plugin's raw input log, with the stick pushed forward:

```
[input] #2 RightHand pressed=[k_EButton_Axis2] ... axis0=(0.00,0.00) ... axis2=(-0.07,1.00)
```

Triggers, grips and the A button arrive where the game expects them, which is why it half-works.

**Fix** — a postfix on `SteamVR_Controller.Device.Update()` copies axis 2 into axis 0 whenever axis 0
is idle. A deflected stick counts as a touched touchpad, a clicked stick as a pressed one. Every reader
in the game — VRTK and the SteamVR InteractionSystem — goes through that one method, so they all see
the same state. A real trackpad, or a binding that already feeds axis 0, is left alone.

> [!TIP]
> The Oasis driver ships per-app legacy bindings for a few older SteamVR games — Google Earth VR,
> PAYDAY 2 — that make the same stick-to-axis-0 remap from the SteamVR side. A binding alone would not
> have been enough here, because of the next problem.

### 3. A Vive-only movement rule

The game ships a modified `VRTK_TouchpadControl`. Its `ValidPrimaryButton` decompiles to roughly:

```csharp
if (XRSettings.loadedDeviceName == "Oculus")
    return controllerEvents &&
           (primaryActivationButton == Undefined || controllerEvents.IsButtonPressed(primaryActivationButton));

return controllerEvents && controllerEventsRightHand.IsButtonPressed(rightHandButton);   // everything else
```

Logged from the running game, the one movement component is wired like this:

```
TouchpadControl '.../Controller (left)/Hand_L' primary=TouchpadTouch ... rightHand='Hand_R' rightHandButton=TouchpadTouch
```

On Oculus: touch the left stick and go. On SteamVR: steer with the left touchpad **while touching the
right one**. With a stick, that feels exactly like the stick not working.

**Fix** — for thumbstick controllers a postfix applies the game's own Oculus branch. The game already
knew how a stick should behave; it just asked the wrong question to decide.

### 4. No turning on SteamVR

The game's only turning is `HandController.ToggleRotation`: 15° per press, left hand turns left, right
hand turns right. `HandController.Start` binds it to the stick click on Oculus and to nothing anywhere
else. It cannot simply be switched back on — on SteamVR the stick click is the teleport pointer
(`activation=TouchpadPress`), and it pivots on the play-area origin rather than your head.

The rig does contain VRTK's example rotate and snap-rotate actions, but none of them has a control
script assigned (`control='none'`). They are leftovers, not a hidden option.

**Fix** — right-stick turning in the plugin: 30° snaps by default, or smooth. It pivots on the headset,
and it stands down while the game has movement disabled, while the game is paused, and for Vive wands.

### 5. The menu hints

The game asks VRTK what the headset is. VRTK does not know the G2, so the game settles on "Unknown" and
shows the Vive wand hints. The plugin shows the Oculus Touch set instead, which matches the G2's layout.

> [!CAUTION]
> The obvious shortcut — telling the game the device is Oculus — **freezes gameplay**.
> `GameController.Update` returns early whenever the device is Oculus and `OVRManager.hasInputFocus` is
> false, and on SteamVR it is always false. Only the hint objects are switched.

### What it was not

- **An Oculus lock-in.** Unlike Broken Spectre, nothing here is gated off. The game already runs on
  SteamVR; its SteamVR branch just assumed Vive hardware.
- **Revive.** The game never calls LibOVR on SteamVR, so there is nothing for Revive to translate.
- **The pause handler.** The game's `DashPauseHandler` freezes time when the Oculus runtime reports lost
  input focus, which made it an early suspect. Its start-up delay only counts down while that focus is
  reported, so on SteamVR it never arms.

---

## Build from source

> [!TIP]
> No .NET SDK required. Uses the Roslyn compiler from Visual Studio Build Tools 2022.

The plugin compiles against the game's own assemblies and BepInEx, so you need the game installed with
BepInEx 5.4.23.5 x64 in its folder.

```powershell
.\tools\Build.ps1 -Install
```

Output lands in `build\FrankensteinPCVRFix.dll`; `-Install` also copies it into `BepInEx\plugins\`. If
the game is not in the default Steam library, pass
`-GamePath "D:\SteamLibrary\steamapps\common\Frankenstein Beyond the Time"`.

> [!IMPORTANT]
> The build uses `-nostdlib+ -noconfig` and references the game's **own** `mscorlib.dll`, `System.dll`
> and `System.Core.dll`. Unity 2017.3's legacy Mono is the .NET 3.5 profile, so the plugin has to
> target mscorlib 2.0.0.0 (the built DLL's image runtime is `v2.0.50727`). The default .NET Framework
> reference set would target mscorlib 4.0, which is not what this runtime provides. That unusual
> reference list is deliberate.

To build both release archives:

```powershell
.\tools\Make-Release.ps1 -Version 1.0.0
```

They land in `dist\` (or wherever `-OutDir` points), with a `SHA256SUMS.txt`. The script refuses a
version that does not match `Plugin.cs`, or a build older than its source. BepInEx is not stored in this
repository — point `-BepInExDir` at an extracted copy of the official release; the script checks it
before using it.

```
src/FrankensteinPCVRFix/
  Plugin.cs          entry point, configuration
  Loading.cs         the shader-warmup skip
  Controls.cs        stick remap, movement rule, turning, menu hints
  Diagnostics.cs     loader tracker, watchdog, raw input log (off by default)
tools/
  Build.ps1          compile the plugin, optionally install it
  Make-Release.ps1   build both release archives
dist-files/          install notes and licence texts that go into the archives
```

---

## Configuration

`BepInEx\config\dave.frankenstein.pcvrfix.cfg`, generated on first run.

| Section | Key | Default | Purpose |
| --- | --- | --- | --- |
| Loading | `SkipShaderWarmup` | `true` | Skip the blocking shader warmup after each level load |
| Loading | `FastSceneLoading` | `false` | Load scenes at High background priority instead of the loader's Low |
| Controls | `ThumbstickAsTouchpad` | `true` | Feed the stick into the touchpad slot the game reads |
| Controls | `ThumbstickDeadzone` | `0.15` | Deflection below which the emulated touchpad counts as untouched |
| Controls | `ThumbstickLocomotion` | `true` | Thumbstick controllers use the game's Oculus movement rule |
| Controls | `ThumbstickControlHints` | `true` | Oculus Touch control hints in the menu |
| Controls | `SnapTurnDegrees` | `30` | Right-stick snap step; `0` turns right-stick turning off |
| Controls | `SmoothTurnSpeed` | `0` | Degrees per second; above `0` it replaces snap turning |
| Diagnostics | `LogStatus` | `false` | Loader phases, frozen-frame watchdog, pause state, control wiring |
| Diagnostics | `LogControllerInput` | `false` | Raw controller buttons and sticks, as SteamVR delivers them |

> [!TIP]
> Stuck somewhere? Set both diagnostics to `true`, reproduce, then attach
> `BepInEx\FrankensteinPCVRFix.log` and `BepInEx\LogOutput.log` to an issue. That log is how every
> problem above was found.

---

## Known limitations

> [!CAUTION]
> - **One headset tested.** HP Reverb G2 on the Oasis driver, from launch through the menu into the
>   start of the game. Later sequences may hide more Vive-wand assumptions.
> - **Shaders compile on first use.** Without the warmup, the first appearance of an effect can hitch
>   briefly. `SkipShaderWarmup = false` restores the stock loads, wait included.
> - **B and Y do nothing.** The G2's default legacy binding does not map them, and the game's SteamVR
>   branch has no use for them.

Vive wands are deliberately left alone: they get the game exactly as it shipped.

---

## Uninstall

Delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and the `BepInEx` folder, plus any
documentation files from the archive.

No game file is modified, so there is nothing to restore.

---

## Legal

Apache 2.0 — see [LICENSE](LICENSE) and [NOTICE](NOTICE).

This repository contains **no third-party binaries** and **no game files**. The plugin is compiled
against your own install and patches the running game in memory; nothing on disk is changed. The
optional `-with-BepInEx` archive redistributes BepInEx unmodified, with all four third-party license
texts and a `THIRD-PARTY.md` manifest.

> [!NOTE]
> **There is no ban risk.** Frankenstein: Beyond the Time is single-player with no anti-cheat, and
> `Frankenstein.exe` carries no Steam DRM wrapper. Removing the mod leaves the game exactly as installed.

Unofficial and unaffiliated. Not endorsed by TD, Valve, HP or Microsoft.

---

## Thanks

- **[BepInEx](https://github.com/BepInEx/BepInEx)** — the Unity mod loader behind all of this. Its
  Doorstop injection works unchanged on Unity 2017.3's legacy Mono.
- **[HarmonyX](https://github.com/BepInEx/HarmonyX)** — runtime patching. Every fix here is a Harmony
  prefix, postfix or transpiler.
- **[Mono.Cecil](https://github.com/jbevain/cecil)** — used to read the game's IL, including the
  modified VRTK code where the Vive-only movement rule lives.
- **[VRTK](https://github.com/ExtendRealityLtd/VRTK)** — the open-source toolkit the game is built on.
  Knowing its stock code is what made the game's changes to it stand out.
- **Valve** — for SteamVR's per-app client logs, which named the rejected emulated trackpad outright.
- **The Oasis Driver for Windows Mixed Reality** — for keeping WMR headsets like the Reverb G2 alive on
  SteamVR. Its per-app legacy bindings for other games do the same stick remap, which confirmed the
  diagnosis.
- **The players in the 2018 Steam thread** who worked out the game was not frozen, just loading for
  minutes. That pointed straight at the loader.
- **[Revive](https://github.com/LibreVR/Revive)** — considered first, as with Broken Spectre. It does not
  apply here: the game already runs natively on SteamVR and never calls LibOVR there.
- **TD**, for making the game.

Any mistakes in this mod are my own, not theirs.
