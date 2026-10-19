using System.Collections.Generic;
using System.Text;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using Deuteranomaly.Core;
using UnityEngine;

namespace Deuteranomaly.UI
{
    /// <summary>
    /// The settings view. Every control writes straight to <see cref="Plugin.Config"/>, which BSIPA
    /// saves; the next level start reads it again, so nothing else needs to be told.
    /// </summary>
    [ViewDefinition("Deuteranomaly.UI.SettingsViewController.bsml")]
    internal class SettingsViewController : BSMLAutomaticViewController
    {
        private static PluginConfig Config => Plugin.Config;

        [UIValue("enabled")]
        private bool Enabled
        {
            get => Config.Enabled;
            set
            {
                Config.Enabled = value;
                NotifyPropertyChanged(nameof(LastLevelText));
            }
        }

        [UIValue("type-choices")]
        private readonly List<object> _typeChoices = new List<object> { CvdType.Protan, CvdType.Deutan, CvdType.Tritan };

        [UIValue("type")]
        private object Type
        {
            get => Config.Type;
            set
            {
                Config.Type = (CvdType)value;
                NotifyPropertyChanged(nameof(LastLevelText));
            }
        }

        [UIValue("severity")]
        private float Severity
        {
            get => (float)Config.Severity;
            set
            {
                Config.Severity = System.Math.Round(value, 2);
                NotifyPropertyChanged(nameof(LastLevelText));
            }
        }

        [UIValue("threshold")]
        private float Threshold
        {
            get => (float)Config.Threshold;
            set
            {
                Config.Threshold = System.Math.Round(value, 1);
                NotifyPropertyChanged(nameof(LastLevelText));
            }
        }

        [UIValue("last-level")]
        private string LastLevelText => Describe(LevelReport.Last, Config);

        [UIAction("format-type")]
        private string FormatType(object type)
        {
            switch ((CvdType)type)
            {
                case CvdType.Protan: return "Protan (red)";
                case CvdType.Deutan: return "Deutan (green)";
                default: return "Tritan (blue)";
            }
        }

        [UIAction("format-severity")]
        private string FormatSeverity(float value)
        {
            if (value <= 0f)
            {
                return "0 (normal vision)";
            }

            return value >= 1f ? "1 (full -opia)" : value.ToString("0.00");
        }

        [UIAction("format-threshold")]
        private string FormatThreshold(float value) => value.ToString("0.0");

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);

            // A level may have been played since the screen was last open.
            NotifyPropertyChanged(nameof(LastLevelText));
        }

        private static string Describe(LevelReport report, PluginConfig config)
        {
            if (report == null)
            {
                return "No map played yet.";
            }

            var text = new StringBuilder();
            text.Append("Last map: ").Append(Escape(string.IsNullOrEmpty(report.SongName) ? report.Level : report.SongName)).Append('\n');
            text.Append("Map colors ").Append(Swatch(report.OriginalLeft)).Append(' ').Append(Swatch(report.OriginalRight))
                .Append($"  distance {report.OriginalDistance:F1}\n");

            if (report.Note != null)
            {
                text.Append("Kept: ").Append(report.Note).Append('\n');
            }
            else if (report.Changed)
            {
                text.Append("Changed to ").Append(Swatch(report.FinalLeft)).Append(' ').Append(Swatch(report.FinalRight))
                    .Append($"  distance {report.FinalDistance:F1}")
                    .Append(report.MeetsThreshold ? "" : " (best available)").Append('\n');
            }
            else
            {
                text.Append("Kept: far enough apart\n");
            }

            if (report.CustomColors != null)
            {
                text.Append("Chroma: ").Append(report.CustomColors).Append('\n');
            }

            // Lets the sliders be tuned against the last map without playing it again.
            Settings settings = config.ToSettings();
            double now = ColorAssist.SimulatedDistance(
                ColorSchemeCorrector.ToRgb(report.OriginalLeft),
                ColorSchemeCorrector.ToRgb(report.OriginalRight),
                settings.Type,
                settings.Severity);
            text.Append($"With these settings: distance {now:F1}, ")
                .Append(!config.Enabled ? "mod disabled" : now < settings.Threshold ? "would change" : "would keep");
            return text.ToString();
        }

        private static string Swatch(Color color) => $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>███</color>";

        // Song names are user data; stop them from opening rich text tags.
        private static string Escape(string s) => s.Replace("<", "<​");
    }
}
