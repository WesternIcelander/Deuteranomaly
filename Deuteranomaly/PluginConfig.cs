using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Deuteranomaly.Core;
using IPA.Config.Stores;
using IPA.Config.Stores.Attributes;
using IPA.Config.Stores.Converters;

[assembly: InternalsVisibleTo(GeneratedStore.AssemblyVisibilityTarget)]

namespace Deuteranomaly
{
    /// <summary>
    /// Settings stored by BSIPA in <c>UserData\Deuteranomaly.json</c>. Properties must stay virtual
    /// so BSIPA's generated subclass can track and save changes.
    /// </summary>
    internal class PluginConfig
    {
        public virtual bool Enabled { get; set; } = true;

        [UseConverter(typeof(EnumConverter<CvdType>))]
        public virtual CvdType Type { get; set; } = CvdType.Deutan;

        /// <summary>0 = normal vision, 1 = full protanopia, deuteranopia or tritanopia.</summary>
        public virtual double Severity { get; set; } = 0.6;

        /// <summary>Minimum CIEDE2000 distance between the two simulated colors.</summary>
        public virtual double Threshold { get; set; } = 15.0;

        /// <summary>On pinned maps, how far apart (CIEDE2000) the corrected colors must be beyond the original distance.</summary>
        public virtual double PinExtraDistance { get; set; } = 10.0;

        /// <summary>Maps whose colors are always changed. Entries: <c>HASH/Characteristic/Difficulty</c> or <c>HASH</c>.</summary>
        [NonNullable]
        [UseConverter(typeof(ListConverter<string>))]
        public virtual List<string> PinnedMaps { get; set; } = new List<string>();

        /// <summary>Maps whose colors are always kept. Same entry format as <see cref="PinnedMaps"/>; wins over a pin.</summary>
        [NonNullable]
        [UseConverter(typeof(ListConverter<string>))]
        public virtual List<string> ExcludedMaps { get; set; } = new List<string>();

        /// <summary>The core settings for these values, with out-of-range values clamped.</summary>
        public Settings ToSettings()
        {
            return new Settings
            {
                Type = Enum.IsDefined(typeof(CvdType), Type) ? Type : CvdType.Deutan,
                Severity = Clamp(Severity, 0.0, 1.0),
                Threshold = Clamp(Threshold, 0.0, 100.0)
            };
        }

        private static double Clamp(double v, double min, double max)
        {
            return double.IsNaN(v) ? min : v < min ? min : v > max ? max : v;
        }
    }
}
