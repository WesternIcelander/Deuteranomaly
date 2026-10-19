using System;
using System.Diagnostics;
using System.Reflection;
using Deuteranomaly.Core;
using HarmonyLib;

namespace Deuteranomaly.Patches
{
    /// <summary>
    /// Corrects the level's color scheme just before <c>GameplayCoreInstaller</c> binds it.
    /// This runs after SongCore's map colors and every transition setup patch, and before
    /// Heck's postfix on the same method deserializes the map's custom data, so the Chroma
    /// colors in that data are corrected here too (<see cref="CustomColorIntegration"/>).
    /// </summary>
    [HarmonyPatch(typeof(GameplayCoreInstaller), nameof(GameplayCoreInstaller.InstallBindings))]
    internal static class GameplayCoreInstallerPatch
    {
        // public readonly, so it is written through reflection; this runs once per level load.
        private static readonly FieldInfo ColorSchemeField =
            AccessTools.Field(typeof(GameplayCoreSceneSetupData), nameof(GameplayCoreSceneSetupData.colorScheme));

        [HarmonyPrefix]
        private static void Prefix(GameplayCoreSceneSetupData ____sceneSetupData)
        {
            if (!Plugin.Config.Enabled || ____sceneSetupData == null)
            {
                return;
            }

            string level;
            Settings settings;
            try
            {
                level = ____sceneSetupData.beatmapKey.SerializedName();
                settings = SettingsFor(____sceneSetupData, level);
            }
            catch (Exception e)
            {
                Plugin.Log.Error($"Reading the settings failed, keeping the map's colors: {e}");
                return;
            }

            if (settings == null)
            {
                return;
            }

            try
            {
                Apply(____sceneSetupData, level, settings);
            }
            catch (Exception e)
            {
                Plugin.Log.Error($"Color correction failed, keeping the map's colors: {e}");
            }

            // After the scheme, whose final colors the pass pairs uncolored notes against.
            try
            {
                CustomColorIntegration.Apply(____sceneSetupData, level, settings);
            }
            catch (Exception e)
            {
                Plugin.Log.Error($"Custom color correction failed: {e}");
            }
        }

        /// <summary>The settings for this level, or null when the config excludes it.</summary>
        private static Settings SettingsFor(GameplayCoreSceneSetupData setupData, string level)
        {
            PluginConfig config = Plugin.Config;
            Settings settings = config.ToSettings();
            BeatmapKey key = setupData.beatmapKey;
            MapRule rule = MapRules.Find(
                config.PinnedMaps,
                config.ExcludedMaps,
                key.levelId,
                key.beatmapCharacteristic != null ? key.beatmapCharacteristic.serializedName : "",
                key.difficulty.SerializedName());
            if (rule == MapRule.Excluded)
            {
                Plugin.Log.Info($"{level}: excluded in the config, keeping the map's colors.");
                if (setupData.colorScheme != null && !ColorSchemeCorrector.IsCorrected(setupData.colorScheme))
                {
                    LevelReport report = Report(setupData, level, setupData.colorScheme, null, null);
                    report.OriginalDistance = report.FinalDistance = ColorAssist.SimulatedDistance(
                        ColorSchemeCorrector.ToRgb(report.OriginalLeft),
                        ColorSchemeCorrector.ToRgb(report.OriginalRight),
                        settings.Type,
                        settings.Severity);
                    report.Note = "excluded in the config";
                }

                return null;
            }

            // On a restart the scheme is already our output; the pinned threshold only matters the first time.
            ColorScheme scheme = setupData.colorScheme;
            if (rule == MapRule.Default || scheme == null || ColorSchemeCorrector.IsCorrected(scheme))
            {
                return settings;
            }

            double distance = ColorAssist.SimulatedDistance(
                ColorSchemeCorrector.ToRgb(scheme.saberAColor),
                ColorSchemeCorrector.ToRgb(scheme.saberBColor),
                settings.Type,
                settings.Severity);
            Settings pinned = MapRules.ForPinned(settings, distance, config.PinExtraDistance);
            Plugin.Log.Info($"{level}: pinned in the config, threshold {pinned.Threshold:F1}.");
            return pinned;
        }

        private static void Apply(GameplayCoreSceneSetupData setupData, string level, Settings settings)
        {
            ColorScheme scheme = setupData.colorScheme;
            if (scheme == null || ColorSchemeCorrector.IsCorrected(scheme))
            {
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            ColorScheme corrected = ColorSchemeCorrector.Correct(scheme, settings, out var result);
            stopwatch.Stop();

            Report(setupData, level, scheme, corrected, result);
            if (corrected == null)
            {
                Plugin.Log.Info(
                    $"{level}: kept {ColorSchemeCorrector.Hex(scheme.saberAColor)} / {ColorSchemeCorrector.Hex(scheme.saberBColor)}, " +
                    $"distance {result.OriginalDistance:F1} ({stopwatch.ElapsedMilliseconds} ms)");
                return;
            }

            ColorSchemeField.SetValue(setupData, corrected);
            Plugin.Log.Info(
                $"{level}: changed {ColorSchemeCorrector.Hex(scheme.saberAColor)} / {ColorSchemeCorrector.Hex(scheme.saberBColor)} " +
                $"to {ColorSchemeCorrector.Hex(corrected.saberAColor)} / {ColorSchemeCorrector.Hex(corrected.saberBColor)}, " +
                $"distance {result.OriginalDistance:F1} -> {result.FinalDistance:F1}" +
                (result.MeetsThreshold ? "" : " (below threshold, best available)") +
                $" ({stopwatch.ElapsedMilliseconds} ms)");
        }

        /// <summary>Records the level's outcome for the settings screen's read-out.</summary>
        private static LevelReport Report(GameplayCoreSceneSetupData setupData, string level, ColorScheme scheme, ColorScheme corrected, Result result)
        {
            ColorScheme final = corrected ?? scheme;
            var report = new LevelReport
            {
                Level = level,
                SongName = setupData.beatmapLevel?.songName,
                OriginalLeft = scheme.saberAColor,
                OriginalRight = scheme.saberBColor,
                FinalLeft = final.saberAColor,
                FinalRight = final.saberBColor,
                Changed = corrected != null
            };
            if (result != null)
            {
                report.OriginalDistance = result.OriginalDistance;
                report.FinalDistance = result.FinalDistance;
                report.MeetsThreshold = result.MeetsThreshold;
            }

            LevelReport.Last = report;
            return report;
        }
    }
}
