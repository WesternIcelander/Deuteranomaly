# Deuteranomaly

A Beat Saber PC mod for players with a color vision deficiency. It keeps each map's own saber and block colors whenever you can tell the two sides apart, and changes them only when they would look too similar to you.

Most maps look fine as the mapper made them, so the mod doesn't swap every map to a fixed "safe" pair. It checks each map at level start, and when a pair is too close it picks the nearest alternative pair that you can tell apart.

- Game version: Beat Saber 1.40.8 (PC)
- Mod version: 0.1.0, not yet tested in the game

## How it works

1. When a level loads, the mod takes the left and right colors that the game would show.
2. It simulates how they look with your type and severity of color vision deficiency. Protan and deutan use the Machado et al. (2009) model, tritan uses Brettel, Viénot & Mollon (1997). Severities below 1 are interpolated toward normal vision.
3. It measures how far apart the two simulated colors are (CIEDE2000, in Lab).
4. If the distance is at or above your threshold, the colors are kept. Otherwise it tries hue-shifted and brightness-adjusted variants of both colors and uses the pair that passes with the smallest visible change for someone with normal vision.

All of this runs once at level load. Nothing runs per frame during gameplay.

### Supported color vision types

| Type | Severity below 1 | Severity 1 |
|---|---|---|
| Protan (red) | protanomaly | protanopia |
| Deutan (green) | deuteranomaly | deuteranopia |
| Tritan (blue) | tritanomaly | tritanopia |

Achromatopsia isn't supported. The game's own color override options already cover it.

### What gets corrected

- The level's color scheme: left and right saber colors, which also color the notes, arcs, chains and saber effects in the base game.
- With **Chroma** (and CustomJSONData) installed: custom note, arc and chain colors, animated color keyframes, and track color animations, including colors that change partway through the map. These are rewritten in the loaded map data before play starts, so Chroma shows the corrected colors with no extra work during gameplay. Each color is checked against the other side's color closest in time.

Left alone:

- Walls, bombs and environment lights.
- One-saber levels.
- Colors that can't be known before play, such as Vivify custom shaders and materials, color keyframes with modifiers or `base…` providers, and track animations on tracks that hold notes of both colors. These are counted and logged once per map.
- Multiplayer: other players' colors.

Animated colors are corrected keyframe by keyframe, so the blend between two corrected keyframes can briefly dip below the threshold.

## Requirements

- BSIPA 4.3 or later
- BeatSaberMarkupLanguage (BSML) 1.12.5 or later

Optional: SongCore, CustomJSONData, Heck and Chroma. The mod loads after them when they're installed and works without them. Chroma custom colors are only corrected when both CustomJSONData and Chroma are present.

## Settings

Open **Deuteranomaly** from the mod buttons in the main menu's left-hand panel.

- **Enabled**: turn the mod on or off.
- **Color vision type**: protan, deutan or tritan.
- **Severity**: 0 is normal vision, 1 is full protanopia, deuteranopia or tritanopia. Default 0.6.
- **Threshold**: how far apart (CIEDE2000) the two colors must look. Higher values change more maps. Default 15.

The screen also shows the last level you played: its original and final colors, their distances, and whether it would change with the current settings, so you can tune the sliders against a map you had trouble with.

Settings are saved in `UserData\Deuteranomaly.json`. That file also holds per-map rules that have no UI yet:

- `PinnedMaps`: maps whose colors are always changed. The colors move at least `PinExtraDistance` (default 10) further apart than the map's own colors.
- `ExcludedMaps`: maps whose colors are always kept. An exclusion wins over a pin.

Entries are either `HASH` (every difficulty) or `HASH/Characteristic/Difficulty`, for example `ABC123.../Standard/ExpertPlus`. `HASH` is the level ID without the `custom_level_` prefix, and matching ignores case.

The BSIPA log has one line per level with the decision, the distances, and for Chroma maps how many custom colors were checked and changed.

## Building

Requirements: the .NET SDK and a Beat Saber 1.40.8 install with BSIPA and BSML.

1. Create `Deuteranomaly/Deuteranomaly.csproj.user` (gitignored) that points at the game:

   ```xml
   <Project><PropertyGroup><GameDirectory>C:\Path\To\Beat Saber</GameDirectory></PropertyGroup></Project>
   ```

2. Build:

   ```sh
   dotnet build -c Release
   ```

   This copies `Deuteranomaly.dll` into the game's `Plugins` folder. To build without touching the game, add `-p:DisableCopyToGame=true`.

3. Run the tests for the color core and the map-data pass:

   ```sh
   dotnet test Deuteranomaly.Tests
   ```

If you change the `DependsOn` or `LoadAfter` items in the csproj, delete `Deuteranomaly/obj/<Config>/net472/manifest.json` or do a clean build, because the manifest isn't regenerated otherwise.

## Project layout

- `Deuteranomaly/Core/`: game-independent code, also compiled into the tests. `ColorAssistCore.cs` has the simulation, CIEDE2000 and the search for a replacement pair. `CustomColorPass.cs` rewrites Chroma colors, and `MapRules.cs` handles pins and exclusions.
- `Deuteranomaly/Patches/GameplayCoreInstallerPatch.cs`: the Harmony patch that runs at level start.
- `Deuteranomaly/ColorSchemeCorrector.cs`: corrects the level's color scheme.
- `Deuteranomaly/CustomColorIntegration.cs`: the optional Chroma and CustomJSONData integration, found by reflection.
- `Deuteranomaly/UI/`: the BSML settings screen.
- `Deuteranomaly.Tests/`: xUnit tests.
- `PLAN.md`: design notes, decompiled hook points, and the task list.

## Caveats

- The simulation is an approximation, and color perception varies from person to person. If the numbers say a pair is fine but you can't tell the colors apart, raise the threshold or severity, or pin the map.
- Tritan is the least validated type.
- Hook points change between game versions, so this build targets 1.40.8 only.
