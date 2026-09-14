# Frankenstein: Beyond the Time PCVR Fix — Install

Removes the long "Please wait" freeze and makes thumbstick controllers (HP Reverb G2, WMR and other
non-Vive controllers) work in the Steam build of Frankenstein: Beyond the Time.
Verified on HP Reverb G2 + SteamVR. Full write-up: see the project page.

Two steps, about two minutes.

---

## 1. Install BepInEx

Download **`BepInEx_win_x64_5.4.23.5.zip`**:

  https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5

It must be the **win_x64** build — the game is 64-bit (Unity 2017.3 on Mono).

Extract it into the game folder so `winhttp.dll` sits next to `Frankenstein.exe`:

```
Steam\steamapps\common\Frankenstein Beyond the Time\
├── Frankenstein.exe
├── winhttp.dll            <- from BepInEx
├── doorstop_config.ini    <- from BepInEx
└── BepInEx\               <- from BepInEx
```

> Default install path is usually
> `C:\Program Files (x86)\Steam\steamapps\common\Frankenstein Beyond the Time`

---

## 2. Install the plugin

Copy **`FrankensteinPCVRFix.dll`** into `BepInEx\plugins\`. Create the `plugins` folder if it is not
there yet (BepInEx also creates it the first time the game starts).

```
Steam\steamapps\common\Frankenstein Beyond the Time\BepInEx\plugins\FrankensteinPCVRFix.dll
```

Then launch the game from Steam as usual.

---

## Playing

- The "Please wait" screen should now last a few seconds.
- Choose your movement style under **Controls scheme** in the game's menu: teleport, move in place, or
  **Free Locomotion** for stick movement.
- **Left stick** = move, **right stick** = turn (30° snaps), **trigger** = select in menus, **grip** = grab,
  **menu (≡) button** = pause. In Teleport mode, hold a stick click to aim and release to jump.

The Windows button on WMR/Reverb controllers always opens the SteamVR dashboard — that is SteamVR
reserving it, and the game cannot see it.

---

## If something goes wrong

**Still a long "Please wait", or the stick does nothing** — the plugin is not loading. Check that
`BepInEx\LogOutput.log` contains `Loading [Frankenstein PCVR Fix 1.0.0]`. If there is no
`LogOutput.log` at all, `winhttp.dll` is not next to `Frankenstein.exe`.

**Turning too fast or too slow** — open `BepInEx\config\dave.frankenstein.pcvrfix.cfg` (created on first
launch) and change `SnapTurnDegrees` (default 30), or set `SmoothTurnSpeed` above 0 for smooth turning
in degrees per second.

**Anything else** — in the same file set `LogStatus = true` and `LogControllerInput = true`, reproduce,
then attach `BepInEx\FrankensteinPCVRFix.log` and `BepInEx\LogOutput.log` to an issue.

---

## Uninstall

Delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and the `BepInEx` folder.

No game file is modified, so there is nothing to restore.

---

## Notes

Frankenstein: Beyond the Time is single-player with no anti-cheat, and the executable has no Steam DRM
wrapper. There is no ban risk.

Apache 2.0 — see LICENSE and NOTICE. Unofficial and unaffiliated; not endorsed by
The Dust, Valve, HP or Microsoft.
