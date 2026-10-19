using System.Runtime.CompilerServices;
using Deuteranomaly.Core;
using UnityEngine;

namespace Deuteranomaly
{
    /// <summary>
    /// Builds a corrected copy of a level's <see cref="ColorScheme"/> when its saber colors
    /// look too similar under the configured color vision deficiency.
    /// </summary>
    internal static class ColorSchemeCorrector
    {
        // Schemes this class created. A restart reuses the scene setup data, so the scheme
        // seen then is our own output and must not be corrected again.
        private static readonly ConditionalWeakTable<ColorScheme, object> Produced =
            new ConditionalWeakTable<ColorScheme, object>();

        internal static bool IsCorrected(ColorScheme scheme) => Produced.TryGetValue(scheme, out _);

        /// <summary>
        /// Returns a new scheme with corrected saber colors, or null when the original is kept.
        /// Never modifies <paramref name="scheme"/>, which may be shared (the player's saved scheme).
        /// </summary>
        internal static ColorScheme Correct(ColorScheme scheme, Settings settings, out Result result)
        {
            Color left = scheme.saberAColor;
            Color right = scheme.saberBColor;
            result = ColorAssist.Resolve(ToRgb(left), ToRgb(right), settings);
            if (!result.Changed)
            {
                return null;
            }

            var corrected = new ColorScheme(
                scheme,
                scheme.overrideNotes,
                ToColor(result.Left, left.a),
                ToColor(result.Right, right.a),
                scheme.overrideLights,
                scheme.environmentColor0,
                scheme.environmentColor1,
                scheme.environmentColorW,
                scheme.supportsEnvironmentColorBoost,
                scheme.environmentColor0Boost,
                scheme.environmentColor1Boost,
                scheme.environmentColorWBoost,
                scheme.obstaclesColor);
            Produced.Add(corrected, null);
            return corrected;
        }

        internal static Rgb ToRgb(Color c) => new Rgb(Clamp01(c.r), Clamp01(c.g), Clamp01(c.b));

        internal static Color ToColor(Rgb c, float alpha) => new Color((float)c.R, (float)c.G, (float)c.B, alpha);

        internal static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        private static double Clamp01(float v) => v < 0f ? 0.0 : v > 1f ? 1.0 : v;
    }
}
