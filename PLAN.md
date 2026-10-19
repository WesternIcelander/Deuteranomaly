# Beat Saber mod: color-vision-aware saber colors

Hand-off document for continuing this project in a Claude Code session on the developer's own machine.

## Goal

A Beat Saber PC mod that keeps a map's own saber colors whenever the user can tell them apart, and only replaces them when they look too similar to the user.

- User: deuteranomaly (a milder red-green deficiency, not full deuteranopia).
- Scope: left and right saber colors only (which also determines block colors). Obstacles and environment lights are out of scope.
- When an override triggers, pick the closest alternative pair that passes the distance threshold, not a fixed pair.
- Game: Beat Saber 1.40.8 (PC). Mods are installed with BSManager.
- Language: C#. Beat Saber is built on Unity 2022.3 with .NET Framework 4.7.2 (source: bsmg.wiki PC setup page), so the plugin targets `net472`.

## What already exists

`ColorAssistCore.cs` (namespace `PerMapColorAssist.Core`) is finished design-wise but has never been compiled.

- It has no Unity or Beat Saber references. Convert `UnityEngine.Color` to the `Rgb` struct (channels 0..1, sRGB) at the edges.
- `ColorAssist.Resolve(Rgb left, Rgb right, Settings s)` returns a `Result`: the chosen colors, whether they changed, whether the threshold was met, and the simulated distance before and after.
- Algorithm: simulate deuteranomaly (Machado et al. deuteranopia matrix in linear RGB, linearly interpolated from identity by `Severity`), convert to Lab, compare with CIEDE2000. If the distance is at least `Threshold`, keep the originals. Otherwise search hue-shifted and brightness-adjusted variants of both colors (plus the unchanged colors) and pick the passing pair with the smallest change from the originals under normal vision. If none passes, return the best available pair with `MeetsThreshold = false`.
- The math was checked against a JavaScript port. The CIEDE2000 results matched the published Sharma et al. reference pairs listed below. The C# itself has not been compiled or run.

Defaults that need tuning by eye, in-game: `Severity = 0.6`, `Threshold = 15`, `HueStepDeg = 10`, `ValueFactors = {1.0, 0.8, 1.2}`, `MinValue = 0.35`.

Sanity numbers from the JS port at severity 0.6: default red/blue pair about 47.8, red/green about 36.3, orange/green about 19.1. At full deuteranopia (severity 1.0) the last two drop to about 12.2 and 2.8. With `Threshold = 15` the red/green and orange/green pairs would both pass at 0.6, so the threshold and severity are the main knobs.

## Tasks

1. **Project setup.** Create the solution outside the Beat Saber folder. The BSMG wiki recommends the BeatSaberModdingTools (BSMT) templates, with `GameVersion` set to `1.40.8` in `Directory.Build.props` and the game directory stored in the `.csproj.user` file. A BSManager instance path can be used as the game directory. Debug and Release builds copy the DLL into the game's `Plugins` folder. Ask the user for the game folder path.
2. **Compile the core.** Add `ColorAssistCore.cs` and fix any compile errors. Keep it free of Unity and game references.
3. **Unit tests for the core** in a separate test project, referencing only the core file.
   - CIEDE2000 reference pairs (Lab1, Lab2, expected): `(50, 2.6772, -79.7751)`/`(50, 0, -82.7485)` = 2.0425; `(50, 3.1571, -77.2803)`/`(50, 0, -82.7485)` = 2.8615; `(50, 2.8361, -74.0200)`/`(50, 0, -82.7485)` = 3.4412; `(50, -1.3802, -84.2814)`/`(50, 0, -82.7485)` = 1.0000; `(50, 2.5, 0)`/`(50, 0, -2.5)` = 4.3065; `(50, 2.5, 0)`/`(73, 25, -18)` = 27.1492; `(60.2574, -34.0099, 36.2677)`/`(60.4626, -34.1751, 39.4387)` = 1.2644. Tolerance 1e-3.
   - Gray stays gray under the simulation.
   - `Resolve` returns the inputs unchanged when they are already distinguishable.
   - `Resolve` returns a changed pair with distance at or above the threshold for a red/green input at high severity.
   - `Resolve` never returns colors below `MinValue` brightness when it changes them.
4. **Decompile the 1.40.8 assemblies** (dnSpy or ILSpy) to find where the active color scheme is applied when a level starts. Do not rely on remembered class or method names, because they change between versions. Candidates to look for, from older versions and unverified for 1.40.8: a `ColorScheme` class holding saber A/B colors, the transition setup that takes an override color scheme when starting a standard level, and the player's color scheme settings. Record the exact types, methods, and signatures found in this document once confirmed.
5. **Chroma and map colors.** Many maps define their own colors, and Chroma may override them. Decide, based on the decompiled flow, where the final saber colors are known. The check should run on the colors that would otherwise be displayed. Look at how Chroma applies its colors before choosing a hook, and make the mod load after Chroma if needed.
6. **Hook.** Prefer the least invasive option found in step 4: modifying the color scheme object passed into level start, or a Harmony patch (Harmony is available in the BSIPA ecosystem) on the method that applies it. Convert to `Rgb`, call `ColorAssist.Resolve`, and write the result back only when `Changed` is true.
7. **Config.** Store `Severity`, `Threshold`, an enable toggle, and optional per-map pins (keyed by level hash and difficulty) and exclusions. A pin forces the override on, and an exclusion forces the map's colors to be kept.
8. **Settings UI** with BSML (BeatSaberMarkupLanguage): enable toggle, severity and threshold sliders, and a read-out of the last map's original and final distance. Optionally add a preview of the original vs. final colors.
9. **In-game testing** with the user on 1.40.8. Use the BSIPA console to confirm the plugin loads and log each decision (map hash, original distance, whether it changed, final distance). Tune `Severity` and `Threshold` against maps where the user has trouble telling the sabers apart.

## Risks and notes

- Hook points break across game updates, so the mod targets 1.40.8 and must declare that version in its manifest.
- The deuteranomaly simulation is an approximation. Severity below 1.0 is interpolated, not Machado's exact per-severity tables, and individual perception varies. The user's own judgment during testing outranks the numbers.
- Do not add any output that would run during gameplay on a per-frame basis. The check should run once per level load.
- Write C# for .NET Framework 4.7.2: no `Math.Cbrt`, no tuple syntax in the core file.

## Sources

- [BSMG wiki: PC Mod Development Intro](https://bsmg.wiki/modding/pc/setup.html)
- [BSMG wiki: Making PC Mods](https://bsmg.wiki/modding/pc/)
