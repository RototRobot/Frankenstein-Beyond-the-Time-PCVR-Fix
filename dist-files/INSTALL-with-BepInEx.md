# Frankenstein: Beyond the Time PCVR Fix — Install (BepInEx included)

Removes the long "Please wait" freeze and makes thumbstick controllers (HP Reverb G2, WMR and other
non-Vive controllers) work in the Steam build of Frankenstein: Beyond the Time.
Verified on HP Reverb G2 + SteamVR.

**This archive already contains BepInEx.** One step, about a minute.

> Prefer to get BepInEx yourself? Use `FrankensteinPCVRFix-<version>.zip` instead — same fix,
> no third-party binaries.

---

## Copy the files in

Extract everything in this archive into your game folder, so it looks like this:

```
Steam\steamapps\common\Frankenstein Beyond the Time\
├── Frankenstein.exe           <- already there
├── winhttp.dll                <- from this archive
├── doorstop_config.ini        <- from this archive
├── .doorstop_version          <- from this archive
└── BepInEx\
    ├── core\                  <- from this archive
    └── plugins\
        └── FrankensteinPCVRFix.dll
```

> Default install path is usually
> `C:\Program Files (x86)\Steam\steamapps\common\Frankenstein Beyond the Time`

You can leave `INSTALL-with-BepInEx.md`, `LICENSE`, `NOTICE`, `THIRD-PARTY.md`, `changelog.txt` and
`THIRD-PARTY-LICENSES\` in the game folder or delete them — they are documentation only.

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

**Turning too fast or too slow** — open `BepInEx\config\frankenstein.pcvrfix.cfg` (created on first
launch) and change `SnapTurnDegrees` (default 30), or set `SmoothTurnSpeed` above 0 for smooth turning
in degrees per second.

**Anything else** — in the same file set `LogStatus = true` and `LogControllerInput = true`, reproduce,
then attach `BepInEx\FrankensteinPCVRFix.log` and `BepInEx\LogOutput.log` to an issue.

---

## Uninstall

Delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and the `BepInEx` folder, plus the
documentation files above if you kept them.

No game file is modified, so there is nothing to restore.

---

## Notes

Frankenstein: Beyond the Time is single-player with no anti-cheat, and the executable has no Steam DRM
wrapper. There is no ban risk.

This fix is Apache 2.0 — see `LICENSE` and `NOTICE`. Bundled third-party components and their
licenses are listed in `THIRD-PARTY.md`. Unofficial and unaffiliated; not endorsed by
The Dust, Valve, HP or Microsoft.
