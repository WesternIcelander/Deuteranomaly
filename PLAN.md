# Beat Saber mod: color-vision-aware saber colors

Hand-off document for continuing this project in a Claude Code session on the developer's own machine.

## Goal

A Beat Saber PC mod that keeps a map's own saber colors whenever the user can tell them apart, and only replaces them when they look too similar to the user.

- User: deuteranomaly (a milder red-green deficiency, not full deuteranopia).
- Supported types: protan, deutan, and tritan, each in the "-anomaly" (severity below 1.0) and "-opia" (severity 1.0) forms. The user picks the type in settings. Achromatopsia is out of scope: those players can use the game's own color override options.
- Scope: left and right saber colors, and block colors. In the base game the saber colors determine the block colors, but maps using Chroma can color blocks and sabers on their own, so both need handling. Obstacles and environment lights are out of scope.
- Colors that change partway through a map must be handled too, not only the colors at level start.
- The mod must work with and without the mods it integrates with (Chroma and others found in step 5). Those mods are optional, never required.
- When an override triggers, pick the closest alternative pair that passes the distance threshold, not a fixed pair.
- Mod identity: name `Deuteranomaly`, manifest ID `io.siggi.beatsaber.deuteranomaly`. The BSIPA manifest schema only requires the ID to contain no whitespace, so the reverse-domain form is valid, though most mods use a plain name like `Chroma`.
- Game: Beat Saber 1.40.8 (PC). Mods are installed with BSManager.
- Language: C#. Beat Saber is built on Unity 2022.3 with .NET Framework 4.7.2 (source: bsmg.wiki PC setup page), so the plugin targets `net472`.

## What already exists

`ColorAssistCore.cs` (originally namespace `PerMapColorAssist.Core`; since step 2 at `Deuteranomaly/Core/ColorAssistCore.cs`, namespace `Deuteranomaly.Core`, compiled, with all three deficiency types) is finished design-wise. The description below is the original deutan-only version.

- It has no Unity or Beat Saber references. Convert `UnityEngine.Color` to the `Rgb` struct (channels 0..1, sRGB) at the edges.
- `ColorAssist.Resolve(Rgb left, Rgb right, Settings s)` returns a `Result`: the chosen colors, whether they changed, whether the threshold was met, and the simulated distance before and after.
- Algorithm: simulate deuteranomaly (Machado et al. deuteranopia matrix in linear RGB, linearly interpolated from identity by `Severity`), convert to Lab, compare with CIEDE2000. If the distance is at least `Threshold`, keep the originals. Otherwise search hue-shifted and brightness-adjusted variants of both colors (plus the unchanged colors) and pick the passing pair with the smallest change from the originals under normal vision. If none passes, return the best available pair with `MeetsThreshold = false`.
- The math was checked against a JavaScript port. The CIEDE2000 results matched the published Sharma et al. reference pairs listed below. The C# itself has not been compiled or run.

Defaults that need tuning by eye, in-game: `Severity = 0.6`, `Threshold = 15`, `HueStepDeg = 10`, `ValueFactors = {1.0, 0.8, 1.2}`, `MinValue = 0.35`.

Sanity numbers from the JS port at severity 0.6: default red/blue pair about 47.8, red/green about 36.3, orange/green about 19.1. At full deuteranopia (severity 1.0) the last two drop to about 12.2 and 2.8. With `Threshold = 15` the red/green and orange/green pairs would both pass at 0.6, so the threshold and severity are the main knobs.

## Tasks

1. **Project setup.** Create the solution outside the Beat Saber folder. The BSMG wiki recommends the BeatSaberModdingTools (BSMT) templates, with `GameVersion` set to `1.40.8` in `Directory.Build.props` and the game directory stored in the `.csproj.user` file. A BSManager instance path can be used as the game directory. Debug and Release builds copy the DLL into the game's `Plugins` folder. The game folder is `D:\Software\BSManager\BSInstances\1.40.8` (see `CLAUDE.md`: ask before writing to it).
   - **Done.** The BSMT Visual Studio templates aren't installed, so the project was written by hand in the same shape: `Deuteranomaly.sln`, `Deuteranomaly/Deuteranomaly.csproj` (SDK-style, `net472`, `BeatSaberModdingTools.Tasks` 2.0.0-beta7, `Microsoft.NETFramework.ReferenceAssemblies` so it builds with only the .NET SDK), and `Deuteranomaly/Plugin.cs` (a BSIPA plugin that logs on start).
   - `GameVersion` is in `Directory.Build.props`. BSMT generates the manifest (`GenerateManifest`) from the csproj properties (`PluginId`, `PluginName`, `Authors`, `Description`, `Version`) and `DependsOn` items. Optional mods go in `LoadAfter` items later.
   - The game directory is the `GameDirectory` property (BSMT 2's name; `BeatSaberDir` is deprecated) in `Deuteranomaly/Deuteranomaly.csproj.user`, which is gitignored. On a fresh clone, create it:
     ```xml
     <Project><PropertyGroup><GameDirectory>D:\Software\BSManager\BSInstances\1.40.8</GameDirectory></PropertyGroup></Project>
     ```
   - Detected in the install: BSIPA 4.3.6, Harmony 2.10.2 (`Libs\0Harmony.dll`), BSML 1.12.5, Chroma 2.9.20, Heck 1.7.14, CustomJSONData 2.6.8, NoodleExtensions 1.7.18, Vivify 1.0.5, CustomSabersLite 1.0.3, BeatSaberPlus 6.4.4.
   - Builds copy the DLL into the game's `Plugins` folder. To build without touching the game, pass `-p:DisableCopyToGame=true` (used so far; nothing has been copied yet).
2. **Compile the core and add the other deficiency types.** Add `ColorAssistCore.cs` and fix any compile errors. Keep it free of Unity and game references. Rename its namespace from `PerMapColorAssist.Core` to `Deuteranomaly.Core` to match the mod name, and use the `Deuteranomaly` root namespace for the rest of the mod. Then generalize the simulation:
   - Add `enum CvdType { Protan, Deutan, Tritan }` and a `Settings.Type` field (default `Deutan`).
   - Replace `SimulateDeuteranomaly(c, severity)` with `Simulate(c, type, severity)`. Pass the type through `SimulatedDistance` and `BuildCandidates`. The candidate search needs no change, since it already tries hues all the way around the wheel.
   - Protan and deutan use Machado et al. (2009) severity 1.0 matrices in linear RGB, as deutan does now.
   - Tritan uses Brettel, Viénot & Mollon (1997), because single-matrix models (Machado, Viénot 1999) are inaccurate for tritan. Brettel projects onto one of two half-planes in LMS space. Using libDaltonLens's precomputed form, this becomes: in linear RGB, take the dot product of the color with a separation-plane normal; if it is `>= 0`, apply matrix 1, otherwise apply matrix 2.
   - For every type, apply severity as a linear interpolation in linear RGB between the input and the full simulation: `out = in + severity * (sim - in)`. This is equivalent to the current interpolation from the identity matrix.
   - Optional later improvement: interpolate between Machado's per-severity tables (published in steps of 0.1) instead of from identity, for protan and deutan.
   - **Done.** The core now lives at `Deuteranomaly/Core/ColorAssistCore.cs` (moved from the repo root with `git mv`), namespace `Deuteranomaly.Core`, compiled into the plugin. It compiled without changes apart from the planned ones. Public API: `CvdType`, `Settings.Type`, `ColorAssist.Simulate(Rgb, CvdType, double)`, `ColorAssist.SimulatedDistance(Rgb, Rgb, CvdType, double)`, `Resolve`, `ToLab`, `DeltaE2000`. The step 3 test project should link this file (`<Compile Include="..\Deuteranomaly\Core\ColorAssistCore.cs" />`) rather than reference the plugin, which needs the game DLLs.
   - Checked by loading the built DLL in Windows PowerShell (not yet unit tests): gray (0.2, 0.5, 0.9) stays gray at severity 1 for all three types; default Beat Saber red/blue (`#C81414` / `#288ED2`) gives 47.7 for deutan at 0.6, matching the JS port's ~47.8.
   - Sanity numbers (CIEDE2000 of simulated colors). Default red/blue: protan 48.3 (0.6) / 52.2 (1.0), deutan 47.7 / 53.6, tritan 59.4 / 66.2. Pure primaries at 0.6 / 1.0:

     | Pair | Protan | Deutan | Tritan |
     |---|---|---|---|
     | red `#FF0000` / blue `#0000FF` | 52.2 / 64.1 | 61.9 / 75.2 | 46.7 / 55.4 |
     | red / green `#00FF00` | 57.7 / 42.4 | 44.4 / 19.4 | 75.6 / 76.2 |
     | orange `#FF8000` / green | 34.1 / 22.9 | 29.4 / 10.2 | 63.1 / 65.7 |
     | blue / green | 96.1 / 84.0 | 99.5 / 85.4 | 59.7 / 41.9 |
     | yellow `#FFFF00` / violet `#8000FF` | 91.3 / 84.6 | 92.7 / 86.6 | 66.9 / 46.7 |

     The plan's earlier red/green (36.3) and orange/green (19.1) deutan figures used colors that weren't recorded, so they don't match the pure-primary rows. Note for step 3: pure blue/green and yellow/violet stay above 40 even at full tritanopia, so they aren't confusion pairs for this model with `Threshold = 15`. The tritan `Resolve` test needs a closer pair (for example two colors on a tritan confusion line, such as a blue and a blue-green of similar lightness); pick one by computing its distance first.

   Constants (row-major, linear RGB, severity 1.0), checked against the sources on 2026-10-19:

   | Model | Matrix |
   |---|---|
   | Machado protan | `0.152286, 1.052583, -0.204868 / 0.114503, 0.786281, 0.099216 / -0.003882, -0.048116, 1.051998` |
   | Machado deutan | `0.367322, 0.860646, -0.227968 / 0.280085, 0.672501, 0.047413 / -0.011820, 0.042940, 0.968881` (already in the core) |
   | Brettel tritan, plane 1 | `1.01277, 0.13548, -0.14826 / -0.01243, 0.86812, 0.14431 / 0.07589, 0.80500, 0.11911` |
   | Brettel tritan, plane 2 | `0.93678, 0.18979, -0.12657 / 0.06154, 0.81526, 0.12320 / -0.37562, 1.12767, 0.24796` |
   | Brettel tritan, separation normal | `0.03901, -0.02788, -0.01113` |

   The Brettel values come from libDaltonLens (public domain). They use sRGB primaries, the Smith & Pokorny 1975 LMS model, and RGB white as the neutral axis.
3. **Unit tests for the core** in a separate test project, referencing only the core file.
   - CIEDE2000 reference pairs (Lab1, Lab2, expected): `(50, 2.6772, -79.7751)`/`(50, 0, -82.7485)` = 2.0425; `(50, 3.1571, -77.2803)`/`(50, 0, -82.7485)` = 2.8615; `(50, 2.8361, -74.0200)`/`(50, 0, -82.7485)` = 3.4412; `(50, -1.3802, -84.2814)`/`(50, 0, -82.7485)` = 1.0000; `(50, 2.5, 0)`/`(50, 0, -2.5)` = 4.3065; `(50, 2.5, 0)`/`(73, 25, -18)` = 27.1492; `(60.2574, -34.0099, 36.2677)`/`(60.4626, -34.1751, 39.4387)` = 1.2644. Tolerance 1e-3.
   - Gray stays gray under the simulation, for every type (including both tritan half-planes).
   - At severity 0, every type returns the input unchanged.
   - `Resolve` returns the inputs unchanged when they are already distinguishable. The default red/blue pair should pass for every type.
   - `Resolve` returns a changed pair with distance at or above the threshold for a red/green input at high severity (protan and deutan), and for a tritan confusion pair such as blue/green or yellow/violet (tritan).
   - Record new sanity numbers for protan and tritan alongside the deutan ones above.
   - `Resolve` never returns colors below `MinValue` brightness when it changes them.
   - **Done.** `Deuteranomaly.Tests/Deuteranomaly.Tests.csproj` (xUnit 2.9, `net472` like the game, in the solution) links only `Deuteranomaly/Core/ColorAssistCore.cs`. Run with `dotnet test Deuteranomaly.Tests` (30 tests, all passing). Building the whole solution still needs `-p:DisableCopyToGame=true` to keep the plugin out of the game folder.
   - Pure red/green does not trigger for protan (42.4 at severity 1.0) or for deutan at 1.0 (19.4), so the tests use other confusion pairs, found by search. `MinValue` caveat: the unchanged original is always a candidate, so a side the search keeps can stay darker than `MinValue`. Only replaced colors are guaranteed to be at least `MinValue`, and that is what the test checks.
   - Confusion pairs used by the tests (simulated CIEDE2000 at severity 0.6 / 1.0, then `Resolve` at severity 1.0, threshold 15):

     | Type | Input | Distance 0.6 / 1.0 | Resolved | Final |
     |---|---|---|---|---|
     | Protan | `#A01A1A` / `#00601A` | 32.7 / 9.9 | `#A01A1A` / `#00731F` | 16.3 |
     | Deutan | `#A01A1A` / `#00601A` | 27.3 / 6.4 | `#C01F1F` / `#005918` | 15.3 |
     | Deutan | `#FF8000` / `#00FF00` | 29.3 / 10.1 | `#CC6600` / `#00FF00` | 21.2 |
     | Tritan | `#0060FF` / `#00A0A0` | 23.8 / 9.8 | `#004DCC` / `#00A0A0` | 19.8 |
     | Tritan | `#FF00FF` / `#FF8080` | 19.9 / 6.0 | `#CC00CC` / `#FF8080` | 17.1 |

     Every pair passes at severity 0.6, so with the defaults (0.6, 15) none of them would be overridden. For in-game tuning in step 9 this means `Threshold = 15` is lenient: the user may need a higher threshold or severity before anything triggers.
4. **Decompile the 1.40.8 assemblies** (dnSpy or ILSpy) to find where the active color scheme is applied when a level starts. Do not rely on remembered class or method names, because they change between versions. Candidates to look for, from older versions and unverified for 1.40.8: a `ColorScheme` class holding saber A/B colors, the transition setup that takes an override color scheme when starting a standard level, and the player's color scheme settings. Record the exact types, methods, and signatures found in this document once confirmed.
5. **Chroma, other mods, and map colors.** Many maps define their own colors, and Chroma may override them. Decide, based on the decompiled flow, where the final saber and block colors are known. The check should run on the colors that would otherwise be displayed. Decompile the installed mods (read-only) to see how they apply colors. Installed mods that may affect colors: Chroma, Heck, CustomJSONData, NoodleExtensions, Vivify, CustomSabersLite, BeatSaberPlus NoteTweaker and GameTweaker. Find every way colors can change partway through a map, for example Chroma per-note colors, animated colors on tracks, and saber color changes. Record each one here with its type, method, and signature.
6. **Hook.** Prefer the least invasive option found in steps 4 and 5: modifying the color scheme object passed into level start, or a Harmony patch (Harmony is available in the BSIPA ecosystem) on the method that applies it. Convert to `Rgb`, call `ColorAssist.Resolve`, and write the result back only when `Changed` is true.
   - **Base game first.** The base-game hook must work with no other mods installed.
   - **Optional integrations.** Do not hard-reference Chroma or the other mods. In the manifest, declare them under `loadAfter`, not `dependsOn`. At startup, check whether each one is loaded through BSIPA's plugin manager. Apply its Harmony patches only when it is present, and find its types by name through reflection, or put each integration in code that only runs when the mod is present. If a target type or method is missing (for example after the mod updates), log one warning and skip that integration instead of failing.
   - **Mid-map changes: correct at level start, not during play.** The map's custom data (parsed by CustomJSONData) is fully in memory before gameplay, so every color the map uses is known in advance as a fixed value or as keyframes. Chroma and Heck apply those colors at play time (per-note colors when a note spawns, track animations interpolated every frame), but the values come from the loaded data. Confirm this in step 5 for the installed versions.
     - Run one correction pass at level start that rewrites the color values in the in-memory map data. Chroma then shows the corrected colors, and none of our color code runs during gameplay.
     - Walk the map's timeline to find which left and right colors are on screen together, and resolve each such pair. Cache results by input pair, quantized so near-identical colors share an entry: one `Resolve` call compares about 12,000 candidate pairs, and many maps reuse the same colors.
     - Animated colors: rewrite each keyframe. The blend between two corrected keyframes is not guaranteed to pass the threshold, so there may be brief stretches mid-transition where the colors are too similar. Accept that at first. If testing shows it matters, have the start-time pass insert extra keyframes where a blend dips below the threshold.
     - Colors that can't be known from the data (for example Vivify custom shaders and materials) are left alone and logged once per map. A correction during play is a last resort, only where step 5 shows there is no other option, and even then it must cost no more than a cache lookup.
     - Keep the start-time pass fast enough not to noticeably lengthen level loading. Log how long it took.
7. **Config.** Store the deficiency type, `Severity`, `Threshold`, an enable toggle, and optional per-map pins (keyed by level hash and difficulty) and exclusions. A pin forces the override on, and an exclusion forces the map's colors to be kept.
8. **Settings UI** with BSML (BeatSaberMarkupLanguage). It must be reachable from the main menu's left-hand panel, where other mods put their settings (BSML's mod menu buttons). Confirm the right BSML API against the installed `BSML.dll`, and look at how an installed mod registers its entry there. Contents: enable toggle, deficiency type dropdown (protan, deutan, tritan), severity slider labeled generically (0 = normal vision, 1 = full "-opia"), threshold slider, and a read-out of the last map's original and final distance. Optionally add a preview of the original vs. final colors.
9. **In-game testing** with the user on 1.40.8. Use the BSIPA console to confirm the plugin loads and log each decision (map hash, original distance, whether it changed, final distance). Tune `Severity` and `Threshold` against maps where the user has trouble telling the sabers apart. The user can only judge deutan by eye. Protan and tritan rely on the unit tests and the simulation unless someone with those types tests it. Also test:
   - With Chroma and the other integrated mods installed, and with them removed (for example a separate BSManager instance with only the required mods).
   - Maps whose colors change partway through, checking that the new colors are also corrected and that frame rate is unaffected.
   - That the settings entry appears in the main menu's left-hand panel.

## Risks and notes

- Hook points break across game updates, so the mod targets 1.40.8 and must declare that version in its manifest.
- The deuteranomaly simulation is an approximation. Severity below 1.0 is interpolated, not Machado's exact per-severity tables, and individual perception varies. The user's own judgment during testing outranks the numbers. Tritan is the least validated: Brettel is the best of the common models for it, but tritan severity is still a linear interpolation, and the user can't check it by eye.
- Do not add any output that would run during gameplay on a per-frame basis. All color correction happens in one pass at level load (see step 6). Log each distinct decision once.
- Patches into optional mods break when those mods update. They must fail soft: log once, then turn that integration off.
- Write C# for .NET Framework 4.7.2: no `Math.Cbrt`, no tuple syntax in the core file.

## Sources

- [BSMG wiki: PC Mod Development Intro](https://bsmg.wiki/modding/pc/setup.html)
- [BSMG wiki: Making PC Mods](https://bsmg.wiki/modding/pc/)
- [Machado et al. 2009, CVD simulation matrices](https://www.inf.ufrgs.br/~oliveira/pubs_files/CVD_Simulation/CVD_Simulation.html)
- [libDaltonLens (Brettel 1997 precomputed parameters, public domain)](https://github.com/DaltonLens/libDaltonLens/blob/master/libDaltonLens.c)
- [DaltonLens: Understanding CVD simulation](https://daltonlens.org/understanding-cvd-simulation/)
