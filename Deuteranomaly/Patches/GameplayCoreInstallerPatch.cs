using System;
using System.Diagnostics;
using System.Reflection;
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
            try
            {
                Apply(____sceneSetupData);
            }
            catch (Exception e)
            {
                Plugin.Log.Error($"Color correction failed, keeping the map's colors: {e}");
            }

            // After the scheme, whose final colors the pass pairs uncolored notes against.
            try
            {
                if (Plugin.Enabled && ____sceneSetupData != null)
                {
                    CustomColorIntegration.Apply(____sceneSetupData, ____sceneSetupData.beatmapKey.SerializedName());
                }
            }
            catch (Exception e)
            {
                Plugin.Log.Error($"Custom color correction failed: {e}");
            }
        }

        private static void Apply(GameplayCoreSceneSetupData setupData)
        {
            if (!Plugin.Enabled || setupData == null)
            {
                return;
            }

            ColorScheme scheme = setupData.colorScheme;
            if (scheme == null || ColorSchemeCorrector.IsCorrected(scheme))
            {
                return;
            }

            string level = setupData.beatmapKey.SerializedName();
            var stopwatch = Stopwatch.StartNew();
            ColorScheme corrected = ColorSchemeCorrector.Correct(scheme, Plugin.Settings, out var result);
            stopwatch.Stop();

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
    }
}
